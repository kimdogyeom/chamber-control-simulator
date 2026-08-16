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

public sealed record EquipmentCycleResult(
	EquipmentCycleDisposition Disposition,
	ControllerSnapshot ControllerSnapshot,
	PlcConnectionState ConnectionState,
	PlcInputSnapshot? InputSnapshot);

public sealed class EquipmentCoordinator : IAsyncDisposable
{
	private readonly ThermalController _controller;
	private readonly IPlcClient _plcClient;
	private long? _lastAcceptedObservationSequence;
	private bool _disposed;

	public EquipmentCoordinator(
		ThermalController controller,
		IPlcClient plcClient)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
		_plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
	}

	public async Task<EquipmentCycleResult> CycleAsync(
		TimeSpan elapsed,
		CancellationToken cancellationToken)
	{
		if (elapsed < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsed));
		}

		ThrowIfDisposed();

		try
		{
			if (_plcClient.ConnectionState == PlcConnectionState.Disconnected)
			{
				await _plcClient.ConnectAsync(cancellationToken);
			}

			if (_plcClient.ConnectionState != PlcConnectionState.Connected)
			{
				return TransportFailed();
			}

			var input = await _plcClient.ReadInputsAsync(cancellationToken);
			if (_lastAcceptedObservationSequence is not null &&
				input.ObservationSequence <= _lastAcceptedObservationSequence.Value)
			{
				return new EquipmentCycleResult(
					EquipmentCycleDisposition.StaleObservation,
					_controller.Snapshot,
					_plcClient.ConnectionState,
					input);
			}

			_lastAcceptedObservationSequence = input.ObservationSequence;
			_controller.ApplyObservation(
				new ThermalObservation(
					isDoorOpen: !input.DoorClosed,
					sensorHealthy: input.SensorHealthy,
					currentTemperature: input.CurrentTemperature),
				elapsed);

			return new EquipmentCycleResult(
				EquipmentCycleDisposition.Completed,
				_controller.Snapshot,
				_plcClient.ConnectionState,
				input);
		}
		catch (InvalidOperationException)
		{
			return TransportFailed();
		}
	}

	public async ValueTask DisposeAsync()
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

	private EquipmentCycleResult TransportFailed() => new(
		EquipmentCycleDisposition.TransportFailed,
		_controller.Snapshot,
		_plcClient.ConnectionState,
		null);

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
