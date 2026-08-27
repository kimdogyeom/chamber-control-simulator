using Microsoft.VisualStudio.TestTools.UnitTesting;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Core.Tests;

[TestClass]
public sealed class ThermalControllerTests
{
	// 목적: Core가 supplied ThermalObservation sequence에서만 정상 phase를 진행하는지 검증한다.
	// 예상 결과: target, hold elapsed, ambient observation 순서에 따라 Heating → Holding → Cooling → Complete가 된다.
	// 완료 조건: Tick의 synthetic temperature change 없이 phase와 event sequence가 통과한다.
	[TestMethod]
	public void ApplyObservation_ValidObservedSequence_ProgressesThroughNormalPhasesToComplete()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var initial = new ThermalObservation(isDoorOpen: false, sensorHealthy: true, currentTemperature: 20d);
		var target = new ThermalObservation(isDoorOpen: false, sensorHealthy: true, currentTemperature: 30d);
		var ambient = new ThermalObservation(isDoorOpen: false, sensorHealthy: true, currentTemperature: 20d);
		controller.ApplyObservation(initial, TimeSpan.Zero);

		controller.Start();
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);

		controller.ApplyObservation(target, TimeSpan.Zero);
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		controller.ApplyObservation(target, TimeSpan.FromSeconds(2.99));
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		controller.ApplyObservation(target, TimeSpan.FromSeconds(0.01));
		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
		controller.ApplyObservation(ambient, TimeSpan.Zero);

		Assert.AreEqual(ControllerState.Complete, controller.Snapshot.State);
		CollectionAssert.AreEqual(
			new[] { "Start", "Phase: Precheck", "Phase: Heating", "Phase: Holding", "Phase: Cooling", "Phase: Complete" },
			controller.EventHistory.Select(entry => entry.Event).ToArray());
	}

	// 목적: external observation 없이 timer tick만 발생할 때 Core가 plant temperature를 합성하거나 thermal phase timer를 진행하지 않는지 검증한다.
	// 예상 결과: Heating, Holding, Cooling의 온도와 phase는 Tick 후에도 마지막 external observation state로 유지된다.
	// 완료 조건: Core의 5°C/s heating/cooling 및 configured-duration Tick-only Holding → Cooling transition이 제거된 뒤 test가 통과한다.
	[TestMethod]
	public void Tick_WithoutExternalObservation_DoesNotSynthesizeTemperatureOrAdvancePhase()
	{
		var holdDuration = TimeSpan.FromSeconds(3);
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d, holdDuration: holdDuration),
			SimulationSettings.Illustrative);
		var observation = new ThermalObservation(
			isDoorOpen: false,
			sensorHealthy: true,
			currentTemperature: 20d);
		var targetObservation = new ThermalObservation(
			isDoorOpen: false,
			sensorHealthy: true,
			currentTemperature: 30d);
		controller.ApplyObservation(observation, TimeSpan.Zero);
		controller.Start();

		controller.Tick(TimeSpan.FromSeconds(2));

		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.AreEqual(20d, controller.Snapshot.CurrentTemperature);

		controller.ApplyObservation(targetObservation, TimeSpan.Zero);
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);

		controller.Tick(holdDuration);

		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		Assert.AreEqual(30d, controller.Snapshot.CurrentTemperature);

		controller.ApplyObservation(targetObservation, holdDuration);
		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);

		controller.Tick(TimeSpan.FromSeconds(2));

		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
		Assert.AreEqual(30d, controller.Snapshot.CurrentTemperature);
	}

	[TestMethod]
	public void Start_WhenDoorIsOpen_RemainsIdleAndIsIneligible()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);

		controller.SetDoorOpen(true);
		controller.Start();

		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanStart);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}

	[TestMethod]
	public void OpenDoor_WhileHeating_EntersDoorOpenAlarm()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();

		controller.SetDoorOpen(true);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.DoorOpen, controller.Snapshot.ActiveAlarm);
		Assert.IsTrue(controller.Snapshot.CanAcknowledge);
		Assert.IsFalse(controller.Snapshot.CanReset);
		Assert.AreEqual("Alarm: DoorOpen", controller.EventHistory[^1].Event);
	}

	[TestMethod]
	public void Reset_AfterDoorAlarmIsClosedAndAcknowledged_ReturnsToIdle()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.SetDoorOpen(true);

		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.IsRecoveryReady);
		Assert.IsTrue(controller.Snapshot.CanReset);

		controller.Reset();

		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
		CollectionAssert.AreEqual(
			new[] { "Acknowledgement", "Recovery ready", "Reset" },
			controller.EventHistory.TakeLast(3).Select(entry => entry.Event).ToArray());
	}

	[TestMethod]
	public void ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();

		controller.ReportTemperature(35);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.OverTemperature, controller.Snapshot.ActiveAlarm);

		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);

		controller.ReportTemperature(34);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.CanReset);

		controller.Reset();
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	[TestMethod]
	public void FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();

		controller.PauseFeedback();
		controller.Tick(TimeSpan.FromSeconds(3.1));
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.SensorTimeout, controller.Snapshot.ActiveAlarm);

		controller.AcknowledgeAlarm();
		controller.ResumeFeedback();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);

		controller.Tick(TimeSpan.FromMilliseconds(1));
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.CanReset);

		controller.Reset();
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	[TestMethod]
	public void Stop_WhenHeating_ReturnsIdleAndPreservesSessionHistory()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();

		controller.Stop();

		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.AreEqual("Stop", controller.EventHistory[^1].Event);
		CollectionAssert.Contains(controller.EventHistory.Select(entry => entry.Event).ToList(), "Start");
	}

	[TestMethod]
	public void EventHistory_CannotBeMutatedOutsideTheController()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);

		var entries = (IList<EventLogEntry>)controller.EventHistory;

		Assert.ThrowsExactly<NotSupportedException>(() => entries.Add(new EventLogEntry(TimeSpan.Zero, ControllerState.Idle, "Injected", null)));
		Assert.IsEmpty(controller.EventHistory);
	}

	[TestMethod]
	public void DoorOpenThenAtSafetyTemperature_TracksBothInterlocksBeforeRecovery()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.SetDoorOpen(true);
		controller.ReportTemperature(35);

		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);
		CollectionAssert.AreEqual(
			new[] { "Alarm: DoorOpen", "Alarm: OverTemperature" },
			controller.EventHistory.Where(entry => entry.Event.StartsWith("Alarm:")).Select(entry => entry.Event).ToArray());

		controller.ReportTemperature(34);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
	}
	[TestMethod]
	public void SensorTimeoutThenDoorOpen_TracksBothInterlocksBeforeRecovery()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start(); controller.PauseFeedback(); controller.Tick(TimeSpan.FromSeconds(3));
		controller.SetDoorOpen(true); controller.AcknowledgeAlarm(); controller.ResumeFeedback(); controller.Tick(TimeSpan.FromMilliseconds(1));
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		CollectionAssert.AreEqual(new[] { "Alarm: SensorTimeout", "Alarm: DoorOpen" }, controller.EventHistory.Where(entry => entry.Event.StartsWith("Alarm:")).Select(entry => entry.Event).ToArray());
		controller.SetDoorOpen(false);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
	}
	[TestMethod]
	public void RecipeAndSimulationSettings_RejectNonFiniteTemperatures()
	{
		foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
		{
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Recipe(value, 35));
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Recipe(30, value));
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SimulationSettings(value, TimeSpan.FromSeconds(3)));
		}
	}
	[TestMethod]
	public void Start_WhenCurrentTemperatureIsAtSafety_RemainsIdleUntilSafeTemperatureReported()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.ReportTemperature(35); controller.Start();
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State); Assert.IsFalse(controller.Snapshot.CanStart);
		controller.ReportTemperature(34); controller.Start();
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
	}
	[TestMethod]
	public void FeedbackTimeout_AtExactBoundaryRequiresPositiveFreshTickAfterResume()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start(); controller.PauseFeedback(); controller.Tick(TimeSpan.FromSeconds(3)); controller.AcknowledgeAlarm(); controller.ResumeFeedback(); controller.Tick(TimeSpan.Zero);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State); Assert.IsFalse(controller.Snapshot.CanReset);
		controller.Tick(TimeSpan.FromMilliseconds(1)); Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
	}
	[TestMethod]
	public void Stop_WhenAlarmed_PreservesAlarmAndBlocksRestart()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start(); controller.SetDoorOpen(true); controller.Stop(); controller.Start();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State); Assert.AreEqual(AlarmKind.DoorOpen, controller.Snapshot.ActiveAlarm); Assert.IsFalse(controller.Snapshot.CanStart);
	}
	[TestMethod]
	public void Reset_PreservesEntirePreResetSessionHistory()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start(); controller.SetDoorOpen(true); var beforeReset = controller.EventHistory.ToArray(); controller.SetDoorOpen(false); controller.AcknowledgeAlarm(); controller.Reset();
		CollectionAssert.AreEqual(beforeReset, controller.EventHistory.Take(beforeReset.Length).ToArray()); Assert.AreEqual("Reset", controller.EventHistory[^1].Event);
	}

	[TestMethod]
	public void Reset_AfterDoorOpenAlarm_AllowsDoorOpenAlarmInNewRun()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.SetDoorOpen(true);

		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		controller.Reset();

		controller.Start();
		controller.SetDoorOpen(true);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.DoorOpen, controller.Snapshot.ActiveAlarm);
	}
	[TestMethod]
	public void DoorOpen_ReassertedFromRecovery_ReturnsToAlarmAndBlocksReset()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);

		controller.SetDoorOpen(true);

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);
	}
	[TestMethod]
	public void OverTemperature_ReassertedFromRecovery_RequiresNewAcknowledgementAndClearCycleBeforeReset()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.ReportTemperature(35);
		controller.AcknowledgeAlarm();
		controller.ReportTemperature(34);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);

		controller.ReportTemperature(35);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.OverTemperature, controller.Snapshot.ActiveAlarm);
		Assert.IsFalse(controller.Snapshot.CanReset);
		Assert.AreEqual("Alarm reasserted: OverTemperature", controller.EventHistory[^1].Event);

		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);

		controller.ReportTemperature(34);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.CanReset);
	}

	[TestMethod]
	public void SensorTimeout_ReassertedFromRecovery_RequiresNewAcknowledgementAndClearCycleBeforeReset()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.PauseFeedback();
		controller.Tick(TimeSpan.FromSeconds(3.1));
		controller.AcknowledgeAlarm();
		controller.ResumeFeedback();
		controller.Tick(TimeSpan.FromMilliseconds(1));
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);

		controller.PauseFeedback();
		controller.Tick(TimeSpan.FromSeconds(3.1));
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.SensorTimeout, controller.Snapshot.ActiveAlarm);
		Assert.IsFalse(controller.Snapshot.CanReset);
		Assert.AreEqual("Alarm reasserted: SensorTimeout", controller.EventHistory[^1].Event);

		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.CanReset);

		controller.ResumeFeedback();
		controller.Tick(TimeSpan.FromMilliseconds(1));
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.CanReset);
	}

	[TestMethod]
	public void SelectRecipe_WhenIdle_ActivatesSelectedRecipe()
	{
		var standard = new Recipe("Standard 250째C", 250, 300);
		var highTemperature = new Recipe("High Temp 300째C", 300, 350);
		var controller = new ThermalController([standard, highTemperature], SimulationSettings.Illustrative);

		var selected = controller.SelectRecipe(highTemperature.Name);

		Assert.IsTrue(selected);
		Assert.AreEqual(highTemperature.Name, controller.Snapshot.RecipeName);
		Assert.AreEqual(highTemperature.TargetTemperature, controller.Snapshot.TargetTemperature);
		Assert.AreEqual("Recipe selected: High Temp 300째C", controller.EventHistory[^1].Event);
	}

	[TestMethod]
	public void SelectRecipe_WhenHeating_KeepsActiveRecipe()
	{
		var standard = new Recipe("Standard 250째C", 250, 300);
		var highTemperature = new Recipe("High Temp 300째C", 300, 350);
		var controller = new ThermalController([standard, highTemperature], SimulationSettings.Illustrative);
		controller.Start();

		var selected = controller.SelectRecipe(highTemperature.Name);

		Assert.IsFalse(selected);
		Assert.AreEqual(standard.Name, controller.Snapshot.RecipeName);
		Assert.AreEqual(standard.TargetTemperature, controller.Snapshot.TargetTemperature);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Recipe selected: High Temp 300째C"));
	}
	[TestMethod]

	public void FeedbackPaused_AfterTimeout_DoesNotRepeatedlyReassertSensorTimeout()
	{
		var controller = new ThermalController(new Recipe(30, 35),SimulationSettings.Illustrative);

		// Arrange
		controller.Start();
		controller.PauseFeedback();

		// Act
		controller.Tick(TimeSpan.FromSeconds(3));
		controller.Tick(TimeSpan.FromMilliseconds(250));
		controller.Tick(TimeSpan.FromMilliseconds(250));

		// Assert
		// SensorTimeout Alarm 이벤트는 최초 1개여야 한다.
		var SensorTimeoutEvents = controller.EventHistory
			.Where(entry => entry.Alarm == AlarmKind.SensorTimeout)
			.ToList();
		Assert.HasCount(1, SensorTimeoutEvents, "SensorTimeout Alarm 이벤트는 최초 1개여야 한다.");
	}

	// 목적: 통신 손실 보고가 유휴 상태를 변경하지 않고 안전 감시 중에는 지속 알람을 발생시키는지 검증한다.
	// 예상 결과: 유휴 컨트롤러에는 이벤트가 없고 가열 중 컨트롤러는 CommunicationLost 알람에 머문다.
	// 완료 조건: 확인, Stop, Reset 후에도 알람 상태와 Reset 불가 조건이 유지된다.
	[TestMethod]
	public void ReportCommunicationLost_OnlySafetyMonitoredControllerRaisesNonBypassableAlarm()
	{
		var idleController = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var heatingController = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		heatingController.Start();

		idleController.ReportCommunicationLost();
		heatingController.ReportCommunicationLost();

		Assert.AreEqual(ControllerState.Idle, idleController.Snapshot.State);
		Assert.IsNull(idleController.Snapshot.ActiveAlarm);
		Assert.IsEmpty(idleController.EventHistory);
		Assert.AreEqual(ControllerState.Alarm, heatingController.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, heatingController.Snapshot.ActiveAlarm);
		Assert.IsFalse(heatingController.Snapshot.CanReset);

		var eventCountBeforeBypassAttempts = heatingController.EventHistory.Count;
		heatingController.AcknowledgeAlarm();
		heatingController.Stop();
		heatingController.Reset();

		Assert.AreEqual(ControllerState.Alarm, heatingController.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, heatingController.Snapshot.ActiveAlarm);
		Assert.IsFalse(heatingController.Snapshot.CanReset);
		Assert.HasCount(eventCountBeforeBypassAttempts + 1, heatingController.EventHistory);
		Assert.AreEqual("Acknowledgement", heatingController.EventHistory[^1].Event);
	}

	// 목적: 통신 손실 뒤 자격 있는 안전 증거와 그 다음 Acknowledge만 Recovery-ready를 만드는지 검증한다.
	// 예상 결과: 증거 전 Acknowledge는 Alarm이고, 안전 증거 뒤 새 Acknowledge만 Recovery이며 Reset은 호출되지 않는다.
	// 완료 조건: CommunicationLost pending 조건이 증거 후에만 해제되고 CanReset은 true여도 Reset 횟수는 0이다.
	[TestMethod]
	public void ReportFreshSafeCommunicationEvidence_ThenNewAcknowledge_ReachesRecoveryReadyWithoutReset()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.ReportCommunicationLost();
		controller.AcknowledgeAlarm();

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
		Assert.IsFalse(controller.Snapshot.IsRecoveryReady);

		controller.ReportFreshSafeCommunicationEvidence();
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.IsRecoveryReady);

		controller.AcknowledgeAlarm();

		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsTrue(controller.Snapshot.IsRecoveryReady);
		Assert.IsTrue(controller.Snapshot.CanReset);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Reset"));
	}

	// 목적: 통신 손실 뒤 문 열림 같은 불안전 입력에서는 Acknowledge해도 Recovery-ready가 되지 않는지 검증한다.
	// 예상 결과: DoorOpen이 남아 Alarm이며 IsRecoveryReady는 false다.
	// 완료 조건: Reset은 호출되지 않고 CommunicationLost 단독 해소로 Recovery에 들어가지 않는다.
	[TestMethod]
	public void AcknowledgeAlarm_AfterCommunicationLostWithOpenDoor_DoesNotBecomeRecoveryReady()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.ReportCommunicationLost();
		controller.ApplyObservation(
			new ThermalObservation(isDoorOpen: true, sensorHealthy: true, currentTemperature: 20d),
			TimeSpan.Zero);
		controller.ReportFreshSafeCommunicationEvidence();
		controller.AcknowledgeAlarm();

		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.IsFalse(controller.Snapshot.IsRecoveryReady);
		Assert.IsFalse(controller.Snapshot.CanReset);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Reset"));
	}
}
