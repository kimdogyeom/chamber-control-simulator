using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Reflection.Emit;

namespace ChamberControlSimulator.Application.Tests;

[TestClass]
public sealed class EquipmentCommandCoordinatorTests
{
	private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Where(field => field.FieldType == typeof(OpCode))
		.Select(field => (OpCode)field.GetValue(null)!)
		.ToDictionary(opCode => opCode.Value);

	// 목적: Application이 eligible Core reservation 뒤 positive command ID 하나를 만들고 PLC dispatch 없이 pending admission만 보관하는지 검증한다.
	// 예상 결과: Accepted result와 command ID 1을 반환하고 Core는 Idle/event-empty를 유지한다.
	// 완료 조건: command ID allocation이 Core transition이나 output write와 분리되어 있음을 test로 보장한다.
	[TestMethod]
	public void TryAdmit_EligibleStart_AllocatesPositiveIdWithoutChangingCoreState()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());

		var result = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Accepted, result.Disposition);
		Assert.IsNotNull(result.Admission);
		Assert.AreEqual(1L, result.Admission.CommandId);
		Assert.AreEqual(ControllerCommandKind.Start, result.Admission.Kind);
		Assert.AreSame(result.Admission, coordinator.PendingCommand);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: one pending admission 동안 sequential duplicate request에 두 번째 ID나 reservation을 주지 않는지 검증한다.
	// 예상 결과: first Start는 Accepted이고 second Start는 Busy이며 original pending command가 유지된다.
	// 완료 조건: queue/implicit preemption 없이 one outstanding command policy가 통과한다.
	[TestMethod]
	public void TryAdmit_WhenPendingCommandExists_ReturnsBusyWithoutSecondAdmission()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());
		var first = coordinator.TryAdmit(ControllerCommandKind.Start);
		Assert.IsNotNull(first.Admission);

		var second = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Busy, second.Disposition);
		Assert.IsNull(second.Admission);
		Assert.AreSame(first.Admission, coordinator.PendingCommand);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: Start가 pending인 동안 Stop이 priority/preemption으로 새 reservation이나 ID를 얻지 않는지 검증한다.
	// 예상 결과: Stop은 Busy이고 original Start ID와 pending authority가 유지된다.
	// 완료 조건: one global command fence가 command kind와 무관하게 적용된다.
	[TestMethod]
	public void TryAdmit_WhenStartPending_StopDoesNotPreemptOrAllocate()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());
		var start = coordinator.TryAdmit(ControllerCommandKind.Start);

		var stop = coordinator.TryAdmit(ControllerCommandKind.Stop);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Busy, stop.Disposition);
		Assert.IsNull(stop.Admission);
		Assert.AreSame(start.Admission, coordinator.PendingCommand);
		Assert.AreEqual(1L, start.Admission!.CommandId);
	}

	// 목적: concurrent admission requests에서도 lock이 exactly one command ID와 one pending reservation만 허용하고 Core state/event가 바뀌지 않는지 검증한다.
	// 예상 결과: 동시에 시작한 여덟 요청 중 하나만 Accepted ID 1이고 나머지는 Busy이며 Core는 Idle/event-empty다.
	// 완료 조건: async dispatch 전 T1 admission gate가 race로 duplicate command나 semantic Core transition을 만들지 않는다.
	[TestMethod]
	public async Task TryAdmit_ConcurrentStartRequests_AdmitsExactlyOneCommand()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());
		using var startGate = new ManualResetEventSlim(false);
		var tasks = Enumerable.Range(0, 8)
			.Select(_ => Task.Run(() =>
			{
				startGate.Wait();
				return coordinator.TryAdmit(ControllerCommandKind.Start);
			}))
			.ToArray();

		startGate.Set();
		var results = await Task.WhenAll(tasks);
		var accepted = results.Where(result => result.Disposition == EquipmentCommandAdmissionDisposition.Accepted).ToArray();
		var busy = results.Where(result => result.Disposition == EquipmentCommandAdmissionDisposition.Busy).ToArray();

		Assert.HasCount(1, accepted);
		var acceptedAdmission = accepted[0].Admission;
		Assert.IsNotNull(acceptedAdmission);
		Assert.AreEqual(1L, acceptedAdmission!.CommandId);
		Assert.HasCount(7, busy);
		Assert.AreSame(acceptedAdmission, coordinator.PendingCommand);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: Core가 ineligible request를 거절한 뒤에도 Application allocator의 first ID를 소비하지 않는지 검증한다.
	// 예상 결과: door-open Start는 Ineligible이고 door close 뒤 accepted Start의 ID는 여전히 1이다.
	// 완료 조건: rejected request가 hidden ID gap이나 pending reservation을 만들지 않는다.
	[TestMethod]
	public void TryAdmit_IneligibleStart_PreservesFirstCommandIdForLaterEligibleRequest()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.SetDoorOpen(true);
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());

		var rejected = coordinator.TryAdmit(ControllerCommandKind.Start);
		controller.SetDoorOpen(false);
		var accepted = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Ineligible, rejected.Disposition);
		Assert.IsNull(rejected.Admission);
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Accepted, accepted.Disposition);
		Assert.IsNotNull(accepted.Admission);
		Assert.AreEqual(1L, accepted.Admission.CommandId);
	}

	// 목적: T2 coordinator가 broad PLC client가 아니라 narrow output port만 받고 P3 observation constructor를 보존하는지 검증한다.
	// 예상 결과: command coordinator constructor는 ThermalController와 IPlcOutputPort만 받고 P3 coordinator는 ThermalController와 IPlcObservationPort만 받는다.
	// 완료 조건: output authority가 P3 observation path 또는 connection/input/virtual-control capability로 확장되지 않는다.
	[TestMethod]
	public void CapabilityBoundary_CoordinatorUsesOnlyNarrowOutputPortAndPreservesP3ObservationPort()
	{
		var coordinatorType = typeof(EquipmentCommandCoordinator);
		const BindingFlags declaredFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		var constructors = coordinatorType.GetConstructors();
		var inspectedTypes = GetTypeAndNestedTypes(coordinatorType).ToArray();
		var declaredMembers = inspectedTypes
			.SelectMany(type => GetDeclaredMembers(type, declaredFlags))
			.ToArray();
		var declaredMemberTypes = declaredMembers.SelectMany(GetMemberTypes).ToArray();
		var referencedMembers = declaredMembers.OfType<MethodBase>().SelectMany(GetReferencedMembers).ToArray();
		var productionSource = File.ReadAllText(FindRepositoryFile(Path.Combine("ChamberControlSimulator.Application", "EquipmentCommandCoordinator.cs")));
		var usingDirectives = productionSource
			.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
			.Select(line => line.Trim())
			.Where(line => line.StartsWith("using ", StringComparison.Ordinal))
			.ToArray();
		var p3Constructor = typeof(EquipmentCoordinator).GetConstructors().Single();

		Assert.HasCount(1, constructors);
		CollectionAssert.AreEqual(
			new[] { typeof(ThermalController), typeof(IPlcOutputPort) },
			constructors[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
		CollectionAssert.AreEqual(
			new[] { typeof(ThermalController), typeof(IPlcObservationPort) },
			p3Constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
		CollectionAssert.AreEqual(
			new[] { "using ChamberControlSimulator.Core;", "using ChamberControlSimulator.Plc.Abstractions;" },
			usingDirectives);
		Assert.IsTrue(productionSource.Contains("IPlcOutputPort", StringComparison.Ordinal));
		Assert.IsTrue(productionSource.Contains("WriteOutputsAsync", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("IPlcClient", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("IPlcObservationPort", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("PlcInputSnapshot", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("AcknowledgedCommandId", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("ConnectAsync", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("DisconnectAsync", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("DisposeAsync", StringComparison.Ordinal));
		Assert.IsTrue(declaredMemberTypes.Where(IsPlcType).All(IsAllowedOutputPlcType));
		Assert.IsTrue(referencedMembers.Where(IsPlcMember).All(IsAllowedOutputPlcMember));
	}

	// 목적: admitted Start가 exact ID와 Start kind를 가진 output command 하나로 mapping되고 Written receipt가 ACK 대기만 뜻하는지 검증한다.
	// 예상 결과: output port write는 한 번이고 result는 AwaitingAcknowledgement이며 Core state/event와 pending fence는 그대로다.
	// 완료 조건: transport Written이 semantic completion이나 Core transition으로 오인되지 않는다.
	[TestMethod]
	public async Task DispatchPendingAsync_MatchingWrittenStart_WritesOnceAndAwaitsAcknowledgement()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var outputPort = new ControlledOutputPort();
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);
		using var cancellation = new CancellationTokenSource();

		var result = await coordinator.DispatchPendingAsync(cancellation.Token);

		Assert.AreEqual(EquipmentCommandTransportDisposition.AwaitingAcknowledgement, result.Disposition);
		Assert.AreEqual(admission.CommandId, result.CommandId);
		Assert.AreEqual(1, outputPort.WriteCount);
		Assert.IsNotNull(outputPort.LastCommand);
		Assert.AreEqual(admission.CommandId, outputPort.LastCommand.CommandId);
		Assert.AreEqual(PlcCommandKind.Start, outputPort.LastCommand.Kind);
		Assert.AreEqual(cancellation.Token, outputPort.LastCancellationToken);
		Assert.AreSame(admission, coordinator.PendingCommand);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Busy, coordinator.TryAdmit(ControllerCommandKind.Stop).Disposition);
	}

	// 목적: exact acknowledged Stop completion이 성공한 뒤에만 coordinator fence가 다음 eligible command에 열리는지 검증한다.
	// 예상 결과: Written은 Heating을 유지하고 completion 뒤 Idle/one Stop이며 next Start는 ID 2로 accepted다.
	// 완료 조건: successful semantic completion만 global duplicate fence를 release하고 receipt는 release하지 않는다.
	[TestMethod]
	public async Task TryCompleteAcknowledgedCommand_StopSuccess_ReleasesFenceForNextCommand()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());
		var stop = coordinator.TryAdmit(ControllerCommandKind.Stop).Admission;
		Assert.IsNotNull(stop);
		await coordinator.DispatchPendingAsync(CancellationToken.None);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);

		var completed = TryCompleteAcknowledgedCommand(coordinator, stop.CommandId);
		var duplicate = TryCompleteAcknowledgedCommand(coordinator, stop.CommandId);
		var next = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.IsTrue(completed);
		Assert.IsFalse(duplicate);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Stop"));
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Accepted, next.Disposition);
		Assert.AreEqual(2L, next.Admission!.CommandId);
	}

	// 목적: exact acknowledged Reset completion만 Recovery state와 coordinator fence를 소비하는지 검증한다.
	// 예상 결과: Written 뒤 Recovery가 유지되고 completion 뒤 Idle/one Reset이며 next Start는 ID 2다.
	// 완료 조건: Reset receipt가 Core recovery나 duplicate fence release shortcut이 아니다.
	[TestMethod]
	public async Task TryCompleteAcknowledgedCommand_ResetSuccess_ReleasesFenceOnlyAfterCoreRevalidation()
	{
		var controller = CreateRecoveryReadyController();
		var coordinator = new EquipmentCommandCoordinator(controller, new ControlledOutputPort());
		var reset = coordinator.TryAdmit(ControllerCommandKind.Reset).Admission;
		Assert.IsNotNull(reset);
		await coordinator.DispatchPendingAsync(CancellationToken.None);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);

		var completed = TryCompleteAcknowledgedCommand(coordinator, reset.CommandId);
		var next = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.IsTrue(completed);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.HasCount(1, controller.EventHistory.Where(entry => entry.Event == "Reset"));
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Accepted, next.Disposition);
		Assert.AreEqual(2L, next.Admission!.CommandId);
	}

	// 목적: matching current Failed receipt가 delivery uncertainty를 해소하거나 retry 가능한 상태로 바뀌지 않는지 검증한다.
	// 예상 결과: DeliveryIndeterminate를 반환하고 original pending/fence와 Core state를 유지하며 repeated dispatch는 거절된다.
	// 완료 조건: Failed transport receipt 뒤 implicit release/retry/replay가 없다.
	[TestMethod]
	public async Task DispatchPendingAsync_MatchingFailedReceipt_HoldsDeliveryIndeterminateWithoutRetry()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var outputPort = new ControlledOutputPort
		{
			Handler = (command, _) => Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Failed))
		};
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);

		var result = await coordinator.DispatchPendingAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandTransportDisposition.DeliveryIndeterminate, result.Disposition);
		Assert.AreEqual(admission.CommandId, result.CommandId);
		Assert.AreSame(admission, coordinator.PendingCommand);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.DispatchPendingAsync(CancellationToken.None));
		Assert.AreEqual(1, outputPort.WriteCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: Written receipt라도 command ID가 pending과 다르면 semantic success나 ACK 대기로 분류하지 않는지 검증한다.
	// 예상 결과: mismatched receipt는 DeliveryIndeterminate이고 pending fence와 Core Idle/event-empty가 유지된다.
	// 완료 조건: exact receipt identity 없이 command lifecycle이 진행되지 않는다.
	[TestMethod]
	public async Task DispatchPendingAsync_MismatchedWrittenReceipt_HoldsDeliveryIndeterminate()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var outputPort = new ControlledOutputPort
		{
			Handler = (command, _) => Task.FromResult(new PlcWriteReceipt(command.CommandId + 1, PlcTransportWriteStatus.Written))
		};
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);

		var result = await coordinator.DispatchPendingAsync(CancellationToken.None);

		Assert.AreEqual(EquipmentCommandTransportDisposition.DeliveryIndeterminate, result.Disposition);
		Assert.AreSame(admission, coordinator.PendingCommand);
		Assert.AreEqual(1, outputPort.WriteCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: first write가 in-flight인 동안 concurrent dispatch가 같은 pending command를 두 번 쓰지 않는지 검증한다.
	// 예상 결과: first invocation만 port에 도달하고 second는 즉시 거절되며 first Written 뒤에도 write count는 1이다.
	// 완료 조건: coordinator gate가 await 전에 dispatch-started를 claim하고 synchronous lock을 await 동안 보유하지 않는다.
	[TestMethod]
	public async Task DispatchPendingAsync_ConcurrentAttempt_WritesAtMostOnce()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var receiptCompletion = new TaskCompletionSource<PlcWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
		var outputPort = new ControlledOutputPort
		{
			Handler = (_, _) => receiptCompletion.Task
		};
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);

		var firstDispatch = coordinator.DispatchPendingAsync(CancellationToken.None);
		await outputPort.InvocationStarted.Task;
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.DispatchPendingAsync(CancellationToken.None));
		receiptCompletion.SetResult(new PlcWriteReceipt(admission.CommandId, PlcTransportWriteStatus.Written));
		var result = await firstDispatch;

		Assert.AreEqual(EquipmentCommandTransportDisposition.AwaitingAcknowledgement, result.Disposition);
		Assert.AreEqual(1, outputPort.WriteCount);
		Assert.AreSame(admission, coordinator.PendingCommand);
	}

	// 목적: output write exception이 pending reservation을 release하거나 같은 command 재전송을 허용하지 않는지 검증한다.
	// 예상 결과: original exception이 전달되고 pending/fence가 남으며 second dispatch와 new admission은 거절된다.
	// 완료 조건: exception ambiguity가 자동 retry/replay 또는 ID replacement로 이어지지 않는다.
	[TestMethod]
	public async Task DispatchPendingAsync_WriteThrows_PreservesFenceAndBlocksRedispatch()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var expected = new IOException("write outcome unknown");
		var outputPort = new ControlledOutputPort
		{
			Handler = (_, _) => Task.FromException<PlcWriteReceipt>(expected)
		};
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);

		var actual = await Assert.ThrowsExactlyAsync<IOException>(() => coordinator.DispatchPendingAsync(CancellationToken.None));

		Assert.AreSame(expected, actual);
		Assert.AreSame(admission, coordinator.PendingCommand);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.DispatchPendingAsync(CancellationToken.None));
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Busy, coordinator.TryAdmit(ControllerCommandKind.Start).Disposition);
		Assert.AreEqual(1, outputPort.WriteCount);
	}

	// 목적: post-reservation canceled write가 pending reservation을 release하거나 재전송 가능 상태로 되돌리지 않는지 검증한다.
	// 예상 결과: cancellation이 전달되고 pending/fence가 남으며 repeated dispatch는 거절된다.
	// 완료 조건: T4 taxonomy 전에도 cancellation ambiguity가 fail-closed one-command fence를 보존한다.
	[TestMethod]
	public async Task DispatchPendingAsync_WriteCanceled_PreservesFenceAndBlocksRedispatch()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var outputPort = new ControlledOutputPort
		{
			Handler = (_, token) => Task.FromCanceled<PlcWriteReceipt>(token)
		};
		var coordinator = new EquipmentCommandCoordinator(controller, outputPort);
		var admission = coordinator.TryAdmit(ControllerCommandKind.Start).Admission;
		Assert.IsNotNull(admission);

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => coordinator.DispatchPendingAsync(cancellation.Token));

		Assert.AreSame(admission, coordinator.PendingCommand);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.DispatchPendingAsync(CancellationToken.None));
		Assert.AreEqual(1, outputPort.WriteCount);
	}

	private static IEnumerable<Type> GetTypeAndNestedTypes(Type type)
	{
		yield return type;
		foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		{
			foreach (var inspectedNestedType in GetTypeAndNestedTypes(nestedType))
			{
				yield return inspectedNestedType;
			}
		}
	}

	private static IEnumerable<MemberInfo> GetDeclaredMembers(Type type, BindingFlags flags) => type
		.GetMembers(flags)
		.Concat(type.GetConstructors(flags))
		.Distinct();

	private static IEnumerable<Type> GetMemberTypes(MemberInfo member)
	{
		switch (member)
		{
			case FieldInfo field:
				yield return field.FieldType;
				yield break;
			case PropertyInfo property:
				yield return property.PropertyType;
				foreach (var parameter in property.GetIndexParameters())
				{
					yield return parameter.ParameterType;
				}
				yield break;
			case EventInfo eventInfo when eventInfo.EventHandlerType is not null:
				yield return eventInfo.EventHandlerType;
				yield break;
			case MethodInfo method:
				yield return method.ReturnType;
				foreach (var parameter in method.GetParameters())
				{
					yield return parameter.ParameterType;
				}
				yield break;
			case ConstructorInfo constructor:
				foreach (var parameter in constructor.GetParameters())
				{
					yield return parameter.ParameterType;
				}
				yield break;
			case Type nestedType:
				yield return nestedType;
				yield break;
			default:
				yield break;
		}
	}

	private static bool IsPlcMember(MemberInfo member) =>
		(member is Type type && IsPlcType(type)) || IsPlcType(member.DeclaringType);

	private static bool IsAllowedOutputPlcMember(MemberInfo member)
	{
		if (member is Type type)
		{
			return IsAllowedOutputPlcType(type);
		}

		return IsAllowedOutputPlcType(member.DeclaringType) &&
			member.Name is not "ReadInputsAsync" and not "ConnectAsync" and not "DisconnectAsync" and not "DisposeAsync";
	}

	private static bool IsAllowedOutputPlcType(Type? type)
	{
		if (type is null)
		{
			return false;
		}

		if (type.HasElementType)
		{
			return IsAllowedOutputPlcType(type.GetElementType());
		}

		if (type.IsGenericType)
		{
			return type.GetGenericArguments().Where(IsPlcType).All(IsAllowedOutputPlcType);
		}

		return type == typeof(IPlcOutputPort) ||
			type == typeof(PlcOutputCommand) ||
			type == typeof(PlcCommandKind) ||
			type == typeof(PlcWriteReceipt) ||
			type == typeof(PlcTransportWriteStatus);
	}

	private static bool IsPlcType(Type? type)
	{
		if (type is null)
		{
			return false;
		}

		if (type.Namespace?.StartsWith("ChamberControlSimulator.Plc", StringComparison.Ordinal) == true)
		{
			return true;
		}

		if (type.HasElementType)
		{
			return IsPlcType(type.GetElementType());
		}

		return type.IsGenericType && type.GetGenericArguments().Any(IsPlcType);
	}

	private static IEnumerable<MemberInfo> GetReferencedMembers(MethodBase method)
	{
		var il = method.GetMethodBody()?.GetILAsByteArray();
		if (il is null)
		{
			yield break;
		}

		var declaringTypeArguments = method.DeclaringType?.GetGenericArguments();
		var methodArguments = method is MethodInfo methodInfo && methodInfo.IsGenericMethod ? methodInfo.GetGenericArguments() : null;
		var offset = 0;
		while (offset < il.Length)
		{
			var opCode = ReadOpCode(il, ref offset);
			var operandOffset = offset;
			var operandLength = GetOperandLength(opCode.OperandType, il, operandOffset);
			if (operandOffset + operandLength > il.Length)
			{
				throw new InvalidOperationException("Invalid IL operand length.");
			}

			if (IsMetadataMemberOperand(opCode.OperandType))
			{
				var token = BitConverter.ToInt32(il, operandOffset);
				MemberInfo? referencedMember = null;
				try
				{
					referencedMember = method.Module.ResolveMember(token, declaringTypeArguments, methodArguments);
				}
				catch (ArgumentException)
				{
				}
				catch (BadImageFormatException)
				{
				}

				if (referencedMember is not null)
				{
					yield return referencedMember;
				}
			}

			offset += operandLength;
		}
	}

	private static OpCode ReadOpCode(byte[] il, ref int offset)
	{
		if (offset >= il.Length)
		{
			throw new InvalidOperationException("Unexpected end of IL stream.");
		}

		short value = il[offset++];
		if (value == 0xfe)
		{
			if (offset >= il.Length)
			{
				throw new InvalidOperationException("Unexpected end of two-byte IL opcode.");
			}

			value = (short)(0xfe00 | il[offset++]);
		}

		if (!OpCodesByValue.TryGetValue(value, out var opCode))
		{
			throw new InvalidOperationException($"Unknown IL opcode: 0x{(ushort)value:x4}.");
		}

		return opCode;
	}

	private static int GetOperandLength(OperandType operandType, byte[] il, int operandOffset)
	{
		return operandType switch
		{
			OperandType.InlineNone => 0,
			OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
			OperandType.InlineVar => 2,
			OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
			OperandType.InlineI8 or OperandType.InlineR => 8,
			OperandType.InlineSwitch => GetInlineSwitchOperandLength(il, operandOffset),
			_ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}.")
		};
	}

	private static int GetInlineSwitchOperandLength(byte[] il, int operandOffset)
	{
		if (operandOffset + sizeof(int) > il.Length)
		{
			throw new InvalidOperationException("Invalid inline switch operand.");
		}

		return checked(sizeof(int) + BitConverter.ToInt32(il, operandOffset) * sizeof(int));
	}

	private static bool IsMetadataMemberOperand(OperandType operandType) => operandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType;

	private sealed class ControlledOutputPort : IPlcOutputPort
	{
		private readonly object _gate = new();
		private int _writeCount;
		private PlcOutputCommand? _lastCommand;
		private CancellationToken _lastCancellationToken;

		public Func<PlcOutputCommand, CancellationToken, Task<PlcWriteReceipt>> Handler { get; init; } =
			(command, _) => Task.FromResult(new PlcWriteReceipt(command.CommandId, PlcTransportWriteStatus.Written));

		public TaskCompletionSource InvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int WriteCount
		{
			get
			{
				lock (_gate)
				{
					return _writeCount;
				}
			}
		}

		public PlcOutputCommand? LastCommand
		{
			get
			{
				lock (_gate)
				{
					return _lastCommand;
				}
			}
		}

		public CancellationToken LastCancellationToken
		{
			get
			{
				lock (_gate)
				{
					return _lastCancellationToken;
				}
			}
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(PlcOutputCommand command, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_writeCount++;
				_lastCommand = command;
				_lastCancellationToken = cancellationToken;
			}

			InvocationStarted.TrySetResult();
			return Handler(command, cancellationToken);
		}
	}

	private static string FindRepositoryFile(string relativePath)
	{
		for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var candidate = Path.Combine(directory.FullName, relativePath);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new FileNotFoundException("Repository source file was not found from the test output directory.", relativePath);
	}
	private static bool TryCompleteAcknowledgedCommand(EquipmentCommandCoordinator coordinator, long commandId)
	{
		var method = typeof(EquipmentCommandCoordinator).GetMethod(
			"TryCompleteAcknowledgedCommand",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.IsNotNull(method);
		return (bool)method.Invoke(coordinator, [commandId])!;
	}

	private static ThermalController CreateRecoveryReadyController()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		controller.Start();
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		return controller;
	}

}
