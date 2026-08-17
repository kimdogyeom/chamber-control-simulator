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



	public void SuppressNextAcknowledgement()
	{
		_client.SuppressNextAcknowledgement();
	}

	public void Advance(TimeSpan elapsed)
	{
		_client.Advance(elapsed);
	}

}
