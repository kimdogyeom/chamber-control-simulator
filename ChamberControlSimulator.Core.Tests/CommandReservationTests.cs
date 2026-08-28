using ChamberControlSimulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ChamberControlSimulator.Core.Tests;

[TestClass]
public sealed class CommandReservationTests
{
	// 목적: eligible Start 요청이 opaque Core reservation만 만들고 semantic ACK route가 아직 없는 T1에서 상태나 event를 바꾸지 않는지 검증한다.
	// 예상 결과: Start reservation은 반환되며 Snapshot은 Idle, EventHistory는 비어 있다.
	// 완료 조건: reservation 자체가 Start transition이나 PLC-facing command를 만들지 않은 채 test가 통과한다.
	[TestMethod]
	public void TryReserveCommand_Start_LeavesControllerIdleWithoutStateTransition()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);

		var reservation = controller.TryReserveCommand(ControllerCommandKind.Start);

		Assert.IsNotNull(reservation);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: still-eligible reservation이 남아 있을 때 duplicate reservation도 새 authority나 state/event 변화를 만들지 않는지 검증한다.
	// 예상 결과: 두 번째 Start reservation은 null이고 controller는 Idle 및 empty event history를 유지한다.
	// 완료 조건: unsafe invalidation이 없어도 one-shot Core fence가 replacement를 거절함을 보장한다.
	[TestMethod]
	public void TryReserveCommand_WhenExistingReservationRemainsEligible_RejectsDuplicateWithoutChangingController()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		Assert.IsNotNull(controller.TryReserveCommand(ControllerCommandKind.Start));

		var duplicateReservation = controller.TryReserveCommand(ControllerCommandKind.Start);

		Assert.IsNull(duplicateReservation);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: active controller의 Stop intent도 예약만으로는 현재 phase나 event를 바꾸지 않는지 검증한다.
	// 예상 결과: Stop reservation 직후 Heating이 유지되고 Stop event는 추가되지 않는다.
	// 완료 조건: Start 외 command도 T1에서 semantic application 없이 transition하지 않는다.
	[TestMethod]
	public void TryReserveCommand_Stop_LeavesActiveControllerUnchanged()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		var eventCountBeforeReservation = controller.EventHistory.Count;

		var reservation = controller.TryReserveCommand(ControllerCommandKind.Stop);

		Assert.IsNotNull(reservation);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.HasCount(eventCountBeforeReservation, controller.EventHistory);
	}

	// 목적: Recovery-ready Reset intent도 예약만으로는 Idle로 전환하지 않는지 검증한다.
	// 예상 결과: Reset reservation 직후 Recovery가 유지되고 Reset event는 추가되지 않는다.
	// 완료 조건: Reset이 T1에서 command uncertainty 또는 semantic ACK boundary를 우회하지 않는다.
	[TestMethod]
	public void TryReserveCommand_Reset_LeavesRecoveryControllerUnchanged()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		var eventCountBeforeReservation = controller.EventHistory.Count;

		var reservation = controller.TryReserveCommand(ControllerCommandKind.Reset);

		Assert.IsNotNull(reservation);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.HasCount(eventCountBeforeReservation, controller.EventHistory);
	}

	// 목적: unsafe intervening observation이 reservation을 release하지 않고 fail-closed pending fence로 남기는지 검증한다.
	// 예상 결과: door-open 후 다시 닫아도 replacement reservation과 legacy Start 모두 허용되지 않는다.
	// 완료 조건: unsafe interval 뒤 late ACK/retry가 새 command ID나 Core Start를 만들 수 있는 T1 loophole가 없다.
	[TestMethod]
	public void TryReserveCommand_AfterUnsafeInterveningObservation_RemainsPendingAndBlocksReplacement()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Start);
		Assert.IsNotNull(reservation);

		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		var replacementReservation = controller.TryReserveCommand(ControllerCommandKind.Start);
		ThermalControllerTestCommands.CompleteStart(controller);

		Assert.IsNull(replacementReservation);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: outstanding reservation이 있을 때 legacy Start seam이 semantic ACK 이전 transition을 bypass하지 않는지 검증한다.
	// 예상 결과: direct Start 호출 뒤에도 Idle과 empty event history가 유지된다.
	// 완료 조건: Presenter migration 전 compatibility method가 T1 reservation authority를 침범하지 않는다.
	[TestMethod]
	public void Start_WhenReservationIsOutstanding_DoesNotBypassReservationFence()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		Assert.IsNotNull(controller.TryReserveCommand(ControllerCommandKind.Start));
		Assert.IsNull(typeof(ThermalController).GetMethod("Start"));
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: T3 completion seam이 public generic reservation apply/release authority로 노출되지 않는지 검증한다.
	// 예상 결과: reservation public constructor/property가 없고 completion method는 non-public instance member다.
	// 완료 조건: external caller가 public reservation token만으로 Start/Stop/Reset을 complete하거나 fence를 release할 수 없다.
	[TestMethod]
	public void ReservationContract_ExposesOnlyNonPublicAcknowledgedCompletionAuthority()
	{
		var reservationType = typeof(ControllerCommandReservation);
		var controllerType = typeof(ThermalController);
		var completion = controllerType.GetMethod(
			"TryCompleteAcknowledgedCommand",
			BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.IsEmpty(reservationType.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
		Assert.IsEmpty(reservationType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
		Assert.IsNotNull(completion);
		Assert.IsFalse(completion.IsPublic);
		Assert.IsNull(controllerType.GetMethod("TryCompleteAcknowledgedCommand", BindingFlags.Instance | BindingFlags.Public));
		Assert.IsNull(controllerType.GetMethod("InvalidateCommandReservation", BindingFlags.Instance | BindingFlags.Public));
	}

	// 목적: owned active Start reservation이 acknowledged completion seam에서 eligibility를 재검증하고 정확히 한 번 소비되는지 검증한다.
	// 예상 결과: first completion만 true이며 Core는 Heating과 one Start event로 전환하고 duplicate completion은 false다.
	// 완료 조건: exact ACK 이후 one-shot Core transition과 reservation consumption이 Core 자체 규칙으로 보장된다.
	[TestMethod]
	public void TryCompleteAcknowledgedCommand_EligibleOwnedStart_CompletesExactlyOnce()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Start);
		Assert.IsNotNull(reservation);

		var first = controller.TryCompleteAcknowledgedCommand(reservation);
		var second = controller.TryCompleteAcknowledgedCommand(reservation);

		Assert.IsTrue(first);
		Assert.IsFalse(second);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.HasCount(3, controller.EventHistory);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Start"));
	}

	// 목적: owned Stop reservation이 semantic completion에서 정확히 한 번 적용되고 fence를 소비하는지 검증한다.
	// 예상 결과: first completion만 true이고 Core는 Idle과 one Stop event로 전환한다.
	// 완료 조건: Stop도 exact ACK authority에서만 one-shot transition한다.
	[TestMethod]
	public void TryCompleteAcknowledgedCommand_EligibleOwnedStop_CompletesExactlyOnce()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Stop);
		Assert.IsNotNull(reservation);

		var first = controller.TryCompleteAcknowledgedCommand(reservation);
		var second = controller.TryCompleteAcknowledgedCommand(reservation);

		Assert.IsTrue(first);
		Assert.IsFalse(second);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Stop"));
	}

	// 목적: Recovery-ready Reset reservation이 semantic completion에서 정확히 한 번 적용되는지 검증한다.
	// 예상 결과: first completion만 true이고 Core는 Idle과 one Reset event로 전환한다.
	// 완료 조건: Reset이 Recovery eligibility와 exact ACK completion을 우회하지 않는다.
	[TestMethod]
	public void TryCompleteAcknowledgedCommand_EligibleOwnedReset_CompletesExactlyOnce()
	{
		var controller = CreateRecoveryReadyController();
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Reset);
		Assert.IsNotNull(reservation);

		var first = controller.TryCompleteAcknowledgedCommand(reservation);
		var second = controller.TryCompleteAcknowledgedCommand(reservation);

		Assert.IsTrue(first);
		Assert.IsFalse(second);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Reset"));
	}

	// 목적: unsafe door interval로 invalidated된 Start reservation이 safe ABA 뒤 acknowledged 되어도 revive되지 않는지 검증한다.
	// 예상 결과: completion은 false, Core는 Idle/event-empty, original fence는 replacement reservation을 계속 막는다.
	// 완료 조건: acknowledged-but-ineligible path가 reservation release나 later safe replay를 허용하지 않는다.
	[TestMethod]
	public void TryCompleteAcknowledgedCommand_InvalidatedStartAfterSafeAba_RemainsHeld()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Start);
		Assert.IsNotNull(reservation);
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);

		var completed = controller.TryCompleteAcknowledgedCommand(reservation);
		var replacement = controller.TryReserveCommand(ControllerCommandKind.Start);

		Assert.IsFalse(completed);
		Assert.IsNull(replacement);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: Recovery-ready 이전 Reset intent가 reservation fence를 만들지 않는지 검증한다.
	// 예상 결과: Idle Reset reservation은 null이고 Core state/event가 변하지 않는다.
	// 완료 조건: ineligible Reset이 output lifecycle authority를 얻을 수 없다.
	[TestMethod]
	public void TryReserveCommand_ResetBeforeRecoveryReady_IsIneligibleWithoutFence()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);

		var rejected = controller.TryReserveCommand(ControllerCommandKind.Reset);
		var start = controller.TryReserveCommand(ControllerCommandKind.Start);

		Assert.IsNull(rejected);
		Assert.IsNotNull(start);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: 다른 ThermalController가 발급한 reservation token이 acknowledged completion authority로 재사용되지 않는지 검증한다.
	// 예상 결과: foreign controller completion은 false이고 두 controller 모두 reservation fence와 Idle state를 보존한다.
	// 완료 조건: reservation identity가 owning Core instance에 묶여 generic token replay를 막는다.
	[TestMethod]
	public void TryCompleteAcknowledgedCommand_ForeignReservation_IsRejectedWithoutRelease()
	{
		var owner = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var other = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var reservation = owner.TryReserveCommand(ControllerCommandKind.Start);
		Assert.IsNotNull(reservation);

		var completed = other.TryCompleteAcknowledgedCommand(reservation);

		Assert.IsFalse(completed);
		Assert.IsNull(owner.TryReserveCommand(ControllerCommandKind.Start));
		Assert.AreEqual(ControllerState.Idle, owner.Snapshot.State);
		Assert.AreEqual(ControllerState.Idle, other.Snapshot.State);
	}
	private static ThermalController CreateRecoveryReadyController()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		return controller;
	}

}
