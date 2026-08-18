using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class IPlcClientTests
{
	// 목적: compatibility client가 observation과 output capability를 composition만 하고 자체 broad member를 선언하지 않는지 검증한다.
	// 예상 결과: IPlcClient는 IPlcObservationPort와 IPlcOutputPort를 상속하고 declared member는 없다.
	// 완료 조건: Application이 필요한 capability별 narrow port를 주입할 수 있다.
	[TestMethod]
	public void Contract_ComposesObservationAndOutputPortsWithoutDeclaredMembers()
	{
		var inherited = typeof(IPlcClient).GetInterfaces();
		var declaredMembers = typeof(IPlcClient).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		CollectionAssert.Contains(inherited, typeof(IPlcObservationPort));
		CollectionAssert.Contains(inherited, typeof(IPlcOutputPort));
		Assert.IsEmpty(declaredMembers);
	}

	// 목적: observation port가 output write authority를 얻지 않았는지 검증한다.
	// 예상 결과: IPlcObservationPort member에는 WriteOutputsAsync가 없다.
	// 완료 조건: P3 read-only coordinator dependency가 observation-only로 유지된다.
	[TestMethod]
	public void ObservationPort_RemainsWithoutOutputWriteCapability()
	{
		Assert.IsNull(typeof(IPlcObservationPort).GetMethod("WriteOutputsAsync"));
	}
}
