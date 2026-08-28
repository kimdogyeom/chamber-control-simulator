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
		long observationSequence,
		PlcSourceTransportIncarnation sourceTransportIncarnation,
		bool heaterEnabled = false)
	{
		if (!double.IsFinite(currentTemperature))
		{
			throw new ArgumentOutOfRangeException(nameof(currentTemperature));
		}

		if (!Enum.IsDefined(machineState))
		{
			throw new ArgumentOutOfRangeException(nameof(machineState));
		}

		if (acknowledgedCommandId < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(acknowledgedCommandId));
		}

		if (observationSequence < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(observationSequence));
		}

		ArgumentNullException.ThrowIfNull(sourceTransportIncarnation);

		DoorClosed = doorClosed;
		SensorHealthy = sensorHealthy;
		CurrentTemperature = currentTemperature;
		MachineState = machineState;
		AcknowledgedCommandId = acknowledgedCommandId;
		ObservationSequence = observationSequence;
		SourceTransportIncarnation = sourceTransportIncarnation;
		HeaterEnabled = heaterEnabled;
	}

	public bool DoorClosed { get; }
	public bool SensorHealthy { get; }
	public double CurrentTemperature { get; }
	public PlcMachineState MachineState { get; }
	public long AcknowledgedCommandId { get; }
	public long ObservationSequence { get; }
	public PlcSourceTransportIncarnation SourceTransportIncarnation { get; }
	public bool HeaterEnabled { get; }
}
