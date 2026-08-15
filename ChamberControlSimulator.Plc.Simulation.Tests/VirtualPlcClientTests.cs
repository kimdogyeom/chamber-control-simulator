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

	// 목적: transport Start write가 virtual heater를 켜고 명시적 virtual time에서만 온도를 바꾸는지 검증한다.
	// 예상 결과: write 직후 온도는 그대로이고 Advance 1초 후 5도 상승한다.
	// 완료 조건: 실제 대기 없이 output write와 plant temperature evolution의 분리가 test로 보장된다.
	[TestMethod]
	public async Task WriteStartThenAdvance_ChangesTemperatureOnlyAfterExplicitVirtualTime()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		var receipt = await port.WriteOutputsAsync(
			new PlcOutputCommand(1, PlcCommandKind.Start),
			CancellationToken.None);
		var temperatureBeforeAdvance = (await port.ReadInputsAsync(CancellationToken.None)).CurrentTemperature;

		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var temperatureAfterAdvance = (await port.ReadInputsAsync(CancellationToken.None)).CurrentTemperature;

		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.AreEqual(20d, temperatureBeforeAdvance);
		Assert.AreEqual(25d, temperatureAfterAdvance);
	}

	// 목적: simulation control로 변경한 door input이 production PLC port read에 반영되는지 검증한다.
	// 예상 결과: 연결 후 읽은 snapshot의 DoorClosed가 false다.
	// 완료 조건: concrete simulation control과 IPlcClient I/O contract가 분리된 상태로 test가 통과한다.
	[TestMethod]
	public async Task ReadInputsAsync_AfterSimulationControlOpensDoor_ReturnsDoorOpenSnapshot()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;

		await port.ConnectAsync(CancellationToken.None);
		client.SimulationControl.SetDoorClosed(false);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.IsFalse(snapshot.DoorClosed);
	}
}
