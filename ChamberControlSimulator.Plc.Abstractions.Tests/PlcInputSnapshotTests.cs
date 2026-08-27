using System.Reflection;

using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcInputSnapshotTests
{
	private static readonly PlcSourceTransportIncarnation TestSource =
		new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

	// 목적: 관측 스냅샷이 소스가 발급한 불투명 transport incarnation을 필수 불변 값으로 노출하는지 검증한다.
	// 예상 결과: 비어 있지 않은 Guid incarnation이 스냅샷 속성과 같고 public setter가 없다.
	// 완료 조건: 로컬 시각·카운터 기본값 없이 명시적 source identity만 수용한다.
	[TestMethod]
	public void Constructor_ValidSourceTransportIncarnation_ExposesImmutableIdentity()
	{
		var incarnation = new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		var snapshot = new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: false,
			currentTemperature: 20.5,
			machineState: PlcMachineState.Running,
			acknowledgedCommandId: 7,
			observationSequence: 11,
			sourceTransportIncarnation: incarnation);

		Assert.AreEqual(incarnation, snapshot.SourceTransportIncarnation);
		Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), snapshot.SourceTransportIncarnation.Value);
		Assert.IsFalse(typeof(PlcSourceTransportIncarnation).GetProperties().Any(property => property.SetMethod?.IsPublic == true));
		Assert.IsFalse(typeof(PlcInputSnapshot).GetProperties().Any(property => property.SetMethod?.IsPublic == true));
	}

	// 목적: 비어 있는 Guid는 source transport incarnation으로 쓸 수 없는지 검증한다.
	// 예상 결과: Guid.Empty는 ArgumentOutOfRangeException을 던지고 ParamName은 value다.
	// 완료 조건: Empty identity를 snapshot 또는 port 기본값으로 복원하지 않는다.
	[TestMethod]
	public void Constructor_EmptySourceTransportIncarnation_Throws()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new PlcSourceTransportIncarnation(Guid.Empty));

		Assert.AreEqual("value", exception.ParamName);
	}

	// 목적: observation port가 현재 연결된 source incarnation을 nullable 계약으로 노출하는지 검증한다.
	// 예상 결과: CurrentSourceTransportIncarnation 속성은 PlcSourceTransportIncarnation? 이고 public setter가 없다.
	// 완료 조건: Connected가 아닌 상태에서 유효 identity를 요구하는 선택적 기본 구현이 없다.
	[TestMethod]
	public void ObservationPort_ExposesNullableCurrentSourceTransportIncarnation()
	{
		var property = typeof(IPlcObservationPort).GetProperty(nameof(IPlcObservationPort.CurrentSourceTransportIncarnation));

		Assert.IsNotNull(property);
		Assert.AreEqual(typeof(PlcSourceTransportIncarnation), property.PropertyType);
		Assert.AreEqual(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
		Assert.IsNull(property.SetMethod);
		Assert.IsNull(typeof(IPlcObservationPort).GetMethod("WriteOutputsAsync"));
	}

	[TestMethod]
	public void Constructor_ValidObservation_ExposesImmutableSnapshot()
	{
		var snapshot = new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: false,
			currentTemperature: 20.5,
			machineState: PlcMachineState.Running,
			acknowledgedCommandId: 7,
			observationSequence: 11,
			sourceTransportIncarnation: TestSource);

		Assert.IsTrue(snapshot.DoorClosed);
		Assert.IsFalse(snapshot.SensorHealthy);
		Assert.AreEqual(20.5, snapshot.CurrentTemperature);
		Assert.AreEqual(PlcMachineState.Running, snapshot.MachineState);
		Assert.AreEqual(7L, snapshot.AcknowledgedCommandId);
		Assert.AreEqual(11L, snapshot.ObservationSequence);
		Assert.AreEqual(TestSource, snapshot.SourceTransportIncarnation);
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
				observationSequence: 0,
				sourceTransportIncarnation: TestSource));

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
			observationSequence: 0,
			sourceTransportIncarnation: TestSource);
		Assert.AreEqual(0L, initialObservation.ObservationSequence);

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: -1,
			sourceTransportIncarnation: TestSource));
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
			observationSequence: 0,
			sourceTransportIncarnation: TestSource);
		Assert.AreEqual(0L, noAcknowledgement.AcknowledgedCommandId);

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: -1,
			observationSequence: 0,
			sourceTransportIncarnation: TestSource));
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
			observationSequence: 0,
			sourceTransportIncarnation: TestSource));

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
