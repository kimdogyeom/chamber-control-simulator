using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class IPlcOutputPortTests
{
	// 목적: output capability가 typed one-shot write 하나만 노출하는지 검증한다.
	// 예상 결과: exact command/token parameters와 Task<PlcWriteReceipt> return을 가진 WriteOutputsAsync 하나가 존재한다.
	// 완료 조건: command dispatch가 broad client 없이 transport receipt를 받을 수 있다.
	[TestMethod]
	public void Contract_ExposesExactlyOneTypedWriteOperation()
	{
		var methods = typeof(IPlcOutputPort).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		Assert.HasCount(1, methods);
		Assert.AreEqual("WriteOutputsAsync", methods[0].Name);
		Assert.AreEqual(typeof(Task<PlcWriteReceipt>), methods[0].ReturnType);
		CollectionAssert.AreEqual(
			new[] { typeof(PlcOutputCommand), typeof(CancellationToken) },
			methods[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
	}

	// 목적: narrow output port가 input, connection, disposal, observation, virtual-control capability를 상속하거나 선언하지 않는지 검증한다.
	// 예상 결과: inherited interface와 property/event는 없고 declared member는 exact write method뿐이다.
	// 완료 조건: output-only fake와 production injection이 P3 input 또는 lifecycle authority를 얻지 않는다.
	[TestMethod]
	public void Contract_HasNoInputConnectionDisposalOrVirtualControlSurface()
	{
		Assert.IsEmpty(typeof(IPlcOutputPort).GetInterfaces());
		Assert.IsEmpty(typeof(IPlcOutputPort).GetProperties());
		Assert.IsEmpty(typeof(IPlcOutputPort).GetEvents());
		Assert.IsFalse(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IPlcOutputPort)));
		Assert.IsNull(typeof(IPlcOutputPort).GetMethod("ReadInputsAsync"));
		Assert.IsNull(typeof(IPlcOutputPort).GetMethod("ConnectAsync"));
		Assert.IsNull(typeof(IPlcOutputPort).GetMethod("DisconnectAsync"));
	}
}
