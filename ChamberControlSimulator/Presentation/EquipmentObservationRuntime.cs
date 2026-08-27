using ChamberControlSimulator.Application;
using ChamberControlSimulator.Plc.Abstractions;
using ChamberControlSimulator.Plc.Simulation;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentObservationRuntime : IEquipmentObservationRuntime, IEquipmentCommandRuntime
	{
		private readonly EquipmentCommandRuntime _commandRuntime;
		private readonly IPlcObservationInputControl _simulationControl;
		private readonly VirtualPlcSimulationControl? _transportSimulation;

		public EquipmentObservationRuntime(
			EquipmentCommandRuntime commandRuntime,
			IPlcObservationInputControl simulationControl,
			VirtualPlcSimulationControl? transportSimulation = null)
		{
			_commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
			_simulationControl = simulationControl ?? throw new ArgumentNullException(nameof(simulationControl));
			_transportSimulation = transportSimulation;
		}

		public EquipmentCommandLifecycleState CurrentState => _commandRuntime.CurrentState;

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

		public void SuppressNextAcknowledgement()
		{
			_transportSimulation?.SuppressNextAcknowledgement();
		}

		public void ForceTransportDisconnect()
		{
			_transportSimulation?.ForceTransportDisconnect();
		}

		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken) =>
			_commandRuntime.CycleAsync(elapsed, cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestStartAsync(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestStopAsync(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestResetAsync(cancellationToken);

		public void StopAdmission() => _commandRuntime.StopAdmission();

		public ValueTask DisposeAsync() => _commandRuntime.DisposeAsync();
	}
}
