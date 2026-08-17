namespace ChamberControlSimulator.Plc.Abstractions;

public interface IPlcObservationInputControl
{
	void SetCurrentTemperature(double currentTemperature);

	void SetSensorHealthy(bool sensorHealthy);

	void SetDoorClosed(bool doorClosed);
}
