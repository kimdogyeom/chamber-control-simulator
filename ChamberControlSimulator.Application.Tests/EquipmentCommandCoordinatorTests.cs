using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
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
		var coordinator = new EquipmentCommandCoordinator(controller);

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
		var coordinator = new EquipmentCommandCoordinator(controller);
		var first = coordinator.TryAdmit(ControllerCommandKind.Start);
		Assert.IsNotNull(first.Admission);

		var second = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Busy, second.Disposition);
		Assert.IsNull(second.Admission);
		Assert.AreSame(first.Admission, coordinator.PendingCommand);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: concurrent admission requests에서도 lock이 exactly one command ID와 one pending reservation만 허용하고 Core state/event가 바뀌지 않는지 검증한다.
	// 예상 결과: 동시에 시작한 여덟 요청 중 하나만 Accepted ID 1이고 나머지는 Busy이며 Core는 Idle/event-empty다.
	// 완료 조건: async dispatch 전 T1 admission gate가 race로 duplicate command나 semantic Core transition을 만들지 않는다.
	[TestMethod]
	public async Task TryAdmit_ConcurrentStartRequests_AdmitsExactlyOneCommand()
	{
		var controller = new ThermalController(new Recipe(30, 35), SimulationSettings.Illustrative);
		var coordinator = new EquipmentCommandCoordinator(controller);
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
		var coordinator = new EquipmentCommandCoordinator(controller);

		var rejected = coordinator.TryAdmit(ControllerCommandKind.Start);
		controller.SetDoorOpen(false);
		var accepted = coordinator.TryAdmit(ControllerCommandKind.Start);

		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Ineligible, rejected.Disposition);
		Assert.IsNull(rejected.Admission);
		Assert.AreEqual(EquipmentCommandAdmissionDisposition.Accepted, accepted.Disposition);
		Assert.IsNotNull(accepted.Admission);
		Assert.AreEqual(1L, accepted.Admission.CommandId);
	}

	// 목적: T1 coordinator의 production source, outer/nested compiler-generated declared member, compiled IL이 PLC/output authority를 숨기거나 WriteOutputsAsync를 직접 호출하지 않는지 검증한다.
	// 예상 결과: source using은 Core 하나만 허용하고 모든 outer/nested declared member type 및 direct IL reference에서 PLC/output capability가 발견되지 않는다.
	// 완료 조건: P4-T2 전 private/lambda state-machine dependency나 direct write call이 public surface 검사를 우회할 수 없음을 보장한다.
	[TestMethod]
	public void CapabilityBoundary_ProductionSourceAndCompiledCoordinatorExposeNoPlcOutputAuthority()
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

		Assert.HasCount(1, constructors);
		CollectionAssert.AreEqual(
			new[] { typeof(ThermalController) },
			constructors[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
		CollectionAssert.AreEqual(new[] { "using ChamberControlSimulator.Core;" }, usingDirectives);
		Assert.IsFalse(productionSource.Contains("Plc", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("WriteOutputsAsync", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("PlcWriteReceipt", StringComparison.Ordinal));
		Assert.IsFalse(productionSource.Contains("AcknowledgedCommandId", StringComparison.Ordinal));
		Assert.IsFalse(declaredMemberTypes.Any(IsPlcType));
		Assert.IsFalse(referencedMembers.Any(member =>
			member.Name == "WriteOutputsAsync" ||
			(member is Type referencedType && IsPlcType(referencedType)) ||
			IsPlcType(member.DeclaringType)));
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
}
