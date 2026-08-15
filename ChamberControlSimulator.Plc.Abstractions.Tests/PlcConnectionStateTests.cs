using ChamberControlSimulator.Plc.Abstractions;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcConnectionStateTests
{
	[TestMethod]
	public void Values_DefineTransportConnectionLifecycleInOrder()
	{
		CollectionAssert.AreEqual(
			new[]
			{
				PlcConnectionState.Disconnected,
				PlcConnectionState.Connecting,
				PlcConnectionState.Connected,
				PlcConnectionState.Faulted
			},
			Enum.GetValues<PlcConnectionState>());
	}
}
