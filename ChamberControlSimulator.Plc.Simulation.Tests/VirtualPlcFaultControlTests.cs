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

		client.ObservationInputControl.SetCurrentTemperature(81.5d);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(81.5d, snapshot.CurrentTemperature);
	}

	// 목적: suppressed observed ACK에서도 Start semantic effect는 configured semantic point에 적용되는 approved fault model을 검증한다.
	// 예상 결과: Written 뒤 semantic point에서 ACK는 0/온도 20이고 다음 virtual second에는 온도 25지만 ACK는 계속 0이다.
	// 완료 조건: observed ACK suppression이 semantic effect를 취소하지 않으며 Application uncertainty hold가 필요한 모델을 고정한다.
	[TestMethod]
	public async Task SuppressNextAcknowledgement_AppliesSemanticEffectButKeepsAckUnobserved()
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
		var atSemanticPoint = await port.ReadInputsAsync(CancellationToken.None);
		client.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var afterHeating = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.AreEqual(0L, atSemanticPoint.AcknowledgedCommandId);
		Assert.AreEqual(20d, atSemanticPoint.CurrentTemperature);
		Assert.AreEqual(0L, afterHeating.AcknowledgedCommandId);
		Assert.AreEqual(25d, afterHeating.CurrentTemperature);
	}

	// 목적: suppressed ACK Start effect도 semantic point overshoot와 equivalent split Advance에서 동일한지 검증한다.
	// 예상 결과: delay 1초 뒤 총 2초를 one-step 또는 1+1초로 진행하면 모두 온도 25와 ACK 0이다.
	// 완료 조건: ACK visibility suppression이 exact semantic-time thermal effect의 partition invariance를 깨지 않는다.
	[TestMethod]
	public async Task SuppressedStartEffectAcrossSemanticPoint_IsPartitionInvariant()
	{
		var options = new VirtualPlcOptions(20d, 5d, TimeSpan.FromSeconds(1));
		var oneStep = new VirtualPlcClient(options);
		var split = new VirtualPlcClient(options);
		IPlcClient oneStepPort = oneStep;
		IPlcClient splitPort = split;
		await oneStepPort.ConnectAsync(CancellationToken.None);
		await splitPort.ConnectAsync(CancellationToken.None);
		oneStep.SimulationControl.SuppressNextAcknowledgement();
		split.SimulationControl.SuppressNextAcknowledgement();
		await oneStepPort.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);
		await splitPort.WriteOutputsAsync(new PlcOutputCommand(1, PlcCommandKind.Start), CancellationToken.None);

		oneStep.SimulationControl.Advance(TimeSpan.FromSeconds(2));
		split.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		split.SimulationControl.Advance(TimeSpan.FromSeconds(1));
		var oneStepSnapshot = await oneStepPort.ReadInputsAsync(CancellationToken.None);
		var splitSnapshot = await splitPort.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(25d, oneStepSnapshot.CurrentTemperature);
		Assert.AreEqual(0L, oneStepSnapshot.AcknowledgedCommandId);
		Assert.AreEqual(oneStepSnapshot.CurrentTemperature, splitSnapshot.CurrentTemperature);
		Assert.AreEqual(oneStepSnapshot.AcknowledgedCommandId, splitSnapshot.AcknowledgedCommandId);
	}

	// 목적: sensor health fault가 immutable PLC input snapshot으로 노출되는지 검증한다.
	// 예상 결과: observation input control로 unhealthy를 설정하면 다음 read의 SensorHealthy가 false다.
	// 완료 조건: sensor fault injection이 application port의 별도 member 없이 read contract로 관측된다.
	[TestMethod]
	public async Task SetSensorHealthyFalse_IsObservedThroughInputSnapshot()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		client.ObservationInputControl.SetSensorHealthy(false);
		var snapshot = await port.ReadInputsAsync(CancellationToken.None);

		Assert.IsFalse(snapshot.SensorHealthy);
	}
}
