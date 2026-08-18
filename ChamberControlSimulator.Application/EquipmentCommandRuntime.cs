using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Application;

public enum EquipmentCommandLifecycleDisposition
{
	NoCommand,
	BaselineRequired,
	AdmissionRejected,
	AwaitingAcknowledgement,
	ReconciliationRequired,
	Completed,
	AcknowledgedButCoreIneligible
}

public sealed record EquipmentCommandRequestResult(
	EquipmentCommandLifecycleDisposition Disposition,
	long? CommandId);

public sealed record EquipmentCommandCycleResult(
	EquipmentCycleResult ObservationResult,
	EquipmentCommandLifecycleDisposition CommandDisposition,
	long? CommandId);

public sealed class EquipmentCommandRuntime : IAsyncDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly EquipmentCoordinator _observationCoordinator;
	private readonly EquipmentCommandCoordinator _commandCoordinator;
	private EquipmentCycleResult? _latestObservationResult;
	private EquipmentCommandLifecycleDisposition _commandDisposition = EquipmentCommandLifecycleDisposition.NoCommand;
	private long? _preDispatchObservationSequence;
	private long? _pendingCommandId;
	private bool _disposed;

	public EquipmentCommandRuntime(
		ThermalController controller,
		IPlcObservationPort observationPort,
		IPlcOutputPort outputPort)
	{
		ArgumentNullException.ThrowIfNull(controller);
		_observationCoordinator = new EquipmentCoordinator(
			controller,
			observationPort ?? throw new ArgumentNullException(nameof(observationPort)));
		_commandCoordinator = new EquipmentCommandCoordinator(
			controller,
			outputPort ?? throw new ArgumentNullException(nameof(outputPort)));
	}

	public async Task<EquipmentCommandCycleResult> CycleAsync(
		TimeSpan elapsed,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();
			var observationResult = await _observationCoordinator
				.CycleAsync(elapsed, cancellationToken)
				.ConfigureAwait(false);
			_latestObservationResult = observationResult;
			EvaluateAcknowledgement(observationResult);
			return new EquipmentCommandCycleResult(
				observationResult,
				_commandDisposition,
				_pendingCommandId);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();
			var baseline = _latestObservationResult;
			if (baseline is null ||
				baseline.Disposition != EquipmentCycleDisposition.Completed ||
				baseline.InputSnapshot is null)
			{
				return new EquipmentCommandRequestResult(
					EquipmentCommandLifecycleDisposition.BaselineRequired,
					null);
			}

			var admission = _commandCoordinator.TryAdmitAfter(
				ControllerCommandKind.Start,
				baseline.InputSnapshot.AcknowledgedCommandId);
			if (admission.Disposition != EquipmentCommandAdmissionDisposition.Accepted || admission.Admission is null)
			{
				return new EquipmentCommandRequestResult(
					EquipmentCommandLifecycleDisposition.AdmissionRejected,
					null);
			}

			_pendingCommandId = admission.Admission.CommandId;
			_preDispatchObservationSequence = baseline.InputSnapshot.ObservationSequence;
			try
			{
				var transport = await _commandCoordinator
					.DispatchPendingAsync(cancellationToken)
					.ConfigureAwait(false);
				_commandDisposition = transport.Disposition == EquipmentCommandTransportDisposition.AwaitingAcknowledgement
					? EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement
					: EquipmentCommandLifecycleDisposition.ReconciliationRequired;
			}
			catch
			{
				_commandDisposition = EquipmentCommandLifecycleDisposition.ReconciliationRequired;
				throw;
			}

			return new EquipmentCommandRequestResult(_commandDisposition, _pendingCommandId);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
		try
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			await _observationCoordinator.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private void EvaluateAcknowledgement(EquipmentCycleResult observationResult)
	{
		if (_pendingCommandId is null ||
			_commandDisposition is EquipmentCommandLifecycleDisposition.Completed or
				EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible or
				EquipmentCommandLifecycleDisposition.ReconciliationRequired ||
			observationResult.Disposition != EquipmentCycleDisposition.Completed ||
			observationResult.InputSnapshot is null ||
			_preDispatchObservationSequence is null ||
			observationResult.InputSnapshot.ObservationSequence <= _preDispatchObservationSequence.Value)
		{
			return;
		}

		var acknowledgedCommandId = observationResult.InputSnapshot.AcknowledgedCommandId;
		if (acknowledgedCommandId < _pendingCommandId.Value)
		{
			return;
		}

		if (acknowledgedCommandId > _pendingCommandId.Value)
		{
			_commandDisposition = EquipmentCommandLifecycleDisposition.ReconciliationRequired;
			return;
		}

		_commandDisposition = _commandCoordinator.TryCompleteAcknowledgedStart(_pendingCommandId.Value)
			? EquipmentCommandLifecycleDisposition.Completed
			: EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible;
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
