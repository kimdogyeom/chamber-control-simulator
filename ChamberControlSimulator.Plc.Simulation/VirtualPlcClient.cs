using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Plc.Simulation;

public sealed class VirtualPlcClient : IPlcClient
{
	private readonly VirtualPlcOptions _options;
	private readonly List<PendingAcknowledgement> _pendingAcknowledgements = [];
	private bool _doorClosed = true;
	private bool _heaterEnabled;
	private bool _disposed;
	private double _currentTemperature;
	private long _acknowledgedCommandId;
	private long _nextObservationSequence;
	private TimeSpan _virtualTime;

	public VirtualPlcClient(VirtualPlcOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_currentTemperature = _options.InitialTemperature;
		SimulationControl = new VirtualPlcSimulationControl(this);
	}

	public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Disconnected;

	public VirtualPlcSimulationControl SimulationControl { get; }

	public Task ConnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		ConnectionState = PlcConnectionState.Connected;
		return Task.CompletedTask;
	}

	public Task DisconnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		ConnectionState = PlcConnectionState.Disconnected;
		return Task.CompletedTask;
	}

	public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		EnsureConnected();

		var snapshot = new PlcInputSnapshot(
			doorClosed: _doorClosed,
			sensorHealthy: true,
			currentTemperature: _currentTemperature,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: _acknowledgedCommandId,
			observationSequence: _nextObservationSequence++);

		return Task.FromResult(snapshot);
	}

	public Task<PlcWriteReceipt> WriteOutputsAsync(
		PlcOutputCommand command,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		EnsureConnected();

		if (command.Kind == PlcCommandKind.Start)
		{
			_heaterEnabled = true;
		}

		_pendingAcknowledgements.Add(new PendingAcknowledgement(
			command.CommandId,
			_virtualTime + _options.AcknowledgementDelay));

		return Task.FromResult(new PlcWriteReceipt(
			command.CommandId,
			PlcTransportWriteStatus.Written));
	}

	public ValueTask DisposeAsync()
	{
		_disposed = true;
		ConnectionState = PlcConnectionState.Disconnected;
		return ValueTask.CompletedTask;
	}

	internal void Advance(TimeSpan elapsed)
	{
		ThrowIfDisposed();

		if (elapsed < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsed));
		}

		_virtualTime += elapsed;

		if (_heaterEnabled)
		{
			_currentTemperature += _options.HeatingRatePerSecond * elapsed.TotalSeconds;
		}

		AcknowledgeDueCommands();
	}

	internal void SetDoorClosed(bool doorClosed)
	{
		ThrowIfDisposed();
		_doorClosed = doorClosed;
	}

	private void AcknowledgeDueCommands()
	{
		for (var index = 0; index < _pendingAcknowledgements.Count;)
		{
			var pending = _pendingAcknowledgements[index];
			if (pending.DueAt > _virtualTime)
			{
				index++;
				continue;
			}

			_acknowledgedCommandId = pending.CommandId;
			_pendingAcknowledgements.RemoveAt(index);
		}
	}

	private void EnsureConnected()
	{
		ThrowIfDisposed();

		if (ConnectionState != PlcConnectionState.Connected)
		{
			throw new InvalidOperationException("Virtual PLC transport is not connected.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private sealed record PendingAcknowledgement(
		long CommandId,
		TimeSpan DueAt);
}
