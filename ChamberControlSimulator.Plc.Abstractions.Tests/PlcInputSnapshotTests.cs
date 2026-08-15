using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcInputSnapshotTests
{
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
