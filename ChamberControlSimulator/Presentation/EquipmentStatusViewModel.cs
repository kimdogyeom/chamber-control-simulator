using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Presentation
{
	public sealed record EquipmentStatusViewModel(
		PlcConnectionState ConnectionState,
		ConnectionSynchronizationState SynchronizationState,
		EquipmentCommandLifecycleDisposition CommandDisposition,
		long? CommandId,
		ControllerCommandKind? CommandKind,
		bool IsAutomatic = false,
		EquipmentCommandRejectionReason RejectionReason = EquipmentCommandRejectionReason.None,
		ControllerCommandKind? RejectedKind = null);
}
