using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Plc.Simulation;

public sealed class VirtualPlcObservationInputControl : IPlcObservationInputControl
{
	private readonly VirtualPlcClient _client;

	internal VirtualPlcObservationInputControl(VirtualPlcClient client)
	{
		_client = client;
	}

	public void SetCurrentTemperature(double currentTemperature)
	{
		_client.SetCurrentTemperature(currentTemperature);
	}

	public void SetSensorHealthy(bool sensorHealthy)
	{
		_client.SetSensorHealthy(sensorHealthy);
	}

	public void SetDoorClosed(bool doorClosed)
	{
		_client.SetDoorClosed(doorClosed);
	}
}
