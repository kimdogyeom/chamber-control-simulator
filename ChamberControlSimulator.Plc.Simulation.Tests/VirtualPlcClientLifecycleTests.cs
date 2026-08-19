using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Simulation.Tests;

[TestClass]
public sealed class VirtualPlcClientLifecycleTests
{
	// 목적: forced transport disconnect 뒤 read와 write가 동일한 transport-specific I/O 계약을 따르는지 검증한다.
	// 예상 결과: read와 write는 PlcTransportException으로 실패하고 ConnectionState는 Faulted로 유지된다.
	// 완료 조건: 두 I/O 경계가 typed failure를 반환한 뒤 explicit reconnect에서만 Connected가 된다.
	[TestMethod]
	public async Task ForceTransportDisconnect_RejectsReadAndWriteWithTransportExceptionUntilReconnect()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);

		client.SimulationControl.ForceTransportDisconnect();

		await Assert.ThrowsExactlyAsync<PlcTransportException>(
			async () => await port.ReadInputsAsync(CancellationToken.None));
		await Assert.ThrowsExactlyAsync<PlcTransportException>(
			async () => await port.WriteOutputsAsync(
				new PlcOutputCommand(1, PlcCommandKind.Stop),
				CancellationToken.None));

		Assert.AreEqual(PlcConnectionState.Faulted, port.ConnectionState);

		await port.ConnectAsync(CancellationToken.None);

		Assert.AreEqual(PlcConnectionState.Connected, port.ConnectionState);
	}

	// 목적: already-cancelled lifecycle token이 transport state를 변경하기 전에 취소되는지 검증한다.
	// 예상 결과: ConnectAsync는 OperationCanceledException을 던지고 ConnectionState는 Disconnected로 유지된다.
	// 완료 조건: cancellation이 connect side effect를 부분 적용하지 않는 상태로 test가 통과한다.
	[TestMethod]
	public async Task ConnectAsync_WithAlreadyCancelledToken_ThrowsWithoutChangingConnectionState()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(
			async () => await port.ConnectAsync(cancellationSource.Token));

		Assert.AreEqual(PlcConnectionState.Disconnected, port.ConnectionState);
	}

	// 목적: asynchronous dispose가 idempotent이고 이후 PLC I/O를 명시적으로 거부하는지 검증한다.
	// 예상 결과: 두 번 DisposeAsync해도 성공하며 이후 read는 ObjectDisposedException을 던진다.
	// 완료 조건: disposed simulation instance가 transport operation을 재개하지 않는 상태로 test가 통과한다.
	[TestMethod]
	public async Task DisposeAsync_IsIdempotentAndRejectsSubsequentIo()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;

		await port.DisposeAsync();
		await port.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
			async () => await port.ReadInputsAsync(CancellationToken.None));
	}

	// 목적: disconnect/reconnect가 field state를 초기화하지 않고 successful read만 observation sequence를 증가시키는지 검증한다.
	// 예상 결과: reconnect 뒤 door input은 보존되고 두 번째 successful read의 sequence는 1이다.
	// 완료 조건: reconnect 자체가 synthetic freshness event를 만들지 않는 상태로 test가 통과한다.
	[TestMethod]
	public async Task DisconnectThenReconnect_PreservesInputAndAdvancesSequenceOnlyOnSuccessfulReads()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;
		await port.ConnectAsync(CancellationToken.None);
		client.ObservationInputControl.SetDoorClosed(false);

		var firstRead = await port.ReadInputsAsync(CancellationToken.None);
		await port.DisconnectAsync(CancellationToken.None);
		await port.ConnectAsync(CancellationToken.None);
		var secondRead = await port.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(0L, firstRead.ObservationSequence);
		Assert.AreEqual(1L, secondRead.ObservationSequence);
		Assert.IsFalse(secondRead.DoorClosed);
	}

	// 목적: 연결 전 I/O를 거부하면서 transport state를 변경하지 않는지 검증한다.
	// 예상 결과: ReadInputsAsync는 PlcTransportException을 던지고 ConnectionState는 Disconnected로 남는다.
	// 완료 조건: 연결 lifecycle를 우회한 PLC read가 성공 또는 암묵적 연결로 바뀌지 않는 상태로 test가 통과한다.
	[TestMethod]
	public async Task ReadInputsAsync_BeforeConnect_ThrowsWithoutChangingConnectionState()
	{
		var client = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		IPlcClient port = client;

		await Assert.ThrowsExactlyAsync<PlcTransportException>(
			async () => await port.ReadInputsAsync(CancellationToken.None));

		Assert.AreEqual(PlcConnectionState.Disconnected, port.ConnectionState);
	}
}
