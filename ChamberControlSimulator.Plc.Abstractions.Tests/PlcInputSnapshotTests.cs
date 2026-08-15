using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcInputSnapshotTests
{
	[TestMethod]
	public void Constructor_ValidObservation_ExposesImmutableSnapshot()
	{
		var snapshot = new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20.5,
			machineState: PlcMachineState.Running,
			acknowledgedCommandId: 7,
			observationSequence: 11);

		Assert.IsTrue(snapshot.DoorClosed);
		Assert.IsTrue(snapshot.SensorHealthy);
		Assert.AreEqual(20.5, snapshot.CurrentTemperature);
		Assert.AreEqual(PlcMachineState.Running, snapshot.MachineState);
		Assert.AreEqual(7L, snapshot.AcknowledgedCommandId);
		Assert.AreEqual(11L, snapshot.ObservationSequence);
		Assert.IsFalse(typeof(PlcInputSnapshot).GetProperties().Any(property => property.SetMethod?.IsPublic == true));
	}

	[TestMethod]
	public void MachineState_DefinesEquipmentStatesInOrder()
	{
		CollectionAssert.AreEqual(
			new[]
			{
				PlcMachineState.Idle,
				PlcMachineState.Running,
				PlcMachineState.Faulted
			},
			Enum.GetValues<PlcMachineState>());
	}
}
