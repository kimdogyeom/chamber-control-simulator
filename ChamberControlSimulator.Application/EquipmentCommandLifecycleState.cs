using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Application;

public enum EquipmentCommandLifecycleDisposition
{
	NoCommand,
	BaselineRequired,
	AdmissionRejected,
	Writing,
	AwaitingAcknowledgement,
	ReceiptTimedOut,
	AcknowledgementTimedOut,
	ReconciliationRequired,
	Completed,
	AcknowledgedButCoreIneligible
}

public enum EquipmentCommandRejectionReason
{
	None,
	AdmissionClosed,
	OutstandingCommand,
	CoreIneligible
}

public sealed record EquipmentCommandLifecycleState(
	EquipmentCommandLifecycleDisposition Disposition,
	long? CommandId,
	ControllerCommandKind? Kind,
	long? WriteInvokedTimestamp,
	long? AcknowledgementStartedTimestamp,
	bool IsAutomatic = false,
	EquipmentCommandRejectionReason RejectionReason = EquipmentCommandRejectionReason.None,
	ControllerCommandKind? RejectedKind = null);

public sealed record EquipmentCommandRequestResult(
	EquipmentCommandLifecycleDisposition Disposition,
	long? CommandId,
	EquipmentCommandRejectionReason RejectionReason = EquipmentCommandRejectionReason.None);
public sealed record EquipmentCommandCycleResult(
	EquipmentCycleResult ObservationResult,
	EquipmentCommandLifecycleDisposition CommandDisposition,
	long? CommandId);
