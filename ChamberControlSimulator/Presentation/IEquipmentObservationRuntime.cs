using ChamberControlSimulator.Application;

namespace ChamberControlSimulator.Presentation
{
	internal interface IEquipmentObservationRuntime : IAsyncDisposable
	{
		void SetCurrentTemperature(double currentTemperature);

		void SetSensorHealthy(bool sensorHealthy);

		void SetDoorClosed(bool doorClosed);

		Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken);
	}
}
