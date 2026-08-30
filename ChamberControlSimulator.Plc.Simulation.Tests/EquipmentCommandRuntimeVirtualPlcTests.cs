using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Simulation.Tests;

[TestClass]
public sealed class EquipmentCommandRuntimeVirtualPlcTests
{
	// 목적: actual Virtual PLC Start path에서 Written 뒤 exact fresh semantic ACK만 Core Start를 complete하는지 검증한다.
	// 예상 결과: baseline/write/delay 전 Core는 Idle이고 semantic point ACK cycle 뒤만 Heating/Completed가 된다.
	// 완료 조건: P3 mapping, T2 write, virtual semantic point, internal Core completion이 Start-only vertical tracer로 연결된다.
	[TestMethod]
	public async Task StartTracer_ExactFreshVirtualAcknowledgement_CompletesCoreAfterSemanticPoint()
	{
		var options = new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(2));
		var plc = new VirtualPlcClient(options);
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		var baseline = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var request = await runtime.RequestStartAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var beforeAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var exactAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, baseline.ObservationResult.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, request.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, beforeAck.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, beforeAck.ObservationResult.ControllerSnapshot.State);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, exactAck.CommandDisposition);
		Assert.AreEqual(request.CommandId, exactAck.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Start"));
	}

	// 목적: suppressed ACK model에서 virtual Start effect는 semantic time에 적용되지만 Core는 observed exact ACK 없이 Idle hold인지 검증한다.
	// 예상 결과: semantic point와 다음 heating second 뒤 ACK는 0, temperature는 25, runtime은 AwaitingAcknowledgement, Core는 Idle이다.
	// 완료 조건: semantic effect와 observed ACK를 분리해 unconfirmed effect가 semantic Core completion으로 오인되지 않는다.
	[TestMethod]
	public async Task StartTracer_SuppressedAck_AppliesVirtualEffectButKeepsCoreUncompleted()
	{
		var options = new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1));
		var plc = new VirtualPlcClient(options);
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.SuppressNextAcknowledgement();
		await runtime.RequestStartAsync(CancellationToken.None);

		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var semanticPoint = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var heatingObserved = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(0L, semanticPoint.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, semanticPoint.CommandDisposition);
		Assert.AreEqual(0L, heatingObserved.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(25d, heatingObserved.ObservationResult.InputSnapshot.CurrentTemperature);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, heatingObserved.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: virtual Stop이 receipt가 아니라 modeled semantic point에서 heater를 끄고 exact ACK 뒤 Core Stop을 complete하는지 검증한다.
	// 예상 결과: due 전에는 Heating/temperature 상승이 유지되고 due에서 ID 2 ACK/Idle, 이후 temperature는 고정된다.
	// 완료 조건: segmented virtual time과 successful Start fence release를 거친 Stop vertical tracer가 성립한다.
	[TestMethod]
	public async Task StopTracer_ExactFreshVirtualAcknowledgement_DisablesHeatAtSemanticPointAndStopsCore()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1)));
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var stop = await runtime.RequestStopAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(500));
		var beforeStop = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(500));
		var exactStop = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var afterStop = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(1L, start.CommandId);
		Assert.AreEqual(2L, stop.CommandId);
		Assert.AreEqual(22.5d, beforeStop.ObservationResult.InputSnapshot!.CurrentTemperature, 0.0001d);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, beforeStop.CommandDisposition);
		Assert.AreEqual(25d, exactStop.ObservationResult.InputSnapshot!.CurrentTemperature, 0.0001d);
		Assert.AreEqual(stop.CommandId, exactStop.ObservationResult.InputSnapshot.AcknowledgedCommandId);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, exactStop.CommandDisposition);
		Assert.AreEqual(20d, afterStop.ObservationResult.InputSnapshot!.CurrentTemperature, 0.0001d);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Stop"));
	}

	// 목적: suppressed Stop ACK에서도 virtual heater-off effect는 semantic time에 적용되지만 Core Stop은 unconfirmed hold인지 검증한다.
	// 예상 결과: due 뒤 ACK는 prior Start ID, temperature는 25에서 고정, runtime/Core는 AwaitingAcknowledgement/Heating이다.
	// 완료 조건: suppressed ACK가 no-effect 증거 또는 implicit Core completion으로 해석되지 않는다.
	[TestMethod]
	public async Task StopTracer_SuppressedAck_AppliesHeaterOffButKeepsCoreUncompleted()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1)));
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.SuppressNextAcknowledgement();
		await runtime.RequestStopAsync(CancellationToken.None);

		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var semanticPoint = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var afterStop = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(start.CommandId, semanticPoint.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(25d, semanticPoint.ObservationResult.InputSnapshot.CurrentTemperature, 0.0001d);
		Assert.AreEqual(20d, afterStop.ObservationResult.InputSnapshot!.CurrentTemperature, 0.0001d);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, afterStop.CommandDisposition);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == "Stop"));
	}

	// 목적: virtual Reset이 plant shortcut 없이 modeled semantic ACK만 제공하고 Core Recovery revalidation 뒤 complete하는지 검증한다.
	// 예상 결과: Written 뒤 Recovery/20도, due exact ACK 뒤 Idle/one Reset이며 virtual temperature는 20도다.
	// 완료 조건: Reset simulation은 command/ACK behavior만 모델링하고 plant/alarm/safety shortcut을 만들지 않는다.
	[TestMethod]
	public async Task ResetTracer_ExactFreshVirtualAcknowledgement_ResetsCoreWithoutPlantShortcut()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1)));
		var controller = CreateRecoveryReadyController();
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var reset = await runtime.RequestResetAsync(CancellationToken.None);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var exactReset = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(reset.CommandId, exactReset.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(20d, exactReset.ObservationResult.InputSnapshot.CurrentTemperature);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, exactReset.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Reset"));
	}

	// 목적: suppressed Reset ACK가 virtual plant를 바꾸거나 Core uncertainty를 자동 해소하지 않는지 검증한다.
	// 예상 결과: semantic time 뒤 ACK 0/temperature 20/Recovery/AwaitingAcknowledgement가 유지된다.
	// 완료 조건: Reset request나 absent ACK가 recovery/reconciliation/plant reset shortcut이 아니다.
	[TestMethod]
	public async Task ResetTracer_SuppressedAck_KeepsPlantAndCoreUnconfirmed()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1)));
		var controller = CreateRecoveryReadyController();
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.SimulationControl.SuppressNextAcknowledgement();
		await runtime.RequestResetAsync(CancellationToken.None);

		plc.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var semanticPoint = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(0L, semanticPoint.ObservationResult.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(20d, semanticPoint.ObservationResult.InputSnapshot.CurrentTemperature);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, semanticPoint.CommandDisposition);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == "Reset"));
	}

	// 목적: P3-only EquipmentCoordinator cycle이 queued Start의 virtual command time이나 ACK state를 진행하지 않는지 검증한다.
	// 예상 결과: explicit Advance 없이 반복 read cycle의 temperature 20과 ACK 0이 유지된다.
	// 완료 조건: P3 observation path가 VirtualPlcSimulationControl authority를 암묵적으로 얻지 않는다.
	[TestMethod]
	public async Task P3OnlyCycles_DoNotAdvanceVirtualStartTimeOrAcknowledgement()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1)));
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		await using var p3 = new EquipmentCoordinator(controller, plc);
		await p3.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await ((IPlcOutputPort)plc).WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);

		var first = await p3.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var second = await p3.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(0L, first.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(20d, first.InputSnapshot.CurrentTemperature);
		Assert.AreEqual(0L, second.InputSnapshot!.AcknowledgedCommandId);
		Assert.AreEqual(20d, second.InputSnapshot.CurrentTemperature);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}
	// 목적: Core Complete 다음 cycle이 같은 게이트에서 자동 Stop write를 넣고 ACK 뒤 히터를 끄는지 검증한다.
	// 예상 결과: Complete 관측 후 IsAutomatic Stop이 AwaitingAck가 되고, Advance+cycle 뒤 히터가 꺼진다.
	// 완료 조건: 관측 래퍼 없이 CommandRuntime.CycleAsync만 Stop admission을 시작한다.
	[TestMethod]
	public async Task CycleAsync_WhenCoreCompleteAndHeaterEnabled_AdmitsAutomaticStop()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		var controller = new ThermalController(
			new Recipe("Fast", 21d, 40d, TimeSpan.FromMilliseconds(1)),
			new SimulationSettings(20d, TimeSpan.FromSeconds(3)));
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await runtime.RequestStartAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(1));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);

		plc.ObservationInputControl.SetCurrentTemperature(21d);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		Assert.AreEqual(ControllerState.Holding, controller.Snapshot.State);
		await runtime.CycleAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);
		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
		plc.ObservationInputControl.SetCurrentTemperature(20d);
		var completeCycle = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(ControllerState.Complete, controller.Snapshot.State);
		Assert.IsTrue(runtime.CurrentState.IsAutomatic);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreNotEqual(EquipmentCommandLifecycleDisposition.NoCommand, completeCycle.CommandDisposition);

		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(1));
		var afterStopAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		Assert.IsFalse(afterStopAck.ObservationResult.InputSnapshot!.HeaterEnabled);
	}
	// 목적: Hold가 끝나 Cooling이 되면 같은 게이트에서 자동 Stop이 히터를 끄고 Core는 Cooling을 유지하는지 검증한다.
	// 예상 결과: Cooling 관측 cycle에 IsAutomatic Stop이 들어가고, Stop ACK 뒤 히터 OFF·Core Cooling이다.
	// 완료 조건: Cooling 중 히터가 켜진 채 온도가 계속 오르지 않는다.
	[TestMethod]
	public async Task CycleAsync_WhenCoreCoolingAndHeaterEnabled_AdmitsAutomaticStopWithoutLeavingCooling()
	{
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		var controller = new ThermalController(
			new Recipe("Fast", 21d, 40d, TimeSpan.FromMilliseconds(1)),
			new SimulationSettings(20d, TimeSpan.FromSeconds(3)));
		await using var runtime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await runtime.RequestStartAsync(CancellationToken.None);
		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(1));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.ObservationInputControl.SetCurrentTemperature(21d);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var coolingCycle = await runtime.CycleAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
		Assert.IsTrue(runtime.CurrentState.IsAutomatic);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreNotEqual(EquipmentCommandLifecycleDisposition.NoCommand, coolingCycle.CommandDisposition);

		plc.SimulationControl.Advance(TimeSpan.FromMilliseconds(1));
		var afterStopAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		Assert.IsFalse(afterStopAck.ObservationResult.InputSnapshot!.HeaterEnabled);
		Assert.AreEqual(ControllerState.Cooling, controller.Snapshot.State);
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
