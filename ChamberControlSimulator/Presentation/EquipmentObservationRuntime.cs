using ChamberControlSimulator.Application;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly EquipmentCoordinator _coordinator;
		private readonly IPlcObservationInputControl _simulationControl;

		public EquipmentObservationRuntime(
			EquipmentCoordinator coordinator,
			IPlcObservationInputControl simulationControl)
		{
			_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
			_simulationControl = simulationControl ?? throw new ArgumentNullException(nameof(simulationControl));
		}

		public void SetCurrentTemperature(double currentTemperature)
		{
			_simulationControl.SetCurrentTemperature(currentTemperature);
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
			_simulationControl.SetSensorHealthy(sensorHealthy);
		}

		public void SetDoorClosed(bool doorClosed)
		{
			_simulationControl.SetDoorClosed(doorClosed);
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			await _coordinator.CycleAsync(elapsed, cancellationToken);
		}

		public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
	}
}
