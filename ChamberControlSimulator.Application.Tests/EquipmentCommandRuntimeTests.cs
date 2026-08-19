using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ChamberControlSimulator.Application.Tests;

[TestClass]
public sealed class EquipmentCommandRuntimeTests
{
	// 목적: latest fresh Completed P3 baseline 없이 Start request가 reservation ID나 output write를 만들지 않는지 검증한다.
	// 예상 결과: BaselineRequired, null command ID, zero write이며 Core는 Idle/event-empty다.
	// 완료 조건: P4 admission/dispatch가 accepted fresh observation baseline 뒤에서만 열린다.
	[TestMethod]
	public async Task RequestStartAsync_WithoutCompletedBaseline_PerformsNoAdmissionOrWrite()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);

		var result = await runtime.RequestStartAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.BaselineRequired, result.Disposition);
		Assert.IsNull(result.CommandId);
		Assert.AreEqual(0, ports.WriteCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: most recent P3 cycle이 stale 또는 transport-failed이면 earlier Completed baseline을 재사용하지 않는지 검증한다.
	// 예상 결과: 두 runtime 모두 BaselineRequired이고 command ID allocation/output write가 없다.
	// 완료 조건: dispatch baseline freshness가 latest accepted cycle state에 fail-closed로 묶인다.
	[TestMethod]
	public async Task RequestStartAsync_AfterStaleOrTransportFailedCycle_PerformsNoAdmissionOrWrite()
	{
		var stalePorts = new ControlledPlcPorts();
		stalePorts.EnqueueSnapshot(Snapshot(sequence: 4));
		stalePorts.EnqueueSnapshot(Snapshot(sequence: 4));
		await using var staleRuntime = new EquipmentCommandRuntime(CreateController(), stalePorts, stalePorts, TimeProvider.System);
		Assert.AreEqual(EquipmentCycleDisposition.Completed, (await staleRuntime.CycleAsync(TimeSpan.Zero, CancellationToken.None)).ObservationResult.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, (await staleRuntime.CycleAsync(TimeSpan.Zero, CancellationToken.None)).ObservationResult.Disposition);

		var staleRequest = await staleRuntime.RequestStartAsync(CancellationToken.None);

		var failedPorts = new ControlledPlcPorts();
		failedPorts.FailNextRead();
		await using var failedRuntime = new EquipmentCommandRuntime(CreateController(), failedPorts, failedPorts, TimeProvider.System);
		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, (await failedRuntime.CycleAsync(TimeSpan.Zero, CancellationToken.None)).ObservationResult.Disposition);
		var failedRequest = await failedRuntime.RequestStartAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.BaselineRequired, staleRequest.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.BaselineRequired, failedRequest.Disposition);
		Assert.AreEqual(0, stalePorts.WriteCount);
		Assert.AreEqual(0, failedPorts.WriteCount);
	}

	// 목적: baseline observed ACK가 Application allocator보다 높을 때 새 Start command ID가 두 watermark 모두 초과하는지 검증한다.
	// 예상 결과: ACK 50 baseline 뒤 command ID 51이 exact one write에 사용되고 AwaitingAcknowledgement를 반환한다.
	// 완료 조건: process-local allocator가 observed PLC ACK high-water와 충돌하거나 stale ID를 재사용하지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_AfterHighAcknowledgementBaseline_AllocatesStrictlyHigherId()
	{
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 10, acknowledgedCommandId: 50));
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var result = await runtime.RequestStartAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, result.Disposition);
		Assert.AreEqual(51L, result.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		Assert.IsNotNull(ports.LastCommand);
		Assert.AreEqual(51L, ports.LastCommand.CommandId);
		Assert.AreEqual(PlcCommandKind.Start, ports.LastCommand.Kind);
	}

	// 목적: matching Written receipt만으로는 Core가 변하지 않고 later exact fresh ACK만 Start를 한 번 complete하는지 검증한다.
	// 예상 결과: request 뒤 Idle/event-empty이며 sequence 2 exact ACK cycle 뒤 Heating과 one Start event, duplicate ACK 뒤에도 one Start다.
	// 완료 조건: transport receipt와 semantic completion이 분리되고 exact accepted ACK가 one-shot Core transition을 만든다.
	[TestMethod]
	public async Task CycleAsync_LaterExactFreshAcknowledgement_CompletesStartExactlyOnce()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: request.CommandId!.Value));

		var completed = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: request.CommandId.Value));
		var duplicate = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, completed.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, duplicate.CommandDisposition);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Start"));
	}

	// 목적: stale exact ACK와 lower ACK는 completion하지 않고 higher/wrong ACK는 terminal reconciliation hold로 들어가는지 검증한다.
	// 예상 결과: stale/lower cycle은 AwaitingAcknowledgement, higher cycle은 ReconciliationRequired이며 later exact ACK도 Core를 시작하지 않는다.
	// 완료 조건: accepted freshness와 exact ID 둘 다 없으면 completion seam이 호출되지 않고 wrong-high ambiguity가 fail-closed다.
	[TestMethod]
	public async Task CycleAsync_StaleLowerAndHigherAcknowledgements_DoNotComplete()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 5));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		var id = request.CommandId!.Value;

		ports.EnqueueSnapshot(Snapshot(sequence: 5, acknowledgedCommandId: id));
		var stale = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 6, acknowledgedCommandId: id - 1));
		var lower = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 7, acknowledgedCommandId: id + 1));
		var higher = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 8, acknowledgedCommandId: id));
		var laterExact = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, stale.ObservationResult.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, stale.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, lower.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, higher.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, laterExact.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: exact ACK observation이 먼저 unsafe door input을 Core에 mapping하면 safe ABA observation으로 held reservation을 revive하지 않는지 검증한다.
	// 예상 결과: unsafe exact cycle은 AcknowledgedButCoreIneligible, later safe exact도 같은 terminal hold이며 Core는 Idle이다.
	// 완료 조건: P3 mapping-before-completion과 Core revalidation이 unsafe-then-safe late ACK replay를 차단한다.
	[TestMethod]
	public async Task CycleAsync_ExactAcknowledgementWithUnsafeObservation_CannotReviveAfterSafeAba()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		var id = request.CommandId!.Value;
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: id, doorClosed: false));

		var unsafeAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: id, doorClosed: true));
		var safeAba = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible, unsafeAck.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible, safeAba.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: shared runtime gate가 in-flight P4 write와 P3 input read를 같은 transport에서 overlap시키지 않는지 검증한다.
	// 예상 결과: blocked write 동안 requested cycle은 read를 시작하지 않고 release 뒤 두 operation이 끝나며 overlap flag는 false다.
	// 완료 조건: P4 write와 P3 read가 one async serialization boundary를 공유한다.
	[TestMethod]
	public async Task RuntimeGate_SerializesP4WriteAndP3ReadWithoutOverlap()
	{
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (command, _) => writeCompletion.Task;

		var requestTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var readCountBeforeConcurrentCycle = ports.ReadCount;
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();

		Assert.AreEqual(readCountBeforeConcurrentCycle, ports.ReadCount);
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		await Task.WhenAll(requestTask, cycleTask);
		Assert.IsFalse(ports.OverlapDetected);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: output write exception 뒤 exact ACK가 도착해도 delivery-indeterminate hold에서 Core completion을 재개하지 않는지 검증한다.
	// 예상 결과: request는 exception을 전달하고 later exact ACK cycle은 ReconciliationRequired, Core Idle/event-empty다.
	// 완료 조건: ambiguous write exception path가 retry, release, 또는 retroactive Start completion을 허용하지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_WriteException_BlocksLaterExactAcknowledgement()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		ports.WriteHandler = (_, _) => throw new InvalidOperationException("controlled write exception");
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => runtime.RequestStartAsync(CancellationToken.None));
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var laterExact = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, laterExact.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: Heating 중 Stop write의 typed transport failure가 P4 hold와 Core 통신 알람을 함께 유지하는지 검증한다.
	// 예상 결과: 동일한 PlcTransportException이 전달되고 runtime은 ReconciliationRequired, Core는 CommunicationLost 알람이다.
	// 완료 조건: command ID 1의 단일 write 뒤 retry, replay, release, 또는 Core Stop 이벤트가 발생하지 않는다.
	[TestMethod]
	public async Task RequestStopAsync_TransportFailure_RaisesCommunicationLostAndPreservesReconciliationHold()
	{
		var controller = CreateController();
		controller.Start();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var transportException = new PlcTransportException("controlled write transport failure");
		ports.WriteHandler = (_, _) => throw transportException;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var thrown = await Assert.ThrowsExactlyAsync<PlcTransportException>(
			() => runtime.RequestStopAsync(CancellationToken.None));

		Assert.AreSame(transportException, thrown);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, runtime.CurrentState.Disposition);
		Assert.AreEqual(1L, runtime.CurrentState.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		Assert.IsNotNull(ports.LastCommand);
		Assert.AreEqual(1L, ports.LastCommand.CommandId);
		Assert.AreEqual(PlcCommandKind.Stop, ports.LastCommand.Kind);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Stop"));

		var blockedRetry = await runtime.RequestStopAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, blockedRetry.Disposition);
		Assert.IsNull(blockedRetry.CommandId);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, runtime.CurrentState.Disposition);
		Assert.AreEqual(1L, runtime.CurrentState.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Stop"));
	}

	// 목적: Heating 중 write 이전 TimeProvider의 typed transport fault가 통신 손실로 잘못 분류되지 않는지 검증한다.
	// 예상 결과: 동일한 PlcTransportException과 P4 ReconciliationRequired hold를 유지하지만 Core는 Heating이고 write는 0회다.
	// 완료 조건: non-write fault가 CommunicationLost를 만들지 않고 command ID/kind와 closed admission을 유지한다.
	[TestMethod]
	public async Task RequestStopAsync_TimeProviderTransportFailureBeforeWrite_DoesNotRaiseCommunicationLost()
	{
		var controller = CreateController();
		controller.Start();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var transportException = new PlcTransportException("controlled TimeProvider transport failure");
		var timeProvider = new ThrowingTimestampTimeProvider(transportException);
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var thrown = await Assert.ThrowsExactlyAsync<PlcTransportException>(
			() => runtime.RequestStopAsync(CancellationToken.None));
		var blockedRetry = await runtime.RequestStopAsync(CancellationToken.None);

		Assert.AreSame(transportException, thrown);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, runtime.CurrentState.Disposition);
		Assert.AreEqual(1L, runtime.CurrentState.CommandId);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.IsNull(controller.Snapshot.ActiveAlarm);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, blockedRetry.Disposition);
		Assert.IsNull(blockedRetry.CommandId);
		Assert.AreEqual(0, ports.WriteCount);
		Assert.IsNull(ports.LastCommand);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Stop"));
	}

	// 목적: in-flight output cancellation 뒤 exact ACK가 와도 terminal reconciliation hold를 벗어나지 않는지 검증한다.
	// 예상 결과: request는 cancellation을 전달하고 later exact ACK cycle은 ReconciliationRequired, Core Idle/event-empty다.
	// 완료 조건: caller cancellation이 uncertain delivery를 reservation release나 retroactive Start success로 바꾸지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_InFlightCancellation_BlocksLaterExactAcknowledgement()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		ports.WriteHandler = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return new PlcWriteReceipt(1, PlcTransportWriteStatus.Written);
		};
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		using var cancellation = new CancellationTokenSource();
		var requestTask = runtime.RequestStartAsync(cancellation.Token);
		await ports.WriteStarted.Task;

		cancellation.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => requestTask);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var laterExact = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, laterExact.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: T5 command runtime이 P3 observation boundary를 보존하면서 Start/Stop/Reset narrow request만 노출하는지 검증한다.
	// 예상 결과: 세 named request method가 있고 public generic command-kind method나 broad IPlcClient field는 없다.
	// 완료 조건: command-family completeness가 P3 또는 caller authority widening 없이 성립한다.
	[TestMethod]
	public void CapabilityBoundary_PreservesP3AndExposesCommandFamilyRuntime()
	{
		var p3Parameters = typeof(EquipmentCoordinator).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
		var runtimeParameters = typeof(EquipmentCommandRuntime).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
		var publicMethods = typeof(EquipmentCommandRuntime).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

		CollectionAssert.AreEqual(new[] { typeof(ThermalController), typeof(IPlcObservationPort) }, p3Parameters);
		CollectionAssert.AreEqual(new[] { typeof(ThermalController), typeof(IPlcObservationPort), typeof(IPlcOutputPort), typeof(TimeProvider) }, runtimeParameters);
		CollectionAssert.AreEquivalent(
			new[] { "CycleAsync", "DisposeAsync", "get_CurrentState", "RequestResetAsync", "RequestStartAsync", "RequestStopAsync", "StopAdmission" },
			publicMethods.Select(method => method.Name).ToArray());
		Assert.IsFalse(publicMethods.Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ControllerCommandKind))));
		Assert.IsFalse(typeof(EquipmentCommandRuntime).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Any(field => field.FieldType == typeof(IPlcClient)));
	}

	// 목적: successful Start semantic completion 뒤 global fence가 열리고 Stop이 같은 lifecycle로 exact ACK completion하는지 검증한다.
	// 예상 결과: Start ID 1 completion 뒤 Stop ID 2 Written은 Heating을 유지하고 fresh exact ACK만 Idle/one Stop으로 전환한다.
	// 완료 조건: success만 next command admission을 열며 Stop receipt나 legacy direct call이 Core를 바꾸지 않는다.
	[TestMethod]
	public async Task CommandFamily_StartThenStop_ReleasesFenceOnlyAfterEachExactAcknowledgement()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: start.CommandId!.Value));
		var started = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var stop = await runtime.RequestStopAsync(CancellationToken.None);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, stop.Disposition);
		Assert.AreEqual(2L, stop.CommandId);
		Assert.AreEqual(PlcCommandKind.Stop, ports.LastCommand!.Kind);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: stop.CommandId.GetValueOrDefault()));
		var stopped = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, started.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, stopped.CommandDisposition);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Start"));
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Stop"));
		Assert.AreEqual(2, ports.WriteCount);
	}

	// 목적: Recovery-ready 이전 Reset request가 reservation ID와 output write를 만들지 않는지 검증한다.
	// 예상 결과: AdmissionRejected/null ID/zero write이며 Core는 Idle이다.
	// 완료 조건: Reset admission은 View state가 아니라 Core Recovery eligibility에 묶인다.
	[TestMethod]
	public async Task RequestResetAsync_BeforeRecoveryReady_PerformsNoAdmissionOrWrite()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var result = await runtime.RequestResetAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, result.Disposition);
		Assert.IsNull(result.CommandId);
		Assert.AreEqual(0, ports.WriteCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: eligible Reset Written이 Core를 바꾸지 않고 later exact fresh ACK만 Reset을 한 번 complete하는지 검증한다.
	// 예상 결과: request 뒤 Recovery, exact ACK 뒤 Idle/Completed/one Reset이며 admitted kind evidence는 Reset이다.
	// 완료 조건: Reset이 Start/Stop과 같은 semantic ACK 및 Core revalidation lifecycle을 따른다.
	[TestMethod]
	public async Task RequestResetAsync_WrittenThenExactFreshAcknowledgement_ResetsExactlyOnce()
	{
		var controller = CreateRecoveryReadyController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var request = await runtime.RequestResetAsync(CancellationToken.None);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, request.Disposition);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.AreEqual(PlcCommandKind.Reset, ports.LastCommand!.Kind);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: request.CommandId!.Value));
		var completed = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: request.CommandId.Value));
		var duplicate = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, completed.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, duplicate.CommandDisposition);
		Assert.AreEqual(ControllerCommandKind.Reset, runtime.CurrentState.Kind);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Reset"));
	}

	// 목적: unresolved Start receipt timeout이 Stop/Reset을 포함한 모든 command kind의 새 ID/write를 막는지 검증한다.
	// 예상 결과: Stop과 Reset은 AdmissionRejected이고 original Start ID/kind와 one write만 유지된다.
	// 완료 조건: Reset이나 Stop이 uncertainty 또는 noncooperative lease를 clear/preempt하지 않는다.
	[TestMethod]
	public async Task RequestCommandAsync_AfterReceiptTimeout_BlocksStopAndResetWithoutPreemption()
	{
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var startTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		var timedOut = await startTask;

		var stop = await runtime.RequestStopAsync(CancellationToken.None);
		var reset = await runtime.RequestResetAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, timedOut.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, stop.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, reset.Disposition);
		Assert.AreEqual(ControllerCommandKind.Start, runtime.CurrentState.Kind);
		Assert.AreEqual(1, ports.WriteCount);
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
	}

	// 목적: output write가 invocation 뒤 정확히 3초 동안 미완료이면 receipt-timeout hold가 되고 실제 I/O가 끝날 때까지 shared lease를 유지하는지 검증한다.
	// 예상 결과: request는 ReceiptTimedOut를 반환하고 concurrent cycle은 write completion 전 read하지 않으며 eventual Written/exact ACK도 Core를 시작하지 않는다.
	// 완료 조건: receipt deadline이 admission이 아닌 write invocation에 묶이고 timeout이 lease release나 retroactive success를 만들지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_WriteStillInFlightAtReceiptDeadline_HoldsLeaseAndCannotRevive()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		var requestTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		var timedOut = await requestTask;
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var readCount = ports.ReadCount;
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, timedOut.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, runtime.CurrentState.Disposition);
		Assert.AreEqual(readCount, ports.ReadCount);
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		var lateCycle = await cycleTask;
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, lateCycle.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: Heating Stop write의 receipt timeout 뒤 늦은 typed 전송 실패가 Core 통신 상실로 보고되는지 검증한다.
	// 예상 결과: late PlcTransportException 뒤에도 ReceiptTimedOut과 command ID 1을 유지하며 CommunicationLost 알람에 진입한다.
	// 완료 조건: write 1회, 재시도/Stop 이벤트/예약 해제 없이 admission이 닫힌 상태로 유지된다.
	[TestMethod]
	public async Task RequestStopAsync_LateTransportFailureAfterReceiptTimeout_RaisesCommunicationLostAndPreservesHold()
	{
		var controller = CreateController();
		controller.Start();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var requestTask = runtime.RequestStopAsync(CancellationToken.None);
		await ports.WriteStarted.Task;
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		var timedOut = await requestTask;
		ports.EnqueueSnapshot(Snapshot(sequence: 2));
		var readCount = ports.ReadCount;
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();

		Assert.AreEqual(readCount, ports.ReadCount);
		writeCompletion.SetException(new PlcTransportException("controlled late write transport failure"));
		var lateCycle = await cycleTask;
		var blockedRetry = await runtime.RequestStopAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, timedOut.Disposition);
		Assert.AreEqual(1L, timedOut.CommandId);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, lateCycle.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, runtime.CurrentState.Disposition);
		Assert.AreEqual(1L, runtime.CurrentState.CommandId);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, blockedRetry.Disposition);
		Assert.IsNull(blockedRetry.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		Assert.IsNotNull(ports.LastCommand);
		Assert.AreEqual(1L, ports.LastCommand.CommandId);
		Assert.AreEqual(PlcCommandKind.Stop, ports.LastCommand.Kind);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Stop"));
	}

	// 목적: semantic ACK deadline이 admission/write start가 아니라 matching Written receipt 뒤에만 시작되는지 monotonic fake time으로 검증한다.
	// 예상 결과: write invocation 후 총 5초가 지나도 Written 뒤 3초 전에는 대기하고 정확히 3초의 later exact ACK는 AcknowledgementTimedOut다.
	// 완료 조건: receipt와 ACK deadline epoch가 분리되고 exact boundary가 fail-closed다.
	[TestMethod]
	public async Task CycleAsync_AcknowledgementDeadlineStartsAfterMatchingWrittenReceipt()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var requestTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;
		timeProvider.Advance(TimeSpan.FromSeconds(2));
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		var request = await requestTask;

		timeProvider.Advance(TimeSpan.FromMilliseconds(2999));
		ports.EnqueueSnapshot(Snapshot(sequence: 2));
		var beforeDeadline = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromMilliseconds(1));
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: request.CommandId!.Value));
		var exactAtDeadline = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, beforeDeadline.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut, exactAtDeadline.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: caller cancellation을 무시하는 in-flight write가 cancellation 뒤에도 P3/P4 shared lease를 실제 completion까지 보유하는지 검증한다.
	// 예상 결과: request는 cancellation을 전달하고 ReconciliationRequired가 되며 cycle read는 eventual receipt 전 시작하지 않고 exact ACK도 revive하지 않는다.
	// 완료 조건: post-reservation cancellation이 safe non-dispatch나 lease release로 오인되지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_NonCooperativeWriteCancellation_RetainsLeaseUntilWriteSettles()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		using var cancellation = new CancellationTokenSource();
		var requestTask = runtime.RequestStartAsync(cancellation.Token);
		await ports.WriteStarted.Task;
		cancellation.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => requestTask);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var readCount = ports.ReadCount;
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await Task.Yield();

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, runtime.CurrentState.Disposition);
		Assert.AreEqual(readCount, ports.ReadCount);
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		var lateCycle = await cycleTask;
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReconciliationRequired, lateCycle.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: writing과 receipt-timeout hold 중 duplicate Start가 새 ID나 output write를 만들지 않는지 검증한다.
	// 예상 결과: 두 duplicate 모두 AdmissionRejected이고 original ID 1의 write 한 번만 존재한다.
	// 완료 조건: global outstanding-command fence가 timing state와 무관하게 유지된다.
	[TestMethod]
	public async Task RequestStartAsync_DuringWritingAndTimeout_PerformsNoDuplicateWriteOrAllocation()
	{
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var originalTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;

		var duringWrite = await runtime.RequestStartAsync(CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		var original = await originalTask;
		var duringTimeout = await runtime.RequestStartAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, duringWrite.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, original.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, duringTimeout.Disposition);
		Assert.AreEqual(1L, original.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
	}

	// 목적: ACK timeout 뒤 disconnect/reconnect observation이 held command deadline이나 disposition을 reset하지 않는지 검증한다.
	// 예상 결과: reconnect cycle이 exact ACK를 읽어도 AcknowledgementTimedOut과 Core Idle이 유지되고 resend가 없다.
	// 완료 조건: connection transition은 read-only reconciliation evidence이고 recovery policy가 아니다.
	[TestMethod]
	public async Task CycleAsync_ReconnectAfterAcknowledgementTimeout_CannotClearHoldOrResend()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		ports.DisconnectForTest();
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: request.CommandId!.Value));

		var reconnected = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(PlcConnectionState.Connected, reconnected.ObservationResult.ConnectionState);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut, reconnected.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.AreEqual(1, ports.WriteCount);
	}

	// 목적: close ownership이 admission을 동기적으로 닫은 뒤 새 Start reservation/write를 만들 수 없는지 검증한다.
	// 예상 결과: StopAdmission 뒤 request는 AdmissionRejected/null이고 Core와 output은 unchanged다.
	// 완료 조건: Form teardown이 cancellation 전에 admission gate를 닫을 수 있는 narrow seam을 가진다.
	[TestMethod]
	public async Task StopAdmission_BeforeRequest_PreventsReservationAndWrite()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		runtime.StopAdmission();
		var result = await runtime.RequestStartAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, result.Disposition);
		Assert.IsNull(result.CommandId);
		Assert.AreEqual(0, ports.WriteCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: receipt와 3초 deadline이 같은 monotonic timestamp에 settle하면 timeout이 tie를 이기는지 검증한다.
	// 예상 결과: physical receipt Task도 complete이지만 result는 ReceiptTimedOut이고 ACK epoch/Core transition은 시작하지 않는다.
	// 완료 조건: Task.WhenAny scheduling order가 exact deadline의 fail-closed 판정을 바꾸지 않는다.
	[TestMethod]
	public async Task RequestStartAsync_ReceiptCompletesAtExactDeadline_TimeoutWinsTie()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var requestTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;

		timeProvider.Advance(TimeSpan.FromSeconds(3));
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		var result = await requestTask;

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, result.Disposition);
		Assert.IsNull(runtime.CurrentState.AcknowledgementStartedTimestamp);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: exact receipt deadline에 이미 settle된 Stop typed write fault가 timeout 우선순위와 통신 손실을 함께 유지하는지 검증한다.
	// 예상 결과: request는 원래 ID/kind의 ReceiptTimedOut이고 Core는 CommunicationLost 알람이며 admission은 닫힌다.
	// 완료 조건: write 1회 뒤 Stop event, retry, replay, reservation release 없이 P4 timeout hold가 유지된다.
	[TestMethod]
	public async Task RequestStopAsync_TransportFailureAtExactReceiptDeadline_TimeoutWinsAndRaisesCommunicationLost()
	{
		var controller = CreateController();
		controller.Start();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>();
		var transportException = new PlcTransportException("controlled exact-deadline write transport failure");
		using var transportFailureTimer = timeProvider.CreateTimer(
			_ => writeCompletion.SetException(transportException),
			null,
			TimeSpan.FromSeconds(3),
			Timeout.InfiniteTimeSpan);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var requestTask = runtime.RequestStopAsync(CancellationToken.None);
		await ports.WriteStarted.Task;

		timeProvider.Advance(TimeSpan.FromSeconds(3));
		EquipmentCommandRequestResult? timedOut = null;
		try
		{
			timedOut = await requestTask;
		}
		catch (Exception exception)
		{
			Assert.Fail($"Expected ReceiptTimedOut, but {exception.GetType().Name} was thrown with {runtime.CurrentState.Disposition}.");
		}
		var blockedRetry = await runtime.RequestStopAsync(CancellationToken.None);

		Assert.IsNotNull(timedOut);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, timedOut.Disposition);
		Assert.AreEqual(1L, timedOut.CommandId);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, runtime.CurrentState.Disposition);
		Assert.AreEqual(1L, runtime.CurrentState.CommandId);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.CurrentState.Kind);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, controller.Snapshot.ActiveAlarm);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, blockedRetry.Disposition);
		Assert.IsNull(blockedRetry.CommandId);
		Assert.AreEqual(1, ports.WriteCount);
		Assert.IsNotNull(ports.LastCommand);
		Assert.AreEqual(1L, ports.LastCommand.CommandId);
		Assert.AreEqual(PlcCommandKind.Stop, ports.LastCommand.Kind);
		Assert.IsFalse(controller.EventHistory.Any(entry => entry.Event == "Stop"));
	}

	// 목적: deadline callback 관찰이 늦더라도 monotonic elapsed가 3초 이상인 late receipt를 success로 수락하지 않는지 검증한다.
	// 예상 결과: 5초 뒤 Written은 ReceiptTimedOut이고 later exact ACK도 Core를 시작하지 않는다.
	// 완료 조건: receipt timeliness가 continuation ordering이 아니라 recorded invocation timestamp로 판정된다.
	[TestMethod]
	public async Task RequestStartAsync_LateReceiptWithDelayedContinuation_RemainsTimedOut()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var requestTask = runtime.RequestStartAsync(CancellationToken.None);
		await ports.WriteStarted.Task;

		timeProvider.Advance(TimeSpan.FromSeconds(5));
		writeCompletion.SetResult(new PlcWriteReceipt(1, PlcTransportWriteStatus.Written));
		var result = await requestTask;
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: 1));
		var lateAck = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, result.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, lateAck.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: ACK timeout을 확정한 cycle의 read가 취소되어도 timeout evidence가 generic reconciliation으로 덮이지 않는지 검증한다.
	// 예상 결과: cycle은 cancellation을 전달하고 CurrentState는 AcknowledgementTimedOut을 유지한다.
	// 완료 조건: terminal timeout classification이 later cancellation보다 안정적이다.
	[TestMethod]
	public async Task CycleAsync_CancellationAfterAcknowledgementDeadline_PreservesTimeoutState()
	{
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		await runtime.RequestStartAsync(CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.ReadHandler = async cancellationToken =>
		{
			readStarted.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return Snapshot(sequence: 2);
		};
		using var cancellation = new CancellationTokenSource();
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, cancellation.Token);
		await readStarted.Task;

		cancellation.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cycleTask);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut, runtime.CurrentState.Disposition);
	}

	// 목적: completed command 뒤 observation cancellation이 durable Completed evidence를 generic reconciliation로 퇴행시키지 않는지 검증한다.
	// 예상 결과: canceled read 뒤에도 Completed와 one Start event가 유지된다.
	// 완료 조건: semantic commit 이후 transport cancellation은 이미 완료된 lifecycle을 재분류하지 않는다.
	[TestMethod]
	public async Task CycleAsync_CancellationAfterCompletion_PreservesCompletedState()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: request.CommandId!.Value));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.ReadHandler = async cancellationToken =>
		{
			readStarted.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return Snapshot(sequence: 3);
		};
		using var cancellation = new CancellationTokenSource();
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, cancellation.Token);
		await readStarted.Task;

		cancellation.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cycleTask);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, runtime.CurrentState.Disposition);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Start"));
	}

	// 목적: unsafe exact ACK로 Core-ineligible terminal hold가 된 뒤 observation cancellation이 그 evidence를 보존하는지 검증한다.
	// 예상 결과: canceled read 뒤에도 AcknowledgedButCoreIneligible와 Core Idle이 유지된다.
	// 완료 조건: terminal Core revalidation failure가 generic cancellation ambiguity로 덮이지 않는다.
	[TestMethod]
	public async Task CycleAsync_CancellationAfterCoreIneligibleAck_PreservesTerminalState()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var request = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: request.CommandId!.Value, doorClosed: false));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.ReadHandler = async cancellationToken =>
		{
			readStarted.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return Snapshot(sequence: 3);
		};
		using var cancellation = new CancellationTokenSource();
		var cycleTask = runtime.CycleAsync(TimeSpan.Zero, cancellation.Token);
		await readStarted.Task;

		cancellation.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cycleTask);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible, runtime.CurrentState.Disposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: receipt timeout 뒤 noncooperative physical write가 fault로 settle할 때 failure evidence와 shared lease 처리를 검증한다.
	// 예상 결과: late exception은 Trace diagnostic에 남고 gate는 실제 fault settlement 뒤에만 풀리며 ReceiptTimedOut은 변하지 않는다.
	// 완료 조건: subsequent P3 read가 진행되어도 retry/replay/new ID/write/revival 없이 timeout hold가 유지된다.
	[TestMethod]
	[DoNotParallelize]
	public async Task RequestStartAsync_LateFaultAfterReceiptTimeout_RecordsDiagnosticAndReleasesLeaseWithoutRevival()
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		var writeCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		ports.WriteHandler = (_, _) => writeCompletion.Task;
		var listener = new RecordingTraceListener();
		System.Diagnostics.Trace.Listeners.Add(listener);

		try
		{
			await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
			await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
			var requestTask = runtime.RequestStartAsync(CancellationToken.None);
			await ports.WriteStarted.Task;
			timeProvider.Advance(TimeSpan.FromSeconds(3));
			var timeoutResult = await requestTask;
			ports.EnqueueSnapshot(Snapshot(sequence: 2));
			var blockedCycle = runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

			Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, timeoutResult.Disposition);
			Assert.IsFalse(blockedCycle.IsCompleted);
			writeCompletion.SetException(new InvalidOperationException("late physical write failure"));
			var cycleResult = await blockedCycle.WaitAsync(TimeSpan.FromSeconds(1));
			System.Diagnostics.Trace.Flush();
			var duplicate = await runtime.RequestStartAsync(CancellationToken.None);

			StringAssert.Contains(listener.Messages, "late physical write failure");
			Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, cycleResult.CommandDisposition);
			Assert.AreEqual(EquipmentCommandLifecycleDisposition.ReceiptTimedOut, runtime.CurrentState.Disposition);
			Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, duplicate.Disposition);
			Assert.AreEqual(1, ports.WriteCount);
			Assert.AreEqual(2, ports.ReadCount);
			Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		}
		finally
		{
			System.Diagnostics.Trace.Listeners.Remove(listener);
			listener.Dispose();
		}
	}

	// 목적: Stop/Reset이 prior ACK, same-sequence exact ACK, mismatched ACK를 모두 거절하고 later fresh exact ACK만 complete하는지 검증한다.
	// 예상 결과: 세 non-authoritative observation 뒤 AwaitingAcknowledgement이고 sequence 5의 exact ID 2에서만 target event 하나가 생긴다.
	// 완료 조건: command-kind 일반화가 exact identity와 strictly-later accepted observation 규칙을 약화하지 않는다.
	[TestMethod]
	[DataRow(ControllerCommandKind.Stop)]
	[DataRow(ControllerCommandKind.Reset)]
	public async Task CommandFamily_StaleMismatchedAndNonFreshAcknowledgements_CannotComplete(
		ControllerCommandKind commandKind)
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: start.CommandId!.Value));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		PrepareCommandEligibility(controller, commandKind);
		var request = await RequestCommandAsync(runtime, commandKind);
		Assert.AreEqual(2L, request.CommandId);

		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: 1));
		var stale = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: 2));
		var nonFresh = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 4, acknowledgedCommandId: 0));
		var mismatched = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 5, acknowledgedCommandId: 2));
		var exactFresh = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, stale.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, nonFresh.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, mismatched.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, exactFresh.CommandDisposition);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == commandKind.ToString()));
	}

	// 목적: dispatch 뒤 Core eligibility가 바뀐 Stop/Reset exact ACK가 transition이나 global fence release를 만들지 않는지 검증한다.
	// 예상 결과: exact ACK는 AcknowledgedButCoreIneligible이고 target event 0, later 세 command는 모두 rejection/write count 2다.
	// 완료 조건: success-only fence release와 Core reservation revalidation이 command kind마다 유지된다.
	[TestMethod]
	[DataRow(ControllerCommandKind.Stop)]
	[DataRow(ControllerCommandKind.Reset)]
	public async Task CommandFamily_CoreIneligibleAfterDispatch_HoldsEveryCommandKind(
		ControllerCommandKind commandKind)
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, TimeProvider.System);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: start.CommandId!.Value));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		PrepareCommandEligibility(controller, commandKind);
		var request = await RequestCommandAsync(runtime, commandKind);
		Assert.AreEqual(2L, request.CommandId);

		controller.SetDoorOpen(true);
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: 2));
		var ineligible = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var laterStart = await runtime.RequestStartAsync(CancellationToken.None);
		var laterStop = await runtime.RequestStopAsync(CancellationToken.None);
		var laterReset = await runtime.RequestResetAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgedButCoreIneligible, ineligible.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterStart.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterStop.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterReset.Disposition);
		Assert.AreEqual(2, ports.WriteCount);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == commandKind.ToString()));
	}

	// 목적: Stop/Reset ACK deadline 뒤 timeout hold가 모든 command kind를 막고 delayed exact ACK로 revive되지 않는지 검증한다.
	// 예상 결과: exact 3초에 AcknowledgementTimedOut, 세 later request rejection, delayed exact ACK 뒤에도 timeout/write count 2다.
	// 완료 조건: T4 monotonic terminal hold가 generalized command family에도 동일하게 적용된다.
	[TestMethod]
	[DataRow(ControllerCommandKind.Stop)]
	[DataRow(ControllerCommandKind.Reset)]
	public async Task CommandFamily_AcknowledgementTimeout_HoldsAllKindsAndCannotRevive(
		ControllerCommandKind commandKind)
	{
		var controller = CreateController();
		var ports = new ControlledPlcPorts();
		var timeProvider = new ManualTimeProvider();
		ports.EnqueueSnapshot(Snapshot(sequence: 1));
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports, timeProvider);
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var start = await runtime.RequestStartAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 2, acknowledgedCommandId: start.CommandId!.Value));
		await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		PrepareCommandEligibility(controller, commandKind);
		var request = await RequestCommandAsync(runtime, commandKind);
		Assert.AreEqual(2L, request.CommandId);

		timeProvider.Advance(TimeSpan.FromSeconds(3));
		ports.EnqueueSnapshot(Snapshot(sequence: 3, acknowledgedCommandId: 1));
		var timedOut = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var laterStart = await runtime.RequestStartAsync(CancellationToken.None);
		var laterStop = await runtime.RequestStopAsync(CancellationToken.None);
		var laterReset = await runtime.RequestResetAsync(CancellationToken.None);
		ports.EnqueueSnapshot(Snapshot(sequence: 4, acknowledgedCommandId: 2));
		var delayedExact = await runtime.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut, timedOut.CommandDisposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterStart.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterStop.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, laterReset.Disposition);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AcknowledgementTimedOut, delayedExact.CommandDisposition);
		Assert.AreEqual(2, ports.WriteCount);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == commandKind.ToString()));
	}

	private static Task<EquipmentCommandRequestResult> RequestCommandAsync(
		EquipmentCommandRuntime runtime,
		ControllerCommandKind commandKind) => commandKind switch
	{
		ControllerCommandKind.Stop => runtime.RequestStopAsync(CancellationToken.None),
		ControllerCommandKind.Reset => runtime.RequestResetAsync(CancellationToken.None),
		_ => throw new ArgumentOutOfRangeException(nameof(commandKind))
	};

	private static void PrepareCommandEligibility(ThermalController controller, ControllerCommandKind commandKind)
	{
		if (commandKind != ControllerCommandKind.Reset)
		{
			return;
		}

		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
	}

	private static ThermalController CreateRecoveryReadyController()
	{
		var controller = CreateController();
		controller.Start();
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		return controller;
	}

	private static ThermalController CreateController() => new(new Recipe(30, 35), SimulationSettings.Illustrative);

	private static PlcInputSnapshot Snapshot(
		long sequence,
		long acknowledgedCommandId = 0,
		bool doorClosed = true) => new(
			doorClosed,
			sensorHealthy: true,
			currentTemperature: 20d,
			PlcMachineState.Idle,
			acknowledgedCommandId,
			sequence);

	private sealed class ThrowingTimestampTimeProvider : TimeProvider
	{
		private readonly Exception _exception;

		public ThrowingTimestampTimeProvider(Exception exception) => _exception = exception;

		public override long GetTimestamp() => throw _exception;
	}

	private sealed class RecordingTraceListener : System.Diagnostics.TraceListener
	{
		private readonly System.Text.StringBuilder _messages = new();

		public string Messages => _messages.ToString();

		public override void Write(string? message) => _messages.Append(message);

		public override void WriteLine(string? message) => _messages.AppendLine(message);
	}

	private sealed class ControlledPlcPorts : IPlcObservationPort, IPlcOutputPort
	{
		private readonly Queue<PlcInputSnapshot> _snapshots = new();
		private int _activeOperations;
		private bool _failNextRead;

		public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Disconnected;
		public int ReadCount { get; private set; }
		public int WriteCount { get; private set; }
		public PlcOutputCommand? LastCommand { get; private set; }
		public bool OverlapDetected { get; private set; }
		public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public Func<CancellationToken, Task<PlcInputSnapshot>>? ReadHandler { get; set; }
		public Func<PlcOutputCommand, CancellationToken, Task<PlcWriteReceipt>> WriteHandler { get; set; } =
			(command, _) => Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Written));

		public void EnqueueSnapshot(PlcInputSnapshot snapshot) => _snapshots.Enqueue(snapshot);
		public void FailNextRead() => _failNextRead = true;
		public void DisconnectForTest() => ConnectionState = PlcConnectionState.Disconnected;

		public Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Connected;
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public async Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			EnterOperation();
			try
			{
				ReadCount++;
				if (ReadHandler is not null)
				{
					return await ReadHandler(cancellationToken);
				}

				if (_failNextRead)
				{
					_failNextRead = false;
					throw new PlcTransportException("controlled transport failure");
				}

				if (_snapshots.Count == 0)
				{
					throw new InvalidOperationException("no controlled snapshot");
				}

				return _snapshots.Dequeue();
			}
			finally
			{
				ExitOperation();
			}
		}

		public async Task<PlcWriteReceipt> WriteOutputsAsync(PlcOutputCommand command, CancellationToken cancellationToken)
		{
			EnterOperation();
			try
			{
				WriteCount++;
				LastCommand = command;
				WriteStarted.TrySetResult();
				return await WriteHandler(command, cancellationToken);
			}
			finally
			{
				ExitOperation();
			}
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}

		private void EnterOperation()
		{
			if (Interlocked.Increment(ref _activeOperations) > 1)
			{
				OverlapDetected = true;
			}
		}

		private void ExitOperation() => Interlocked.Decrement(ref _activeOperations);
	}
}
