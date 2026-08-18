using ChamberControlSimulator.Application;

namespace ChamberControlSimulator.Presentation
{
	internal interface IEquipmentCommandRuntime
	{
		Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken);

		void StopAdmission();
	}
}
