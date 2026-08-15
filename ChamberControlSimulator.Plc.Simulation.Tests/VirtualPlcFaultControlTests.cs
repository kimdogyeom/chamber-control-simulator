using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Simulation.Tests;

[TestClass]
public sealed class VirtualPlcFaultControlTests
{
	// 목적: simulation temperature override가 immutable PLC input snapshot으로 노출되는지 검증한다.
	// 예상 결과: control에서 설정한 finite temperature가 다음 read의 CurrentTemperature와 같다.
	// 완료 조건: UI/test-only temperature injection이 Core 또는 IPlcClient member 없이 simulation boundary에 머문다.
	[TestMethod]
	public async Task SetCurrentTemperature_IsObservedThroughInputSnapshot()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		client.SimulationControl.SetCurrentTemperature(81.5d);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(81.5d, snapshot.CurrentTemperature);
	}

	// 목적: simulation-only ACK suppression이 receipt와 later semantic acknowledgement를 분리하는지 검증한다.
	// 예상 결과: write receipt는 Written이지만 configured virtual delay 뒤에도 acknowledged command ID는 0이다.
	// 완료 조건: fault injection API가 IPlcClient가 아닌 VirtualPlcSimulationControl에만 있고 test가 통과한다.
	[TestMethod]
	public async Task SuppressNextAcknowledgement_LeavesAcknowledgedCommandIdUnchangedAfterVirtualDelay()
	{
		var options = new VirtualPlcOptions(
			initialTemperature: 20d,
			heatingRatePerSecond: 5d,
			acknowledgementDelay: TimeSpan.FromSeconds(1));
		var client = new VirtualPlcClient(options);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		client.SimulationControl.SuppressNextAcknowledgement();
		var receipt = await port.WriteOutputsAsync(
			new PlcOutputCommand(13, PlcCommandKind.Start),
			CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.AreEqual(0L, snapshot.AcknowledgedCommandId);
	}

	// 목적: sensor health fault가 immutable PLC input snapshot으로 노출되는지 검증한다.
	// 예상 결과: simulation control로 unhealthy를 설정하면 다음 read의 SensorHealthy가 false다.
	// 완료 조건: sensor fault injection이 application port의 별도 member 없이 read contract로 관측된다.
	[TestMethod]
	public async Task SetSensorHealthyFalse_IsObservedThroughInputSnapshot()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		client.SimulationControl.SetSensorHealthy(false);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.IsFalse(snapshot.SensorHealthy);
	}
}
