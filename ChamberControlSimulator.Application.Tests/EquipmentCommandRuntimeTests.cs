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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);

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
		await using var staleRuntime = new EquipmentCommandRuntime(CreateController(), stalePorts, stalePorts);
		Assert.AreEqual(EquipmentCycleDisposition.Completed, (await staleRuntime.CycleAsync(TimeSpan.Zero, CancellationToken.None)).ObservationResult.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, (await staleRuntime.CycleAsync(TimeSpan.Zero, CancellationToken.None)).ObservationResult.Disposition);

		var staleRequest = await staleRuntime.RequestStartAsync(CancellationToken.None);

		var failedPorts = new ControlledPlcPorts();
		failedPorts.FailNextRead();
		await using var failedRuntime = new EquipmentCommandRuntime(CreateController(), failedPorts, failedPorts);
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
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(CreateController(), ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);
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
		await using var runtime = new EquipmentCommandRuntime(controller, ports, ports);
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

	// 목적: T3 composition이 P3 observation constructor를 보존하고 public command request를 Start-only narrow ports로 제한하는지 검증한다.
	// 예상 결과: P3 constructor는 observation-only, runtime constructor는 separate observation/output ports이고 generic command-kind request가 없다.
	// 완료 조건: UI, broad IPlcClient, Stop/Reset completion, input/output capability widening 없이 non-UI Start tracer만 노출된다.
	[TestMethod]
	public void CapabilityBoundary_PreservesP3AndExposesStartOnlyRuntime()
	{
		var p3Parameters = typeof(EquipmentCoordinator).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
		var runtimeParameters = typeof(EquipmentCommandRuntime).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
		var publicMethods = typeof(EquipmentCommandRuntime).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

		CollectionAssert.AreEqual(new[] { typeof(ThermalController), typeof(IPlcObservationPort) }, p3Parameters);
		CollectionAssert.AreEqual(new[] { typeof(ThermalController), typeof(IPlcObservationPort), typeof(IPlcOutputPort) }, runtimeParameters);
		Assert.IsNotNull(publicMethods.SingleOrDefault(method => method.Name == "RequestStartAsync"));
		Assert.IsFalse(publicMethods.Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ControllerCommandKind))));
		Assert.IsFalse(typeof(EquipmentCommandRuntime).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Any(field => field.FieldType == typeof(IPlcClient)));
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
		public Func<PlcOutputCommand, CancellationToken, Task<PlcWriteReceipt>> WriteHandler { get; set; } =
			(command, _) => Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Written));

		public void EnqueueSnapshot(PlcInputSnapshot snapshot) => _snapshots.Enqueue(snapshot);
		public void FailNextRead() => _failNextRead = true;

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

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			EnterOperation();
			try
			{
				ReadCount++;
				if (_failNextRead)
				{
					_failNextRead = false;
					throw new InvalidOperationException("controlled transport failure");
				}

				if (_snapshots.Count == 0)
				{
					throw new InvalidOperationException("no controlled snapshot");
				}

				return Task.FromResult(_snapshots.Dequeue());
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
