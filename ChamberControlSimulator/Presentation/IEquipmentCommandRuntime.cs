using ChamberControlSimulator.Application;

namespace ChamberControlSimulator.Presentation
{
	internal interface IEquipmentCommandRuntime
	{
		Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken);

		Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken);

		Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken);

		void StopAdmission();
	}
}
