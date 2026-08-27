using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Application;

public enum EquipmentCycleDisposition
{
	Completed,
	SkippedBusy,
	StaleObservation,
	TransportFailed
}

public enum ConnectionSynchronizationState
{
	Synchronized,
	WaitingForReconnect,
	WaitingForFreshInput,
	ReconnectExhausted
}

public enum ReconnectFailureKind
{
	None,
	TransportFailure,
	ConnectionNotEstablished,
	TimeProviderFailure,
	Canceled
}

public sealed record EquipmentCycleResult(
	EquipmentCycleDisposition Disposition,
	ControllerSnapshot ControllerSnapshot,
	PlcConnectionState ConnectionState,
	PlcInputSnapshot? InputSnapshot,
	ConnectionSynchronizationState SynchronizationState,
	int ReconnectAttemptCount,
	ReconnectFailureKind LastReconnectFailure);

public sealed class EquipmentCoordinator : IAsyncDisposable
{
	private readonly SemaphoreSlim _cycleGate = new(1, 1);
	private readonly ThermalController _controller;
	private readonly IPlcObservationPort _plcClient;
	private readonly TimeProvider _timeProvider;
	private readonly ReconnectPolicy _reconnectPolicy;
	private PlcSourceTransportIncarnation? _lastAcceptedSourceIncarnation;
	private long? _lastAcceptedObservationSequence;
	private bool _requireDifferentSourceIncarnation;
	private bool _requireStrictlyLaterSourceObservation;
	private long? _reconnectDelayStartedTimestamp;
	private int _reconnectAttemptCount;
	private ConnectionSynchronizationState _synchronizationState = ConnectionSynchronizationState.Synchronized;
	private ReconnectFailureKind _lastReconnectFailure = ReconnectFailureKind.None;
	private bool _reconnectEpochActive;
	private bool _disposed;

	public EquipmentCoordinator(
		ThermalController controller,
		IPlcObservationPort plcClient)
		: this(controller, plcClient, TimeProvider.System, ReconnectPolicy.Conservative)
	{
	}

	internal EquipmentCoordinator(
		ThermalController controller,
		IPlcObservationPort plcClient,
		TimeProvider timeProvider,
		ReconnectPolicy reconnectPolicy)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
		_plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_reconnectPolicy = reconnectPolicy ?? throw new ArgumentNullException(nameof(reconnectPolicy));
	}

	public async Task<EquipmentCycleResult> CycleAsync(
		TimeSpan elapsed,
		CancellationToken cancellationToken)
	{
		if (elapsed < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsed));
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!_cycleGate.Wait(0))
		{
			return CreateResult(EquipmentCycleDisposition.SkippedBusy, null);
		}

		try
		{
			ThrowIfDisposed();
			if (_reconnectEpochActive &&
				_lastReconnectFailure == ReconnectFailureKind.Canceled)
			{
				return TransportFailed();
			}
			if (_reconnectEpochActive &&
				(_synchronizationState == ConnectionSynchronizationState.ReconnectExhausted ||
				_reconnectAttemptCount >= _reconnectPolicy.MaximumAttemptCount))
			{
				_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
				_reconnectDelayStartedTimestamp = null;
				return TransportFailed();
			}
			if (_reconnectEpochActive && IsReconnectable(_plcClient.ConnectionState))
			{
				var reconnectResult = await TryReconnectAsync(cancellationToken);
				if (reconnectResult is not null)
				{
					return reconnectResult;
				}
			}
			else if (!_reconnectEpochActive && _plcClient.ConnectionState == PlcConnectionState.Disconnected)
			{
				try
				{
					await _plcClient.ConnectAsync(cancellationToken);
				}
				catch (PlcTransportException)
				{
					return TransportFailed();
				}
			}

			if (_plcClient.ConnectionState != PlcConnectionState.Connected &&
				_plcClient.ConnectionState != PlcConnectionState.Faulted)
			{
				return TransportFailed();
			}

			PlcInputSnapshot input;
			try
			{
				input = await _plcClient.ReadInputsAsync(cancellationToken);
			}
			catch (PlcTransportException)
			{
				_controller.ReportCommunicationLost();
				_requireDifferentSourceIncarnation = _lastAcceptedSourceIncarnation is not null;
				BeginReconnectEpoch();
				return TransportFailed();
			}

			if (!IsCurrentSourceObservation(input) || !IsSourceFreshObservation(input))
			{
				if (_synchronizationState != ConnectionSynchronizationState.ReconnectExhausted)
				{
					_synchronizationState = ConnectionSynchronizationState.WaitingForFreshInput;
				}

				return CreateResult(EquipmentCycleDisposition.StaleObservation, input);
			}

			_lastAcceptedSourceIncarnation = input.SourceTransportIncarnation;
			_lastAcceptedObservationSequence = input.ObservationSequence;
			_controller.ApplyObservation(
				new ThermalObservation(
					isDoorOpen: !input.DoorClosed,
					sensorHealthy: input.SensorHealthy,
					currentTemperature: input.CurrentTemperature),
				elapsed);
			CompleteSynchronization();

			return CreateResult(EquipmentCycleDisposition.Completed, input);
		}
		finally
		{
			_cycleGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _cycleGate.WaitAsync(CancellationToken.None);
		try
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			try
			{
				if (_plcClient.ConnectionState == PlcConnectionState.Connected)
				{
					await _plcClient.DisconnectAsync(CancellationToken.None);
				}
			}
			finally
			{
				await _plcClient.DisposeAsync();
			}
		}
		finally
		{
			_cycleGate.Release();
		}
	}

	internal void InvalidateSynchronizationAfterOutputTransportFailure()
	{
		ThrowIfDisposed();
		_requireStrictlyLaterSourceObservation = true;
		try
		{
			BeginReconnectEpoch();
			if (_plcClient.ConnectionState == PlcConnectionState.Connected)
			{
				_synchronizationState = ConnectionSynchronizationState.WaitingForFreshInput;
			}
		}
		catch (Exception exception)
		{
			System.Diagnostics.Trace.TraceError(exception.ToString());
		}
	}

	private async Task<EquipmentCycleResult?> TryReconnectAsync(CancellationToken cancellationToken)
	{
		if (_synchronizationState == ConnectionSynchronizationState.ReconnectExhausted ||
			_reconnectAttemptCount >= _reconnectPolicy.MaximumAttemptCount)
		{
			_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
			return TransportFailed();
		}

		var delayStartedTimestamp = _reconnectDelayStartedTimestamp ??
			throw new InvalidOperationException("An active reconnect epoch requires a delay timestamp.");
		var attemptStartedTimestamp = _timeProvider.GetTimestamp();
		var attemptNumber = _reconnectAttemptCount + 1;
		if (_timeProvider.GetElapsedTime(delayStartedTimestamp, attemptStartedTimestamp) <
			_reconnectPolicy.GetDelayBeforeAttempt(attemptNumber))
		{
			return TransportFailed();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsReconnectable(_plcClient.ConnectionState))
		{
			return null;
		}

		try
		{
			await _plcClient.ConnectAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
			_reconnectDelayStartedTimestamp = null;
			_lastReconnectFailure = ReconnectFailureKind.Canceled;
			throw;
		}
		catch (PlcTransportException)
		{
			RecordReconnectFailure(ReconnectFailureKind.TransportFailure);
			return TransportFailed();
		}

		_reconnectAttemptCount++;
		if (_plcClient.ConnectionState != PlcConnectionState.Connected)
		{
			RecordReconnectFailure(ReconnectFailureKind.ConnectionNotEstablished, false);
			return TransportFailed();
		}

		_synchronizationState = ConnectionSynchronizationState.WaitingForFreshInput;
		return null;
	}

	private void BeginReconnectEpoch()
	{
		if (_reconnectEpochActive &&
			_reconnectAttemptCount >= _reconnectPolicy.MaximumAttemptCount)
		{
			_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
			_reconnectDelayStartedTimestamp = null;
			return;
		}

		if (!_reconnectEpochActive)
		{
			_reconnectAttemptCount = 0;
		}

		_reconnectEpochActive = true;
		_reconnectDelayStartedTimestamp = null;
		_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
		_lastReconnectFailure = ReconnectFailureKind.TimeProviderFailure;
		var timestamp = _timeProvider.GetTimestamp();
		_reconnectDelayStartedTimestamp = timestamp;
		_synchronizationState = ConnectionSynchronizationState.WaitingForReconnect;
		_lastReconnectFailure = ReconnectFailureKind.None;
	}

	private void RecordReconnectFailure(
		ReconnectFailureKind failureKind,
		bool incrementAttemptCount = true)
	{
		if (incrementAttemptCount)
		{
			_reconnectAttemptCount++;
		}
		_lastReconnectFailure = failureKind;
		if (_reconnectAttemptCount >= _reconnectPolicy.MaximumAttemptCount)
		{
			_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
			_reconnectDelayStartedTimestamp = null;
			return;
		}

		_synchronizationState = ConnectionSynchronizationState.ReconnectExhausted;
		_reconnectDelayStartedTimestamp = null;
		_lastReconnectFailure = ReconnectFailureKind.TimeProviderFailure;
		var failureTimestamp = _timeProvider.GetTimestamp();
		_reconnectDelayStartedTimestamp = failureTimestamp;
		_synchronizationState = ConnectionSynchronizationState.WaitingForReconnect;
		_lastReconnectFailure = failureKind;
	}

	private void CompleteSynchronization()
	{
		_reconnectEpochActive = false;
		_reconnectDelayStartedTimestamp = null;
		_reconnectAttemptCount = 0;
		_requireDifferentSourceIncarnation = false;
		_requireStrictlyLaterSourceObservation = false;
		_synchronizationState = ConnectionSynchronizationState.Synchronized;
		_lastReconnectFailure = ReconnectFailureKind.None;
	}

	private bool IsCurrentSourceObservation(PlcInputSnapshot input) =>
		_plcClient.ConnectionState == PlcConnectionState.Connected &&
		_plcClient.CurrentSourceTransportIncarnation is { } current &&
		input.SourceTransportIncarnation == current;

	private bool IsSourceFreshObservation(PlcInputSnapshot input)
	{
		if (_requireDifferentSourceIncarnation &&
			_lastAcceptedSourceIncarnation is { } lastAccepted &&
			input.SourceTransportIncarnation == lastAccepted)
		{
			return false;
		}

		if (_lastAcceptedSourceIncarnation is { } accepted &&
			input.SourceTransportIncarnation == accepted &&
			_lastAcceptedObservationSequence is { } sequence &&
			input.ObservationSequence <= sequence)
		{
			return false;
		}

		if (_requireStrictlyLaterSourceObservation &&
			_lastAcceptedSourceIncarnation is { } barrierIncarnation &&
			input.SourceTransportIncarnation == barrierIncarnation &&
			_lastAcceptedObservationSequence is { } barrierSequence &&
			input.ObservationSequence <= barrierSequence)
		{
			return false;
		}

		return true;
	}

	private EquipmentCycleResult TransportFailed() =>
		CreateResult(EquipmentCycleDisposition.TransportFailed, null);

	private EquipmentCycleResult CreateResult(
		EquipmentCycleDisposition disposition,
		PlcInputSnapshot? inputSnapshot) => new(
			disposition,
			_controller.Snapshot,
			_plcClient.ConnectionState,
			inputSnapshot,
			_synchronizationState,
			_reconnectAttemptCount,
			_lastReconnectFailure);

	private static bool IsReconnectable(PlcConnectionState connectionState) =>
		connectionState == PlcConnectionState.Disconnected ||
		connectionState == PlcConnectionState.Faulted;

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
