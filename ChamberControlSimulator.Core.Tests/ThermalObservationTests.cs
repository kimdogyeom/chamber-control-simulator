using ChamberControlSimulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Core.Tests;

[TestClass]
public sealed class ThermalObservationTests
{
	// 목적: 하나의 external observation에 포함된 open-door input이 active thermal phase에서 interlock alarm으로 적용되는지 검증한다.
	// 예상 결과: Heating controller에 door-open observation을 적용하면 DoorOpen alarm이 된다.
	// 완료 조건: PLC 타입 없이 Core-owned observation만으로 door safety mapping이 test를 통과한다.
	[TestMethod]
	public void ApplyObservation_WhenDoorIsOpenDuringHeating_RaisesDoorOpenAlarm()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);

		controller.ApplyObservation(
			new ThermalObservation(
				isDoorOpen: true,
				sensorHealthy: true,
				currentTemperature: 20d),
			TimeSpan.Zero);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.DoorOpen, controller.Snapshot.ActiveAlarm);
	}

	// 목적: external temperature observation이 safety threshold에서 over-temperature alarm을 발생시키는지 검증한다.
	// 예상 결과: heating 중 safety temperature 이상의 observation을 적용하면 OverTemperature alarm이 된다.
	// 완료 조건: Core가 synthetic heating 값이 아닌 supplied observation으로 safety policy를 판단해 test가 통과한다.
	[TestMethod]
	public void ApplyObservation_AtSafetyTemperature_RaisesOverTemperatureAlarm()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);

		controller.ApplyObservation(
			new ThermalObservation(
				isDoorOpen: false,
				sensorHealthy: true,
				currentTemperature: 35d),
			TimeSpan.Zero);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.OverTemperature, controller.Snapshot.ActiveAlarm);
	}

	// 목적: sensor timeout recovery가 acknowledgement 뒤 새 healthy observation과 positive elapsed를 요구하는지 검증한다.
	// 예상 결과: zero elapsed healthy observation은 recovery하지 못하고 positive elapsed healthy observation만 Recovery로 전환한다.
	// 완료 조건: fresh observation requirement가 fragmented pause/resume call 없이 deterministic test로 통과한다.
	[TestMethod]
	public void ApplyObservation_AfterSensorTimeout_RequiresFreshHealthyObservationBeforeRecovery()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		var unhealthy = new ThermalObservation(false, false, 20d);
		var healthy = new ThermalObservation(false, true, 20d);

		controller.ApplyObservation(unhealthy, TimeSpan.FromSeconds(3));
		controller.AcknowledgeAlarm();
		controller.ApplyObservation(healthy, TimeSpan.Zero);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);

		controller.ApplyObservation(healthy, TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.CanReset);
	}
}
