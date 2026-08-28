using ChamberControlSimulator.Application;
using ChamberControlSimulator.Plc.Abstractions;
using ChamberControlSimulator.Plc.Simulation;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentObservationRuntime : IEquipmentObservationRuntime
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

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_transportSimulation?.Advance(elapsed);
			return await _commandRuntime.CycleAsync(elapsed, cancellationToken).ConfigureAwait(false);
		}

		public ValueTask DisposeAsync() => _commandRuntime.DisposeAsync();
	}
}
