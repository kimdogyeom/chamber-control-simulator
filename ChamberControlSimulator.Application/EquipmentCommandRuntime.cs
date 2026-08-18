using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Application;

public sealed class EquipmentCommandRuntime : IAsyncDisposable
{
	private static readonly TimeSpan ReceiptDeadline = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan AcknowledgementDeadline = TimeSpan.FromSeconds(3);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly EquipmentCoordinator _observationCoordinator;
	private readonly EquipmentCommandCoordinator _commandCoordinator;
	private readonly TimeProvider _timeProvider;
	private EquipmentCommandLifecycleState _currentState = new(
		EquipmentCommandLifecycleDisposition.NoCommand,
		null,
		null,
		null,
		null);
	private EquipmentCycleResult? _latestObservationResult;
	private long? _preDispatchObservationSequence;
	private long? _pendingCommandId;
	private ControllerCommandKind? _pendingCommandKind;
	private long? _writeInvokedTimestamp;
	private long? _acknowledgementStartedTimestamp;
	private int _acceptingAdmission = 1;
	private bool _disposed;

	public EquipmentCommandRuntime(
		ThermalController controller,
		IPlcObservationPort observationPort,
		IPlcOutputPort outputPort,
		TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(controller);
		_observationCoordinator = new EquipmentCoordinator(
			controller,
			observationPort ?? throw new ArgumentNullException(nameof(observationPort)));
		_commandCoordinator = new EquipmentCommandCoordinator(
			controller,
			outputPort ?? throw new ArgumentNullException(nameof(outputPort)));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
	}

	public EquipmentCommandLifecycleState CurrentState => Volatile.Read(ref _currentState);

	public void StopAdmission() => Interlocked.Exchange(ref _acceptingAdmission, 0);

	public async Task<EquipmentCommandCycleResult> CycleAsync(
		TimeSpan elapsed,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();
			ExpireAcknowledgementIfDue();
			EquipmentCycleResult observationResult;
			try
			{
				observationResult = await _observationCoordinator
					.CycleAsync(elapsed, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (_pendingCommandId is not null)
			{
				if (CurrentState.Disposition == EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement)
				{
					SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
				}

				throw;
			}

			_latestObservationResult = observationResult;
			ExpireAcknowledgementIfDue();
			EvaluateAcknowledgement(observationResult);
			var state = CurrentState;
			return new EquipmentCommandCycleResult(
				observationResult,
				state.Disposition,
				state.CommandId);
		}
		finally
		{
			_gate.Release();
		}
	}

	public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
		RequestCommandAsync(ControllerCommandKind.Start, cancellationToken);

	public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
		RequestCommandAsync(ControllerCommandKind.Stop, cancellationToken);

	public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
		RequestCommandAsync(ControllerCommandKind.Reset, cancellationToken);

	private async Task<EquipmentCommandRequestResult> RequestCommandAsync(
		ControllerCommandKind kind,
		CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		if (Volatile.Read(ref _acceptingAdmission) == 0 || HasOutstandingCommand(CurrentState))
		{
			return RejectedRequest();
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		var releaseGate = true;
		Task<EquipmentCommandTransportResult>? writeTask = null;
		try
		{
			ThrowIfDisposed();
			if (Volatile.Read(ref _acceptingAdmission) == 0 || HasOutstandingCommand(CurrentState))
			{
				return RejectedRequest();
			}

			var baseline = _latestObservationResult;
			if (baseline is null ||
				baseline.Disposition != EquipmentCycleDisposition.Completed ||
				baseline.InputSnapshot is null)
			{
				SetState(EquipmentCommandLifecycleDisposition.BaselineRequired, null, null);
				return new EquipmentCommandRequestResult(
					EquipmentCommandLifecycleDisposition.BaselineRequired,
					null);
			}

			var admission = _commandCoordinator.TryAdmitAfter(
				kind,
				baseline.InputSnapshot.AcknowledgedCommandId);
			if (admission.Disposition != EquipmentCommandAdmissionDisposition.Accepted || admission.Admission is null)
			{
				SetState(EquipmentCommandLifecycleDisposition.AdmissionRejected, null, null);
				return RejectedRequest();
			}

			_pendingCommandId = admission.Admission.CommandId;
			_pendingCommandKind = admission.Admission.Kind;
			_preDispatchObservationSequence = baseline.InputSnapshot.ObservationSequence;
			_writeInvokedTimestamp = _timeProvider.GetTimestamp();
			_acknowledgementStartedTimestamp = null;
			SetState(
				EquipmentCommandLifecycleDisposition.Writing,
				_pendingCommandId,
				_pendingCommandKind);

			using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var deadlineTask = Task.Delay(ReceiptDeadline, _timeProvider, deadlineCancellation.Token);
			writeTask = _commandCoordinator.DispatchPendingAsync(cancellationToken);
			var completedTask = await Task.WhenAny(writeTask, deadlineTask).ConfigureAwait(false);
			if (!writeTask.IsCompleted && completedTask == deadlineTask)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
					HandOffGateUntilWriteSettles(writeTask);
					releaseGate = false;
					await deadlineTask.ConfigureAwait(false);
				}

				SetTerminalState(EquipmentCommandLifecycleDisposition.ReceiptTimedOut);
				HandOffGateUntilWriteSettles(writeTask);
				releaseGate = false;
				return new EquipmentCommandRequestResult(CurrentState.Disposition, _pendingCommandId);
			}

			deadlineCancellation.Cancel();
			var transport = await writeTask.ConfigureAwait(false);
			if (HasReceiptDeadlineElapsed())
			{
				SetTerminalState(EquipmentCommandLifecycleDisposition.ReceiptTimedOut);
				return new EquipmentCommandRequestResult(CurrentState.Disposition, _pendingCommandId);
			}

			if (cancellationToken.IsCancellationRequested)
			{
				throw new TaskCanceledException("The command write was canceled after admission.", null, cancellationToken);
			}
			if (transport.Disposition == EquipmentCommandTransportDisposition.AwaitingAcknowledgement)
			{
				_acknowledgementStartedTimestamp = _timeProvider.GetTimestamp();
				SetState(
					EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement,
					_pendingCommandId,
					_pendingCommandKind);
			}
			else
			{
				SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
			}

			return new EquipmentCommandRequestResult(CurrentState.Disposition, _pendingCommandId);
		}
		catch
		{
			if (_pendingCommandId is not null)
			{
				SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
				if (releaseGate && writeTask is { IsCompleted: false })
				{
					HandOffGateUntilWriteSettles(writeTask);
					releaseGate = false;
				}
			}

			throw;
		}
		finally
		{
			if (releaseGate)
			{
				_gate.Release();
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		StopAdmission();
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

	private EquipmentCommandRequestResult RejectedRequest() => new(
		EquipmentCommandLifecycleDisposition.AdmissionRejected,
		null);

	private bool HasReceiptDeadlineElapsed() =>
		_writeInvokedTimestamp is not null &&
		_timeProvider.GetElapsedTime(
			_writeInvokedTimestamp.Value,
			_timeProvider.GetTimestamp()) >= ReceiptDeadline;

	private void ExpireAcknowledgementIfDue()
	{
		if (CurrentState.Disposition != EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement ||
			_acknowledgementStartedTimestamp is null)
		{
			return;
		}

		if (_timeProvider.GetElapsedTime(
			_acknowledgementStartedTimestamp.Value,
			_timeProvider.GetTimestamp()) >= AcknowledgementDeadline)
		{
			SetTerminalState(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut);
		}
	}

	private void EvaluateAcknowledgement(EquipmentCycleResult observationResult)
	{
		if (_pendingCommandId is null ||
			CurrentState.Disposition != EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement ||
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
			SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
			return;
		}

		if (_commandCoordinator.TryCompleteAcknowledgedCommand(_pendingCommandId.Value))
		{
			SetTerminalState(EquipmentCommandLifecycleDisposition.Completed);
			ClearCompletedCommand();
		}
		else
		{
			SetTerminalState(EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible);
		}
	}

	private void SetState(
		EquipmentCommandLifecycleDisposition disposition,
		long? commandId,
		ControllerCommandKind? kind)
	{
		Volatile.Write(
			ref _currentState,
			new EquipmentCommandLifecycleState(
				disposition,
				commandId,
				kind,
				_writeInvokedTimestamp,
				_acknowledgementStartedTimestamp));
	}

	private void SetTerminalState(EquipmentCommandLifecycleDisposition disposition) =>
		SetState(disposition, _pendingCommandId, _pendingCommandKind);

	private static bool HasOutstandingCommand(EquipmentCommandLifecycleState state) =>
		state.CommandId is not null && state.Disposition != EquipmentCommandLifecycleDisposition.Completed;

	private void ClearCompletedCommand()
	{
		_pendingCommandId = null;
		_pendingCommandKind = null;
		_preDispatchObservationSequence = null;
		_writeInvokedTimestamp = null;
		_acknowledgementStartedTimestamp = null;
	}

	private void HandOffGateUntilWriteSettles(Task<EquipmentCommandTransportResult> writeTask) =>
		_ = ReleaseGateAfterWriteSettlesAsync(writeTask);

	private async Task ReleaseGateAfterWriteSettlesAsync(Task<EquipmentCommandTransportResult> writeTask)
	{
		try
		{
			await writeTask.ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			System.Diagnostics.Trace.TraceError(exception.ToString());
		}
		finally
		{
			_gate.Release();
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
