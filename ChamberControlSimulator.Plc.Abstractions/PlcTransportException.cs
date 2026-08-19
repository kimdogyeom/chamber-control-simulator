namespace ChamberControlSimulator.Plc.Abstractions;

public sealed class PlcTransportException : Exception
{
	public PlcTransportException(string message)
		: base(message)
	{
	}
}