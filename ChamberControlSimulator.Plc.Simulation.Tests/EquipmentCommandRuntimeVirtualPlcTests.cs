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
}
