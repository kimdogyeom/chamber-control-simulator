using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Application.Tests;

[TestClass]
public sealed class EquipmentCoordinatorTests
{
	// 목적: confirmed reconnect failure 뒤 다음 delay timestamp를 얻지 못하면 clock fault metadata로 fail-closed 되는지 검증한다.
	// 예상 결과: TimeProvider 예외는 전파되지만 이후 cycle은 attempt 1의 ReconnectExhausted/TimeProviderFailure를 안정적으로 반환한다.
	// 완료 조건: connect 1회 뒤 추가 reconnect/read/write와 inactive Core 알람 없이 clock failure indicator가 고정된다.
	[TestMethod]
	public async Task CycleAsync_WhenFailureTimestampThrows_ExhaustsWithTimeProviderMetadataWithoutRetry()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var exception = new InvalidOperationException("controlled failure timestamp error");
		var timeProvider = new ThrowOnThirdTimestampTimeProvider(exception);
		var plc = new ReconnectFailingObservationPort();
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));

		var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None));
		var exhausted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreSame(exception, thrown);
		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, exhausted.SynchronizationState);
		Assert.AreEqual(1, exhausted.ReconnectAttemptCount);
		Assert.AreEqual(ReconnectFailureKind.TimeProviderFailure, exhausted.LastReconnectFailure);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}
	// 목적: reconnect due 계산 중 observation port가 외부에서 Connected로 바뀌면 stale한 사전 상태로 ConnectAsync를 호출하지 않는지 검증한다.
	// 예상 결과: coordinator는 connect 직전 실제 상태를 다시 확인하고 connect 없이 두 번째 read를 Completed로 처리한다.
	// 완료 조건: confirmed first read fault 뒤 250ms 경계 cycle의 connect는 0회, read는 총 2회이며 synchronization은 Synchronized다.
	[TestMethod]
	public async Task CycleAsync_WhenObservationReconnectStateChangesBeforeConnect_RechecksActualStateAndDoesNotConnect()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var timeProvider = new ManualTimeProvider();
		var plc = new StateChangingObservationPort();
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));
		plc.ConnectExternallyAfterNextStateRead();

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, result.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.Synchronized, result.SynchronizationState);
		Assert.AreEqual(0, plc.ConnectCallCount);
		Assert.AreEqual(2, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}
	// 목적: 오래 걸린 reconnect attempt의 다음 backoff가 attempt 시작이 아니라 typed failure 확정 시각부터 계산되는지 검증한다.
	// 예상 결과: 첫 connect가 10초 뒤 실패해도 즉시 재시도하지 않고 그 failure 뒤 500ms 경계에서만 두 번째 connect를 호출한다.
	// 완료 조건: failure 직후와 +499ms connect count는 1이고 +500ms에서만 2가 되며 attempt metadata도 각각 1/2다.
	[TestMethod]
	public async Task CycleAsync_WhenReconnectFailureIsDelayed_SchedulesNextDelayFromFailureTime()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var timeProvider = new ManualTimeProvider();
		var connectCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var plc = new ReconnectFailingObservationPort
		{
			ConnectHandler = _ => connectCompletion.Task
		};
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));
		var firstAttempt = coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		timeProvider.Advance(TimeSpan.FromSeconds(10));
		connectCompletion.SetException(new PlcTransportException("controlled delayed reconnect failure"));
		var firstFailure = await firstAttempt;
		var immediatelyAfterFailure = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var immediateConnectCount = plc.ConnectCallCount;
		timeProvider.Advance(TimeSpan.FromMilliseconds(499));
		var beforeBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var beforeBoundaryConnectCount = plc.ConnectCallCount;
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var secondFailure = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(1, firstFailure.ReconnectAttemptCount);
		Assert.AreEqual(1, immediatelyAfterFailure.ReconnectAttemptCount);
		Assert.AreEqual(1, immediateConnectCount);
		Assert.AreEqual(1, beforeBoundary.ReconnectAttemptCount);
		Assert.AreEqual(1, beforeBoundaryConnectCount);
		Assert.AreEqual(2, secondFailure.ReconnectAttemptCount);
		Assert.AreEqual(2, plc.ConnectCallCount);
	}
	// 목적: reconnect due 계산의 TimeProvider 예외가 ConnectAsync 경계로 오분류되어 통신 알람이나 PLC 호출을 만들지 않는지 검증한다.
	// 예상 결과: 동일한 policy-time 예외가 전파되고 reconnect connect/read/write 추가 호출과 inactive Core 알람은 없다.
	// 완료 조건: confirmed read fault 뒤 due 조회 실패 시 connect 0회, read 1회, write 0회와 Idle/no-alarm 상태가 유지된다.
	[TestMethod]
	public async Task CycleAsync_WhenReconnectTimeProviderThrows_PropagatesWithoutConnectOrNewAlarm()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var exception = new InvalidOperationException("controlled reconnect time failure");
		var timeProvider = new ThrowOnSecondTimestampTimeProvider(exception);
		var plc = new ReconnectFailingObservationPort();
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None));

		Assert.AreSame(exception, thrown);
		Assert.AreEqual(0, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}
	// 목적: reconnect delay가 아직 지나지 않은 canceled cycle이 connect attempt나 retry 기준 시각을 바꾸지 않는지 검증한다.
	// 예상 결과: cancellation은 전파되고 원래 250ms 경계 전 connect 0회, 경계에서 첫 connect 1회만 기록된다.
	// 완료 조건: attempt metadata는 canceled cycle 전후 0이고 inactive Core에는 cancellation 유래 통신 알람이 생기지 않는다.
	[TestMethod]
	public async Task CycleAsync_WhenCanceledBeforeReconnectIsDue_PropagatesWithoutAttemptOrReschedule()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var timeProvider = new ManualTimeProvider();
		var plc = new ReconnectFailingObservationPort();
		await using var coordinator = CreateCoordinator(controller, plc, timeProvider, ReconnectPolicy.Conservative);
		var fault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(
			() => coordinator.CycleAsync(TimeSpan.Zero, cancellationSource.Token));
		timeProvider.Advance(TimeSpan.FromMilliseconds(249));
		var beforeBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var firstFailure = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(0, fault.ReconnectAttemptCount);
		Assert.AreEqual(0, beforeBoundary.ReconnectAttemptCount);
		Assert.AreEqual(1, firstFailure.ReconnectAttemptCount);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}
	// 목적: due reconnect ConnectAsync가 in-flight cancellation되면 같은 경계의 자동 replay가 영구 차단되는지 검증한다.
	// 예상 결과: cancellation은 그대로 전파되고 epoch는 Canceled terminal 상태, 활성 Core는 Heating/no-alarm으로 고정된다.
	// 완료 조건: 같은 시각과 이후 시각에도 connect 1회, read 1회, write 0회이며 CommunicationLost가 추가되지 않는다.
	[TestMethod]
	public async Task CycleAsync_WhenInFlightReconnectConnectIsCanceled_TerminatesEpochWithoutReplay()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var timeProvider = new ManualTimeProvider();
		var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handlerCallCount = 0;
		var plc = new ReconnectFailingObservationPort
		{
			ConnectHandler = async cancellationToken =>
			{
				handlerCallCount++;
				if (handlerCallCount == 1)
				{
					connectStarted.TrySetResult();
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
			}
		};
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		controller.Start();
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));
		using var cancellationSource = new CancellationTokenSource();

		var canceledAttempt = coordinator.CycleAsync(TimeSpan.Zero, cancellationSource.Token);
		await connectStarted.Task;
		cancellationSource.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledAttempt);
		var sameBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(10));
		var later = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, sameBoundary.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, sameBoundary.SynchronizationState);
		Assert.AreEqual(0, sameBoundary.ReconnectAttemptCount);
		Assert.AreEqual("Canceled", sameBoundary.LastReconnectFailure.ToString());
		Assert.AreEqual(sameBoundary, later);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}
	// 목적: 오래 걸린 reconnect connect 성공 직후 typed read fault가 attempt count와 fault 기준 backoff를 보존하는지 검증한다.
	// 예상 결과: 각 connect 중 10초가 지나도 backoff는 read fault 시각부터 계산되고 세 번째 fault cycle에서 즉시 소진된다.
	// 완료 조건: 세 번째 read fault 자체가 ReconnectExhausted/count 3이고 connect 3회, read 4회, write 0회로 고정된다.
	[TestMethod]
	public async Task CycleAsync_WhenReconnectConnectSucceedsButReadFaults_PreservesAttemptBackoffAndCap()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var timeProvider = new ManualTimeProvider();
		var plc = new ReconnectFailingObservationPort
		{
			ConnectHandler = _ =>
			{
				timeProvider.Advance(TimeSpan.FromSeconds(10));
				return Task.CompletedTask;
			},
			ConnectSucceeds = true
		};
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);

		var initialFault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));
		var firstReadFault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var immediatelyAfterFirst = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(499));
		var beforeSecondBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var secondReadFault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var immediatelyAfterSecond = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(999));
		var beforeThirdBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var thirdReadFault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var exhausted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(0, initialFault.ReconnectAttemptCount);
		Assert.AreEqual(1, firstReadFault.ReconnectAttemptCount);
		Assert.AreEqual(1, immediatelyAfterFirst.ReconnectAttemptCount);
		Assert.AreEqual(1, beforeSecondBoundary.ReconnectAttemptCount);
		Assert.AreEqual(2, secondReadFault.ReconnectAttemptCount);
		Assert.AreEqual(2, immediatelyAfterSecond.ReconnectAttemptCount);
		Assert.AreEqual(2, beforeThirdBoundary.ReconnectAttemptCount);
		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, thirdReadFault.SynchronizationState);
		Assert.AreEqual(3, thirdReadFault.ReconnectAttemptCount);
		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, exhausted.SynchronizationState);
		Assert.AreEqual(3, exhausted.ReconnectAttemptCount);
		Assert.AreEqual(3, plc.ConnectCallCount);
		Assert.AreEqual(4, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
	}
	// 목적: 세 번째 reconnect 성공 직후 typed read fault가 정책 시계를 다시 조회하지 않고 즉시 상한 종료되는지 검증한다.
	// 예상 결과: 여섯 번의 허용 timestamp 조회 뒤 세 번째 read-fault cycle 자체가 ReconnectExhausted/count 3을 반환한다.
	// 완료 조건: 네 번째 read fault가 시계를 봉쇄한 뒤 미래 경계를 지나도 connect/read/write 3/4/0과 통신 알람 이력이 고정된다.
	[TestMethod]
	public async Task CycleAsync_WhenThirdReconnectReadFaults_ExhaustsWithoutPostFaultTimestampQuery()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var exception = new InvalidOperationException("forbidden post-third-read-fault timestamp query");
		var timeProvider = new RejectPostThirdReadFaultTimestampTimeProvider(exception);
		var plc = new ReconnectFailingObservationPort
		{
			BeforeReadFailure = readCallCount =>
			{
				if (readCallCount == 4)
				{
					timeProvider.RejectTimestampReads();
				}
			},
			ConnectSucceeds = true,
			ReadFailureKeepingConnectionState = 4
		};
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);

		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(250));
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(500));
		await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		var thirdReadFault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var eventCountAfterThirdReadFault = controller.EventHistory.Count;
		var stillExhausted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(10));
		var exhaustedAfterFutureBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, thirdReadFault.SynchronizationState);
		Assert.AreEqual(3, thirdReadFault.ReconnectAttemptCount);
		Assert.AreEqual(6, timeProvider.TimestampCallCount);
		Assert.AreEqual(3, plc.ConnectCallCount);
		Assert.AreEqual(4, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(thirdReadFault, stillExhausted);
		Assert.AreEqual(thirdReadFault, exhaustedAfterFutureBoundary);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
		Assert.HasCount(eventCountAfterThirdReadFault, controller.EventHistory);
	}

	// 목적: confirmed read fault 뒤 reconnect가 monotonic 250ms, 500ms, 1s 경계에서만 최대 3회 수행되는지 검증한다.
	// 예상 결과: 각 경계 전에는 connect가 없고 세 번째 typed connect 실패 뒤 ReconnectExhausted metadata가 고정된다.
	// 완료 조건: 추가 cycle/time advance에도 connect 3회, read 1회, write 0회이며 raw exception 없는 failure indicator만 반환한다.
	[TestMethod]
	public async Task CycleAsync_AfterReadFault_AttemptsReconnectOnlyAtBoundariesAndStopsAfterThreeFailures()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var timeProvider = new ManualTimeProvider();
		var plc = new ReconnectFailingObservationPort();
		await using var coordinator = CreateCoordinator(
			controller,
			plc,
			timeProvider,
			ReconnectPolicy.Conservative);

		var fault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var beforeFirstDelay = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(249));
		var beforeFirstBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var firstFailure = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var beforeSecondDelay = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(499));
		var beforeSecondBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var secondFailure = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var beforeThirdDelay = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(999));
		var beforeThirdBoundary = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		var exhausted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(10));
		var stillExhausted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(ConnectionSynchronizationState.WaitingForReconnect, fault.SynchronizationState);
		Assert.AreEqual(0, fault.ReconnectAttemptCount);
		Assert.AreEqual(ReconnectFailureKind.None, fault.LastReconnectFailure);
		Assert.AreEqual(0, beforeFirstDelay.ReconnectAttemptCount);
		Assert.AreEqual(0, beforeFirstBoundary.ReconnectAttemptCount);
		Assert.AreEqual(1, firstFailure.ReconnectAttemptCount);
		Assert.AreEqual(ReconnectFailureKind.TransportFailure, firstFailure.LastReconnectFailure);
		Assert.AreEqual(1, beforeSecondDelay.ReconnectAttemptCount);
		Assert.AreEqual(1, beforeSecondBoundary.ReconnectAttemptCount);
		Assert.AreEqual(2, secondFailure.ReconnectAttemptCount);
		Assert.AreEqual(2, beforeThirdDelay.ReconnectAttemptCount);
		Assert.AreEqual(2, beforeThirdBoundary.ReconnectAttemptCount);
		Assert.AreEqual(ConnectionSynchronizationState.ReconnectExhausted, exhausted.SynchronizationState);
		Assert.AreEqual(3, exhausted.ReconnectAttemptCount);
		Assert.AreEqual(ReconnectFailureKind.TransportFailure, exhausted.LastReconnectFailure);
		Assert.IsFalse(typeof(EquipmentCycleResult).GetProperties().Any(property => typeof(Exception).IsAssignableFrom(property.PropertyType)));
		Assert.AreEqual(exhausted, stillExhausted);
		Assert.AreEqual(3, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
	}
	// 목적: reconnect policy가 승인된 3회 상한과 양수/1초 cap/비감소 delay 구성을 강제하는지 검증한다.
	// 예상 결과: 250ms, 500ms, 1s 기본 정책은 유지되고 빈/과다/0/1초 초과/감소 구성은 거부된다.
	// 완료 조건: 최대 시도 횟수와 각 attempt 전 delay가 결정적이며 유효하지 않은 policy 생성이 모두 예외가 된다.
	[TestMethod]
	public void ReconnectPolicy_WhenConfigurationIsInvalid_RejectsOutsideBoundedThreeAttemptSchedule()
	{
		var policy = ReconnectPolicy.Conservative;

		Assert.AreEqual(3, policy.MaximumAttemptCount);
		Assert.AreEqual(TimeSpan.FromMilliseconds(250), policy.GetDelayBeforeAttempt(1));
		Assert.AreEqual(TimeSpan.FromMilliseconds(500), policy.GetDelayBeforeAttempt(2));
		Assert.AreEqual(TimeSpan.FromSeconds(1), policy.GetDelayBeforeAttempt(3));
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy());
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(1),
			TimeSpan.FromMilliseconds(2),
			TimeSpan.FromMilliseconds(3),
			TimeSpan.FromMilliseconds(4)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReconnectPolicy(
			TimeSpan.Zero,
			TimeSpan.FromMilliseconds(500),
			TimeSpan.FromSeconds(1)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(250),
			TimeSpan.FromMilliseconds(500),
			TimeSpan.FromMilliseconds(1001)));
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(500),
			TimeSpan.FromMilliseconds(250),
			TimeSpan.FromSeconds(1)));
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(249),
			TimeSpan.FromMilliseconds(500),
			TimeSpan.FromSeconds(1)));
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(250),
			TimeSpan.FromMilliseconds(499),
			TimeSpan.FromSeconds(1)));
		Assert.ThrowsExactly<ArgumentException>(() => new ReconnectPolicy(
			TimeSpan.FromMilliseconds(250),
			TimeSpan.FromMilliseconds(500),
			TimeSpan.FromMilliseconds(999)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => policy.GetDelayBeforeAttempt(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => policy.GetDelayBeforeAttempt(4));
	}
	// 목적: 진행 중인 connect cycle과 겹친 두 번째 cycle이 PLC 작업을 대기하거나 병렬 실행하지 않는지 검증한다.
	// 예상 결과: 두 번째 cycle은 즉시 SkippedBusy이고 connect/read/write 횟수는 첫 cycle 한 번분뿐이다.
	// 완료 조건: pending connect를 해제하기 전 두 번째 cycle이 완료되고 PLC 작업 중첩 없이 첫 cycle만 Completed가 된다.
	[TestMethod]
	public async Task CycleAsync_WhenConnectCycleOverlaps_ReturnsSkippedBusyWithoutPlcWork()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var connectCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 1, new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))),
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 2, new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))))
		{
			ConnectHandler = _ => connectCompletion.Task
		};
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var firstCycle = coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();
		var overlappingCycle = coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();
		var completedBeforeConnect = overlappingCycle.IsCompleted;
		connectCompletion.SetResult();
		var firstResult = await firstCycle;
		var overlappingResult = await overlappingCycle;

		Assert.IsTrue(completedBeforeConnect);
		Assert.AreEqual(EquipmentCycleDisposition.Completed, firstResult.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.SkippedBusy, overlappingResult.Disposition);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}
	// 목적: open-door PLC input이 coordinator의 Core observation mapping을 거쳐 active interlock alarm이 되는지 검증한다.
	// 예상 결과: Heating controller의 cycle result와 controller snapshot은 DoorOpen alarm을 나타내며 output write는 없다.
	// 완료 조건: coordinator가 PLC type을 Core에 전달하지 않고 one-cycle safety mapping을 test로 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenDoorIsOpenDuringHeating_MapsInputToDoorOpenAlarmWithoutWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(false, true, 20d, PlcMachineState.Idle, 0, 0, new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, result.Disposition);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.DoorOpen, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: producer observation sequence가 증가하지 않으면 stale input이 Core 상태를 재적용하지 않는지 검증한다.
	// 예상 결과: 두 번째 cycle은 StaleObservation이고 첫 accepted door observation으로 만든 controller snapshot이 유지된다.
	// 완료 조건: initial sequence 0/이후 sequence의 strict-increase policy가 output write 없이 test로 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenObservationSequenceIsNotIncreasing_ReturnsStaleWithoutMutatingController()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 1, new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))),
			new PlcInputSnapshot(false, true, 20d, PlcMachineState.Idle, 0, 1, new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var first = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var second = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, first.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, second.Disposition);
		Assert.IsFalse(second.ControllerSnapshot.IsDoorOpen);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: disconnected PLC port를 coordinator가 연결한 뒤 한 번의 input read만 처리하는지 검증한다.
	// 예상 결과: cycle은 Completed이고 controller snapshot과 accepted input을 반환하며 output write는 발생하지 않는다.
	// 완료 조건: Application이 PLC transport를 직접 구현하지 않고 read-only P3 cycle을 완결하는 상태로 test가 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenDisconnected_ConnectsReadsAndDoesNotWriteOutputs()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var plc = new RecordingPlcClient(new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20d,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0,
			sourceTransportIncarnation: new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Connected, result.ConnectionState);
		Assert.IsNotNull(result.InputSnapshot);
		Assert.AreEqual(0L, result.InputSnapshot.ObservationSequence);
		Assert.AreEqual(ControllerState.Idle, result.ControllerSnapshot.State);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 연결이 끊긴 활성 cycle의 ConnectAsync 전송 실패가 Core 통신 상실 알람으로 분류되지 않는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 controller는 Heating 상태와 알람 없음 상태를 유지하며 read/write가 없다.
	// 완료 조건: 연결 시도 1회, read 0회, write 0회이고 CommunicationLost가 발생하지 않는다.
	[TestMethod]
	public async Task CycleAsync_WhenConnectThrowsTransportException_ReturnsNonAlarmTransportFailure()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new RecordingPlcClient(new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20d,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0,
			sourceTransportIncarnation: new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))))
		{
			ThrowTransportExceptionOnConnect = true
		};
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Disconnected, result.ConnectionState);
		Assert.IsNull(result.InputSnapshot);
		Assert.AreEqual(ControllerState.Heating, result.ControllerSnapshot.State);
		Assert.IsNull(result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(0, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 활성 cycle의 typed PLC read fault가 Core 통신 알람으로 매핑되는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 controller는 CommunicationLost 알람이며 output write는 없다.
	// 완료 조건: cancellation으로 변환하지 않고 read boundary 결과와 fail-closed Core 상태가 함께 유지된다.
	[TestMethod]
	public async Task CycleAsync_WhenReadThrowsTransportException_RaisesCommunicationLostWithoutWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new TransportFailingObservationPort();
		using var cancellationSource = new CancellationTokenSource();
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, cancellationSource.Token);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.IsFalse(cancellationSource.IsCancellationRequested);
	}

	// 목적: Faulted 상태의 활성 관찰 포트가 재연결 없이 typed read 경계를 통과하는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 Faulted를 유지하며 controller는 CommunicationLost 알람에 진입한다.
	// 완료 조건: connect 0회, read 1회, write 0회이고 Core 통신 상실 상태가 반환된다.
	[TestMethod]
	public async Task CycleAsync_WhenFaultedReadThrowsTransportException_RaisesCommunicationLostWithoutReconnectOrWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new TransportFailingObservationPort(PlcConnectionState.Faulted);
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Faulted, result.ConnectionState);
		Assert.IsNull(result.InputSnapshot);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 확인된 typed read fault 뒤 복사된 이전 incarnation 관측을 거부하고 현재 새 incarnation만 동기화하는지 검증한다.
	// 예상 결과: A/101은 StaleObservation/WaitingForFreshInput이고 Core를 바꾸지 않으며 B/0만 Completed/Synchronized다.
	// 완료 조건: CommunicationLost는 유지되고 ConnectAsync 추가 호출·출력 쓰기·Recovery 진입이 없다.
	[TestMethod]
	public async Task CycleAsync_AfterReadFault_RejectsCopiedOldIncarnationAndAcceptsCurrentReset()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var sourceA = new PlcSourceTransportIncarnation(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
		var sourceB = new PlcSourceTransportIncarnation(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 100, sourceA),
			new PlcInputSnapshot(false, true, 99d, PlcMachineState.Idle, 0, 101, sourceA),
			new PlcInputSnapshot(true, true, 21d, PlcMachineState.Idle, 0, 0, sourceB))
		{
			CurrentSourceOverride = sourceA
		};
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var accepted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.FailNextReadKeepingConnection = true;
		var fault = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		plc.ForceConnected(sourceB);
		var copied = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var fresh = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, accepted.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, fault.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, copied.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.WaitingForFreshInput, copied.SynchronizationState);
		Assert.IsFalse(copied.ControllerSnapshot.IsDoorOpen);
		Assert.AreEqual(20d, copied.ControllerSnapshot.CurrentTemperature);
		Assert.AreEqual(AlarmKind.CommunicationLost, copied.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(EquipmentCycleDisposition.Completed, fresh.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.Synchronized, fresh.SynchronizationState);
		Assert.AreEqual(21d, fresh.ControllerSnapshot.CurrentTemperature);
		Assert.AreEqual(AlarmKind.CommunicationLost, fresh.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(ControllerState.Alarm, fresh.ControllerSnapshot.State);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 연결된 observation port에서 output-fault 무효화 뒤 같은 incarnation의 이후 관측만 재동기화하는지 검증한다.
	// 예상 결과: A/10은 WaitingForFreshInput이고 A/11은 Synchronized이며 ConnectAsync와 retry clock 변화가 없다.
	// 완료 조건: Idle Core는 알람 없이 유지되고 Recovery/Reset 권한이나 출력 쓰기가 없다.
	[TestMethod]
	public async Task CycleAsync_AfterOutputFaultWhileConnected_RequiresLaterSameIncarnationObservation()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var sourceA = new PlcSourceTransportIncarnation(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 10, sourceA),
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 10, sourceA),
			new PlcInputSnapshot(true, true, 22d, PlcMachineState.Idle, 0, 11, sourceA))
		{
			CurrentSourceOverride = sourceA
		};
		await using var coordinator = new EquipmentCoordinator(controller, plc);
		var accepted = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		typeof(EquipmentCoordinator)
			.GetMethod(
				"InvalidateSynchronizationAfterOutputTransportFailure",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.Invoke(coordinator, null);
		var stale = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var fresh = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, accepted.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, stale.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.WaitingForFreshInput, stale.SynchronizationState);
		Assert.AreEqual(20d, stale.ControllerSnapshot.CurrentTemperature);
		Assert.AreEqual(EquipmentCycleDisposition.Completed, fresh.Disposition);
		Assert.AreEqual(ConnectionSynchronizationState.Synchronized, fresh.SynchronizationState);
		Assert.AreEqual(22d, fresh.ControllerSnapshot.CurrentTemperature);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.AreEqual(ReconnectFailureKind.None, fresh.LastReconnectFailure);
	}

	private sealed class ThrowOnThirdTimestampTimeProvider : TimeProvider
	{
		private readonly Exception _exception;
		private long _timestamp;
		private int _callCount;

		public ThrowOnThirdTimestampTimeProvider(Exception exception) => _exception = exception;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp()
		{
			if (++_callCount == 3)
			{
				throw _exception;
			}

			return _timestamp;
		}

		public void Advance(TimeSpan elapsed) => _timestamp = checked(_timestamp + elapsed.Ticks);
	}
	private sealed class RejectPostThirdReadFaultTimestampTimeProvider : TimeProvider
	{
		private readonly Exception _exception;
		private long _timestamp;
		private bool _rejectTimestampReads;

		public RejectPostThirdReadFaultTimestampTimeProvider(Exception exception) => _exception = exception;

		public int TimestampCallCount { get; private set; }
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp()
		{
			if (_rejectTimestampReads)
			{
				throw _exception;
			}

			TimestampCallCount++;
			return _timestamp;
		}

		public void Advance(TimeSpan elapsed) => _timestamp = checked(_timestamp + elapsed.Ticks);
		public void RejectTimestampReads() => _rejectTimestampReads = true;
	}

	private sealed class StateChangingObservationPort : IPlcClient
	{
		private PlcConnectionState _connectionState = PlcConnectionState.Connected;
		private bool _connectExternallyAfterNextStateRead;

		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public PlcSourceTransportIncarnation? CurrentSourceTransportIncarnation { get; private set; } =
			new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		public PlcConnectionState ConnectionState
		{
			get
			{
				var currentState = _connectionState;
				if (_connectExternallyAfterNextStateRead)
				{
					_connectExternallyAfterNextStateRead = false;
					_connectionState = PlcConnectionState.Connected;
					CurrentSourceTransportIncarnation =
						new PlcSourceTransportIncarnation(Guid.Parse("22222222-2222-2222-2222-222222222222"));
				}

				return currentState;
			}
		}

		public void ConnectExternallyAfterNextStateRead() => _connectExternallyAfterNextStateRead = true;

		public Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			_connectionState = PlcConnectionState.Connected;
			CurrentSourceTransportIncarnation =
				new PlcSourceTransportIncarnation(Guid.Parse("22222222-2222-2222-2222-222222222222"));
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_connectionState = PlcConnectionState.Disconnected;
			CurrentSourceTransportIncarnation = null;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			if (ReadCallCount == 1)
			{
				_connectionState = PlcConnectionState.Disconnected;
				CurrentSourceTransportIncarnation = null;
				throw new PlcTransportException("Confirmed first read transport failure.");
			}

			return Task.FromResult(new PlcInputSnapshot(
				doorClosed: true,
				sensorHealthy: true,
				currentTemperature: 20d,
				machineState: PlcMachineState.Idle,
				acknowledgedCommandId: 0,
				observationSequence: 1,
				sourceTransportIncarnation: CurrentSourceTransportIncarnation
					?? new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))));
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			_connectionState = PlcConnectionState.Disconnected;
			CurrentSourceTransportIncarnation = null;
			return ValueTask.CompletedTask;
		}
	}
	private sealed class ThrowOnSecondTimestampTimeProvider : TimeProvider
	{
		private readonly Exception _exception;
		private int _callCount;

		public ThrowOnSecondTimestampTimeProvider(Exception exception) => _exception = exception;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp()
		{
			if (++_callCount == 2)
			{
				throw _exception;
			}

			return 0;
		}
	}
	private static EquipmentCoordinator CreateCoordinator(
		ThermalController controller,
		IPlcObservationPort observationPort,
		TimeProvider timeProvider,
		ReconnectPolicy reconnectPolicy) =>
		(EquipmentCoordinator)Activator.CreateInstance(
			typeof(EquipmentCoordinator),
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
			binder: null,
			args: new object[] { controller, observationPort, timeProvider, reconnectPolicy },
			culture: null)!;
	private sealed class ReconnectFailingObservationPort : IPlcClient
	{
		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public Action<int>? BeforeReadFailure { get; init; }
		public Func<CancellationToken, Task>? ConnectHandler { get; init; }
		public bool ConnectSucceeds { get; init; }
		public int? ReadFailureKeepingConnectionState { get; init; }
		public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Connected;
		public PlcSourceTransportIncarnation? CurrentSourceTransportIncarnation =>
			ConnectionState == PlcConnectionState.Connected
				? new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))
				: null;

		public async Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			if (ConnectHandler is not null)
			{
				await ConnectHandler(cancellationToken);
			}
			if (ConnectSucceeds)
			{
				ConnectionState = PlcConnectionState.Connected;
				return;
			}

			throw new PlcTransportException("Controlled reconnect transport failure.");
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			if (ReadFailureKeepingConnectionState != ReadCallCount)
			{
				ConnectionState = PlcConnectionState.Disconnected;
			}
			BeforeReadFailure?.Invoke(ReadCallCount);
			throw new PlcTransportException("Confirmed read transport failure.");
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}
	}
	private sealed class TransportFailingObservationPort : IPlcClient
	{
		public TransportFailingObservationPort(
			PlcConnectionState connectionState = PlcConnectionState.Connected)
		{
			ConnectionState = connectionState;
		}

		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public PlcConnectionState ConnectionState { get; private set; }
		public PlcSourceTransportIncarnation? CurrentSourceTransportIncarnation =>
			ConnectionState == PlcConnectionState.Connected
				? new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"))
				: null;

		public Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			ConnectionState = PlcConnectionState.Connected;
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			throw new PlcTransportException("Confirmed read transport failure.");
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(
				command.CommandId,
				PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingPlcClient : IPlcClient
	{
		private readonly Queue<PlcInputSnapshot> _inputSnapshots;

		public RecordingPlcClient(params PlcInputSnapshot[] inputSnapshots)
		{
			if (inputSnapshots.Length == 0)
			{
				throw new ArgumentException("At least one input snapshot is required.", nameof(inputSnapshots));
			}

			_inputSnapshots = new Queue<PlcInputSnapshot>(inputSnapshots);
		}

		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public bool ThrowTransportExceptionOnConnect { get; init; }
		public Func<CancellationToken, Task>? ConnectHandler { get; init; }
		public PlcSourceTransportIncarnation? CurrentSourceOverride { get; set; } =
			new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		public PlcConnectionState? ConnectionStateOverride { get; set; }
		public bool FailNextReadKeepingConnection { get; set; }
		public void ForceConnected(PlcSourceTransportIncarnation source)
		{
			CurrentSourceOverride = source;
			ConnectionState = PlcConnectionState.Connected;
		}

		public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Disconnected;
		public PlcSourceTransportIncarnation? CurrentSourceTransportIncarnation =>
			ConnectionState == PlcConnectionState.Connected
				? CurrentSourceOverride
				: null;

		public async Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			if (ThrowTransportExceptionOnConnect)
			{
				throw new PlcTransportException("Confirmed connect transport failure.");
			}
			if (ConnectHandler is not null)
			{
				await ConnectHandler(cancellationToken);
			}
			ConnectionState = ConnectionStateOverride ?? PlcConnectionState.Connected;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			if (FailNextReadKeepingConnection)
			{
				FailNextReadKeepingConnection = false;
				throw new PlcTransportException("Confirmed read transport failure.");
			}
			if (_inputSnapshots.Count == 0)
			{
				throw new InvalidOperationException("No input snapshot remains.");
			}

			return Task.FromResult(_inputSnapshots.Dequeue());
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(
				command.CommandId,
				PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}
	}
}
