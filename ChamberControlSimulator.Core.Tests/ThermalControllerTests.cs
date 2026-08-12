using Microsoft.VisualStudio.TestTools.UnitTesting;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Core.Tests;

[TestClass]
public sealed class ThermalControllerTests
{
	[TestMethod]
	public void Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);

		controller.Start();
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		controller.Tick(TimeSpan.FromSeconds(2));
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		controller.Tick(TimeSpan.FromSeconds(2.99));
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		controller.Tick(TimeSpan.FromSeconds(0.01));
		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
		controller.Tick(TimeSpan.FromSeconds(2));

		Assert.AreEqual(ControllerState.Complete, controller.Snapshot.State);
		CollectionAssert.AreEqual(
			new[] { "Start", "Phase: Precheck", "Phase: Heating", "Phase: Holding", "Phase: Cooling", "Phase: Complete" },
			controller.EventHistory.Select(entry => entry.Event).ToArray());
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
}