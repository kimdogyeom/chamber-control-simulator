using ChamberControlSimulator.Application;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentObservationRuntime : IEquipmentObservationRuntime, IEquipmentCommandRuntime
	{
		private readonly EquipmentCommandRuntime _commandRuntime;
		private readonly IPlcObservationInputControl _simulationControl;

		public EquipmentObservationRuntime(
			EquipmentCommandRuntime commandRuntime,
			IPlcObservationInputControl simulationControl)
		{
			_commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
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
			await _commandRuntime.CycleAsync(elapsed, cancellationToken);
		}

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestStartAsync(cancellationToken);

		public void StopAdmission() => _commandRuntime.StopAdmission();

		public ValueTask DisposeAsync() => _commandRuntime.DisposeAsync();
	}
}
