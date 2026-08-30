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
	private readonly ThermalController _controller;
	private EquipmentCommandLifecycleState _currentState = new(
		EquipmentCommandLifecycleDisposition.NoCommand,
		null,
		null,
		null,
		null);
	private bool _isAutomatic;
	private EquipmentCycleResult? _latestObservationResult;
	private PlcSourceTransportIncarnation? _preDispatchSourceIncarnation;
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
		TimeProvider timeProvider,
		ReconnectPolicy? reconnectPolicy = null)
	{
		ArgumentNullException.ThrowIfNull(controller);
		_controller = controller;
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_observationCoordinator = new EquipmentCoordinator(
			controller,
			observationPort ?? throw new ArgumentNullException(nameof(observationPort)),
			_timeProvider,
			reconnectPolicy ?? ReconnectPolicy.Conservative);
		_commandCoordinator = new EquipmentCommandCoordinator(
			controller,
			outputPort ?? throw new ArgumentNullException(nameof(outputPort)));
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
			TryAdmitAutomaticCompleteStopWhileHoldingGate();
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

	public Task<EquipmentCommandRequestResult> RequestAbortAsync(CancellationToken cancellationToken) =>
		RequestCommandAsync(ControllerCommandKind.Abort, cancellationToken);

	private async Task<EquipmentCommandRequestResult> RequestCommandAsync(
		ControllerCommandKind kind,
		CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		var isAbort = kind == ControllerCommandKind.Abort;
		if (Volatile.Read(ref _acceptingAdmission) == 0)
		{
			return RejectRequest(kind, EquipmentCommandRejectionReason.AdmissionClosed);
		}

		if (!isAbort && HasOutstandingCommand(CurrentState))
		{
			return RejectRequest(kind, EquipmentCommandRejectionReason.OutstandingCommand);
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		var releaseGate = true;
		Task<EquipmentCommandTransportResult>? writeTask = null;
		try
		{
			ThrowIfDisposed();
			if (Volatile.Read(ref _acceptingAdmission) == 0)
			{
				return RejectRequest(kind, EquipmentCommandRejectionReason.AdmissionClosed);
			}

			if (!isAbort && HasOutstandingCommand(CurrentState))
			{
				return RejectRequest(kind, EquipmentCommandRejectionReason.OutstandingCommand);
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

			var admission = isAbort
				? _commandCoordinator.TryAdmitAbortPreempting(baseline.InputSnapshot.AcknowledgedCommandId)
				: _commandCoordinator.TryAdmitAfter(kind, baseline.InputSnapshot.AcknowledgedCommandId);
			if (admission.Disposition != EquipmentCommandAdmissionDisposition.Accepted || admission.Admission is null)
			{
				var reason = admission.Disposition == EquipmentCommandAdmissionDisposition.Busy
					? EquipmentCommandRejectionReason.OutstandingCommand
					: EquipmentCommandRejectionReason.CoreIneligible;
				return RejectRequest(kind, reason);
			}

			_pendingCommandId = admission.Admission.CommandId;
			_pendingCommandKind = admission.Admission.Kind;
			_preDispatchSourceIncarnation = baseline.InputSnapshot.SourceTransportIncarnation;
			_preDispatchObservationSequence = baseline.InputSnapshot.ObservationSequence;
			_writeInvokedTimestamp = _timeProvider.GetTimestamp();
			_acknowledgementStartedTimestamp = null;
			if (kind != ControllerCommandKind.Stop)
			{
				_isAutomatic = false;
			}
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
			EquipmentCommandTransportResult transport;
			try
			{
				transport = await writeTask.ConfigureAwait(false);
			}
			catch (PlcTransportException)
			{
				_controller.ReportCommunicationLost();
				_observationCoordinator.InvalidateSynchronizationAfterOutputTransportFailure();
				if (HasReceiptDeadlineElapsed())
				{
					SetTerminalState(EquipmentCommandLifecycleDisposition.ReceiptTimedOut);
					return new EquipmentCommandRequestResult(CurrentState.Disposition, _pendingCommandId);
				}

				throw;
			}
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
		catch (Exception)
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

	private EquipmentCommandRequestResult RejectRequest(
		ControllerCommandKind kind,
		EquipmentCommandRejectionReason reason)
	{
		_isAutomatic = false;
		SetRejectedState(kind, reason);
		return new EquipmentCommandRequestResult(
			EquipmentCommandLifecycleDisposition.AdmissionRejected,
			null,
			reason);
	}

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
			_preDispatchSourceIncarnation is null ||
			_preDispatchObservationSequence is null ||
			observationResult.InputSnapshot.SourceTransportIncarnation != _preDispatchSourceIncarnation ||
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
		ControllerCommandKind? kind,
		EquipmentCommandRejectionReason rejectionReason = EquipmentCommandRejectionReason.None,
		ControllerCommandKind? rejectedKind = null)
	{
		Volatile.Write(
			ref _currentState,
			new EquipmentCommandLifecycleState(
				disposition,
				commandId,
				kind,
				_writeInvokedTimestamp,
				_acknowledgementStartedTimestamp,
				_isAutomatic,
				rejectionReason,
				rejectedKind));
	}

	private void SetRejectedState(ControllerCommandKind kind, EquipmentCommandRejectionReason reason)
	{
		var current = CurrentState;
		if (HasOutstandingCommand(current))
		{
			SetState(current.Disposition, current.CommandId, current.Kind, reason, kind);
			return;
		}

		SetState(
			EquipmentCommandLifecycleDisposition.AdmissionRejected,
			null,
			null,
			reason,
			kind);
	}

	private void SetTerminalState(EquipmentCommandLifecycleDisposition disposition) =>
		SetState(disposition, _pendingCommandId, _pendingCommandKind);

	private static bool HasOutstandingCommand(EquipmentCommandLifecycleState state) =>
		state.CommandId is not null && state.Disposition != EquipmentCommandLifecycleDisposition.Completed;

	private void ClearCompletedCommand()
	{
		_pendingCommandId = null;
		_pendingCommandKind = null;
		_preDispatchSourceIncarnation = null;
		_preDispatchObservationSequence = null;
		_writeInvokedTimestamp = null;
		_acknowledgementStartedTimestamp = null;
		_isAutomatic = false;
	}

	private void TryAdmitAutomaticCompleteStopWhileHoldingGate()
	{
		if (Volatile.Read(ref _acceptingAdmission) == 0 || HasOutstandingCommand(CurrentState))
		{
			return;
		}

		var observation = _latestObservationResult;
		if (observation is null ||
			observation.Disposition != EquipmentCycleDisposition.Completed ||
			observation.InputSnapshot is null ||
			!observation.InputSnapshot.HeaterEnabled)
		{
			return;
		}

		var controllerState = observation.ControllerSnapshot.State;
		if (controllerState is not ControllerState.Complete and not ControllerState.Cooling)
		{
			return;
		}

		_isAutomatic = true;
		var admission = _commandCoordinator.TryAdmitAfter(
			ControllerCommandKind.Stop,
			observation.InputSnapshot.AcknowledgedCommandId);
		if (admission.Disposition != EquipmentCommandAdmissionDisposition.Accepted || admission.Admission is null)
		{
			_isAutomatic = false;
			SetRejectedState(
				ControllerCommandKind.Stop,
				admission.Disposition == EquipmentCommandAdmissionDisposition.Busy
					? EquipmentCommandRejectionReason.OutstandingCommand
					: EquipmentCommandRejectionReason.CoreIneligible);
			return;
		}

		_pendingCommandId = admission.Admission.CommandId;
		_pendingCommandKind = admission.Admission.Kind;
		_preDispatchSourceIncarnation = observation.InputSnapshot.SourceTransportIncarnation;
		_preDispatchObservationSequence = observation.InputSnapshot.ObservationSequence;
		_writeInvokedTimestamp = _timeProvider.GetTimestamp();
		_acknowledgementStartedTimestamp = null;
		SetState(
			EquipmentCommandLifecycleDisposition.Writing,
			_pendingCommandId,
			_pendingCommandKind);
		try
		{
			var transport = _commandCoordinator.DispatchPendingAsync(CancellationToken.None)
				.GetAwaiter()
				.GetResult();
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
		}
		catch (PlcTransportException)
		{
			_controller.ReportCommunicationLost();
			_observationCoordinator.InvalidateSynchronizationAfterOutputTransportFailure();
			SetTerminalState(EquipmentCommandLifecycleDisposition.ReconciliationRequired);
		}
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
			if (exception is PlcTransportException)
			{
				_controller.ReportCommunicationLost();
				_observationCoordinator.InvalidateSynchronizationAfterOutputTransportFailure();
			}

			System.Diagnostics.Trace.TraceError(exception.ToString());
		}
		finally
		{
			_gate.Release();
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
