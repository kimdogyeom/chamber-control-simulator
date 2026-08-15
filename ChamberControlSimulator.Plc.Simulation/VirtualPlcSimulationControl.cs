namespace ChamberControlSimulator.Plc.Simulation;

public sealed class VirtualPlcSimulationControl
{
	private readonly VirtualPlcClient _client;

	internal VirtualPlcSimulationControl(VirtualPlcClient client)
	{
		_client = client;
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
