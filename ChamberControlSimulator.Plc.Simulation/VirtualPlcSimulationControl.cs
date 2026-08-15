namespace ChamberControlSimulator.Plc.Simulation;

public sealed class VirtualPlcSimulationControl
{
	private readonly VirtualPlcClient _client;

	internal VirtualPlcSimulationControl(VirtualPlcClient client)
	{
		_client = client;
	}

	public void ForceTransportDisconnect()
	{
		_client.ForceTransportDisconnect();
	}

	public void SetCurrentTemperature(double currentTemperature)
	{
		_client.SetCurrentTemperature(currentTemperature);
	}

	public void SetSensorHealthy(bool sensorHealthy)
	{
		_client.SetSensorHealthy(sensorHealthy);
	}

	public void SuppressNextAcknowledgement()
	{
		_client.SuppressNextAcknowledgement();
	}

	public void Advance(TimeSpan elapsed)
	{
		_client.Advance(elapsed);
	}

	public void SetDoorClosed(bool doorClosed)
	{
		_client.SetDoorClosed(doorClosed);
	}
}
