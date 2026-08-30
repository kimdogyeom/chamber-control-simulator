using ChamberControlSimulator.Application;

namespace ChamberControlSimulator.Presentation
{
	internal interface IEquipmentCommandRuntime
	{
		EquipmentCommandLifecycleState CurrentState { get; }

		Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken);

		Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken);

		Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken);
		Task<EquipmentCommandRequestResult> RequestAbortAsync(CancellationToken cancellationToken);

		void StopAdmission();
	}
}
