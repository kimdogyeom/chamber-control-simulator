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
			sensorHealthy: false,
			currentTemperature: 20.5,
			machineState: PlcMachineState.Running,
			acknowledgedCommandId: 7,
			observationSequence: 11);

		Assert.IsTrue(snapshot.DoorClosed);
		Assert.IsFalse(snapshot.SensorHealthy);
		Assert.AreEqual(20.5, snapshot.CurrentTemperature);
		Assert.AreEqual(PlcMachineState.Running, snapshot.MachineState);
		Assert.AreEqual(7L, snapshot.AcknowledgedCommandId);
		Assert.AreEqual(11L, snapshot.ObservationSequence);
		Assert.IsFalse(typeof(PlcInputSnapshot).GetProperties().Any(property => property.SetMethod?.IsPublic == true));
	}

	[TestMethod]
	public void Constructor_NonFiniteTemperature_Throws()
	{
		foreach (var temperature in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
		{
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
				doorClosed: true,
				sensorHealthy: true,
				currentTemperature: temperature,
				machineState: PlcMachineState.Idle,
				acknowledgedCommandId: 0,
				observationSequence: 0));

			Assert.AreEqual("currentTemperature", exception.ParamName);
		}
	}

	[TestMethod]
	public void Constructor_ObservationSequence_MustBeNonNegative()
	{
		var initialObservation = new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0);
		Assert.AreEqual(0L, initialObservation.ObservationSequence);

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: -1));
		Assert.AreEqual("observationSequence", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_AcknowledgedCommandId_MustBeNonNegative()
	{
		var noAcknowledgement = new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0);
		Assert.AreEqual(0L, noAcknowledgement.AcknowledgedCommandId);

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: -1,
			observationSequence: 0));
		Assert.AreEqual("acknowledgedCommandId", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_UndefinedMachineState_Throws()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: (PlcMachineState)99,
			acknowledgedCommandId: 0,
			observationSequence: 0));

		Assert.AreEqual("machineState", exception.ParamName);
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
