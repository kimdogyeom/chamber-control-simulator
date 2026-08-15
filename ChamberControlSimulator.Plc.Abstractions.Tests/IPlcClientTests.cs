using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class IPlcClientTests
{
	[TestMethod]
	public void WriteOutputsAsync_ReturnsTransportReceiptAndUsesCancellationToken()
	{
		var method = typeof(IPlcClient).GetMethod("WriteOutputsAsync");

		Assert.IsNotNull(method);
		Assert.AreEqual(typeof(Task<PlcWriteReceipt>), method.ReturnType);
		CollectionAssert.AreEqual(
			new[] { typeof(PlcOutputCommand), typeof(CancellationToken) },
			method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
	}

	[TestMethod]
	public void ReadInputsAsync_ReturnsSnapshotAndUsesCancellationToken()
	{
		var method = typeof(IPlcClient).GetMethod("ReadInputsAsync");

		Assert.IsNotNull(method);
		Assert.AreEqual(typeof(Task<PlcInputSnapshot>), method.ReturnType);
		CollectionAssert.AreEqual(
			new[] { typeof(CancellationToken) },
			method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
	}

	[TestMethod]
	[DataRow("ConnectAsync")]
	[DataRow("DisconnectAsync")]
	public void LifecycleOperation_UsesCancellationTokenAndTask(string operationName)
	{
		var method = typeof(IPlcClient).GetMethod(operationName);

		Assert.IsNotNull(method);
		Assert.AreEqual(typeof(Task), method.ReturnType);
		CollectionAssert.AreEqual(
			new[] { typeof(CancellationToken) },
			method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
	}

	[TestMethod]
	public void ConnectionState_UsesImmutableTransportState()
	{
		var property = typeof(IPlcClient).GetProperty("ConnectionState");

		Assert.IsNotNull(property);
		Assert.AreEqual(typeof(PlcConnectionState), property.PropertyType);
		Assert.IsNull(property.SetMethod);
	}

	[TestMethod]
	public void Contract_InheritsAsyncDisposable()
	{
		Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IPlcClient)));
	}
}
