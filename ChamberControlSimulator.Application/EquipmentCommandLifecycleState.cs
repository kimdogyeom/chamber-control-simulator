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

public sealed record EquipmentCommandLifecycleState(
	EquipmentCommandLifecycleDisposition Disposition,
	long? CommandId,
	ControllerCommandKind? Kind,
	long? WriteInvokedTimestamp,
	long? AcknowledgementStartedTimestamp);

public sealed record EquipmentCommandRequestResult(
	EquipmentCommandLifecycleDisposition Disposition,
	long? CommandId);

public sealed record EquipmentCommandCycleResult(
	EquipmentCycleResult ObservationResult,
	EquipmentCommandLifecycleDisposition CommandDisposition,
	long? CommandId);
