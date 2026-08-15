namespace ChamberControlSimulator.Plc.Abstractions;

public enum PlcMachineState
{
	Idle,
	Running,
	Faulted
}

public sealed record PlcInputSnapshot
{
	public PlcInputSnapshot(
		bool doorClosed,
		bool sensorHealthy,
		double currentTemperature,
		PlcMachineState machineState,
		long acknowledgedCommandId,
		long observationSequence)
	{
		DoorClosed = doorClosed;
		SensorHealthy = sensorHealthy;
		CurrentTemperature = currentTemperature;
		MachineState = machineState;
		AcknowledgedCommandId = acknowledgedCommandId;
		ObservationSequence = observationSequence;
	}

	public bool DoorClosed { get; }
	public bool SensorHealthy { get; }
	public double CurrentTemperature { get; }
	public PlcMachineState MachineState { get; }
	public long AcknowledgedCommandId { get; }
	public long ObservationSequence { get; }
}
