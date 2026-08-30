using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Simulation.Tests;

[TestClass]
public sealed class VirtualPlcClientTests
{
	// 목적: transport write receipt와 PLC semantic ACK가 서로 다른 시간·관측 경계인지 검증한다.
	// 예상 결과: Written receipt는 즉시 반환되지만 acknowledged command ID는 configured virtual delay 뒤에만 갱신된다.
	// 완료 조건: receipt를 semantic acceptance로 오해하지 않는 contract가 deterministic test로 통과한다.
	[TestMethod]
	public async Task WriteOutputsAsync_DelaysSemanticAcknowledgementUntilConfiguredVirtualTime()
	{
		var options = new VirtualPlcOptions(
			initialTemperature: 20d,
			heatingRatePerSecond: 5d,
			acknowledgementDelay: TimeSpan.FromSeconds(2));
		var client = new VirtualPlcClient(options);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		var receipt = await port.WriteOutputsAsync(
			new PlcOutputCommand(7, PlcCommandKind.Start),
			CancellationToken.None);
		var acknowledgementBeforeAdvance = (await port.ReadInputsAsync(CancellationToken.None)).AcknowledgedCommandId;

		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var acknowledgementBeforeDelay = (await port.ReadInputsAsync(CancellationToken.None)).AcknowledgedCommandId;

		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var acknowledgementAfterDelay = (await port.ReadInputsAsync(CancellationToken.None)).AcknowledgedCommandId;

		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.AreEqual(0L, acknowledgementBeforeAdvance);
		Assert.AreEqual(0L, acknowledgementBeforeDelay);
		Assert.AreEqual(7L, acknowledgementAfterDelay);
	}

	// 목적: Start transport write가 아니라 configured semantic point에서만 virtual heater와 ACK가 함께 적용되는지 검증한다.
	// 예상 결과: write와 delay 전에는 20도/ACK 0, semantic point에서는 20도/ACK 1, 이후 Advance 1초에만 25도가 된다.
	// 완료 조건: Written receipt가 virtual semantic effect나 temperature evolution을 선행하지 않는다.
	[TestMethod]
	public async Task WriteStart_ActivatesHeaterOnlyAtSemanticPoint()
	{
		var options = new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(2));
		var client = new VirtualPlcClient(options);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		var receipt = await port.WriteOutputsAsync(
			new PlcOutputCommand(1, PlcCommandKind.Start),
			CancellationToken.None);
		var afterWrite = await port.ReadInputsAsync(CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var beforeSemanticPoint = await port.ReadInputsAsync(CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var atSemanticPoint = await port.ReadInputsAsync(CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var afterHeating = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.AreEqual(20d, afterWrite.CurrentTemperature);
		Assert.AreEqual(0L, afterWrite.AcknowledgedCommandId);
		Assert.AreEqual(20d, beforeSemanticPoint.CurrentTemperature);
		Assert.AreEqual(0L, beforeSemanticPoint.AcknowledgedCommandId);
		Assert.AreEqual(20d, atSemanticPoint.CurrentTemperature);
		Assert.AreEqual(1L, atSemanticPoint.AcknowledgedCommandId);
		Assert.AreEqual(25d, afterHeating.CurrentTemperature);
	}
	// 목적: 히터 OFF일 때 plant가 초기 온도로 식는지 검증한다.
	// 예상 결과: Start 적분 후 Stop ACK 다음 Advance에서 온도가 내려간다.
	// 완료 조건: Cooling 구간에 히터가 꺼져 있으면 온도가 오르지 않는다.
	[TestMethod]
	public async Task Advance_WhenHeaterOffAboveInitial_CoolsTowardInitialTemperature()
	{
		var client = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(2));
		var heated = await port.ReadInputsAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(2, PlcCommandKind.Stop), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var cooling = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(30d, heated.CurrentTemperature);
		Assert.IsTrue(heated.HeaterEnabled);
		Assert.AreEqual(25d, cooling.CurrentTemperature);
		Assert.IsFalse(cooling.HeaterEnabled);
	}


	// 목적: Start semantic point를 넘는 one-step Advance와 equivalent split Advance가 같은 plant/ACK 결과인지 검증한다.
	// 예상 결과: delay 2초 뒤 총 3초를 one-step 또는 2+1초로 진행하면 모두 온도 25와 ACK 1이다.
	// 완료 조건: virtual Start semantics와 thermal integration이 caller step partition에 의존하지 않는다.
	[TestMethod]
	public async Task AdvanceAcrossStartSemanticPoint_IsPartitionInvariant()
	{
		var options = new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(2));
		var oneStep = new VirtualPlcClient(options);
		var split = new VirtualPlcClient(options);
		IPlcClient oneStepPort = oneStep;
		IPlcClient splitPort = split;
		await oneStepPort.ConnectAsync(CancellationToken.None);
		await splitPort.ConnectAsync(CancellationToken.None);
		await oneStepPort.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		await splitPort.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);

		oneStep.SimulationControl.Advance(TimeSpan.FromSeconds(3));
		split.SimulationControl.Advance(TimeSpan.FromSeconds(2));
		split.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var oneStepSnapshot = await oneStepPort.ReadInputsAsync(CancellationToken.None);
		var splitSnapshot = await splitPort.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(25d, oneStepSnapshot.CurrentTemperature);
		Assert.AreEqual(1L, oneStepSnapshot.AcknowledgedCommandId);
		Assert.AreEqual(oneStepSnapshot.CurrentTemperature, splitSnapshot.CurrentTemperature);
		Assert.AreEqual(oneStepSnapshot.AcknowledgedCommandId, splitSnapshot.AcknowledgedCommandId);
	}

	// 목적: observation input control로 변경한 door input이 production PLC port read에 반영되는지 검증한다.
	// 예상 결과: 연결 후 읽은 snapshot의 DoorClosed가 false다.
	// 완료 조건: concrete simulation control과 IPlcClient I/O contract가 분리된 상태로 test가 통과한다.
	[TestMethod]
	public async Task ReadInputsAsync_AfterObservationInputControlOpensDoor_ReturnsDoorOpenSnapshot()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;

		await port.ConnectAsync(CancellationToken.None);
		client.ObservationInputControl.SetDoorClosed(false);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.IsFalse(snapshot.DoorClosed);
	}
	// 목적: Heating 중 문 열림이 Core 알람과 별개로 virtual heater를 즉시 끄는지 검증한다.
	// 예상 결과: Start semantic ACK 뒤 온도가 오르다가 SetDoorClosed(false) 이후 Advance해도 온도가 더 오르지 않는다.
	// 완료 조건: 문 열림 interlock이 plant heater latch를 끈다.
	[TestMethod]
	public async Task SetDoorClosedFalse_AfterStartHeater_StopsTemperatureRise()
	{
		var client = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var heating = await port.ReadInputsAsync(CancellationToken.None);

		client.ObservationInputControl.SetDoorClosed(false);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(2));
		var afterDoorOpen = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(25d, heating.CurrentTemperature);
		Assert.IsTrue(heating.HeaterEnabled);
		Assert.IsFalse(afterDoorOpen.DoorClosed);
		Assert.IsLessThanOrEqualTo(heating.CurrentTemperature, afterDoorOpen.CurrentTemperature);
		Assert.IsFalse(afterDoorOpen.HeaterEnabled);
	}

	// 목적: plant 과온 천장이 Core Recipe와 독립적으로 히터를 끄는지 검증한다.
	// 예상 결과: 한도 80에서 적분 후 온도가 80이고 HeaterEnabled는 false다.
	// 완료 조건: VirtualPlcOptions.OverTemperatureLimit만 사용하고 Core를 참조하지 않는다.
	[TestMethod]
	public async Task Advance_AtOverTemperatureLimit_LatchesHeaterOff()
	{
		var client = new VirtualPlcClient(new VirtualPlcOptions(70d, 20d, TimeSpan.Zero, overTemperatureLimit: 80d));
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(80d, snapshot.CurrentTemperature);
		Assert.IsFalse(snapshot.HeaterEnabled);
	}
	[TestMethod]
	public async Task ForceTransportDisconnect_AfterStartHeater_StopsTemperatureRise()
	{
		var client = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var heating = await port.ReadInputsAsync(CancellationToken.None);

		client.SimulationControl.ForceTransportDisconnect();
		client.SimulationControl.Advance(TimeSpan.FromSeconds(2));
		await port.ConnectAsync(CancellationToken.None);
		var afterDisconnect = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(25d, heating.CurrentTemperature);
		Assert.IsTrue(heating.HeaterEnabled);
		Assert.IsLessThanOrEqualTo(heating.CurrentTemperature, afterDisconnect.CurrentTemperature);
		Assert.IsFalse(afterDisconnect.HeaterEnabled);
	}

	[TestMethod]
	public async Task ResetCommand_DoesNotKeepHeaterOnAfterDisconnectTrip()
	{
		var client = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.Zero));
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		client.SimulationControl.ForceTransportDisconnect();
		await port.ConnectAsync(CancellationToken.None);
		await port.WriteOutputsAsync(new PlcOutputCommand(2, PlcCommandKind.Reset), CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var afterReset = await port.ReadInputsAsync(CancellationToken.None);

		Assert.IsFalse(afterReset.HeaterEnabled);
		Assert.IsLessThanOrEqualTo(25d, afterReset.CurrentTemperature);
	}
}
