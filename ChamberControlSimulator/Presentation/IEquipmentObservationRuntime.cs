namespace ChamberControlSimulator.Presentation
{
	internal interface IEquipmentObservationRuntime : IAsyncDisposable
	{
		void SetCurrentTemperature(double currentTemperature);

		void SetSensorHealthy(bool sensorHealthy);

		void SetDoorClosed(bool doorClosed);

		Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken);
	}
}
