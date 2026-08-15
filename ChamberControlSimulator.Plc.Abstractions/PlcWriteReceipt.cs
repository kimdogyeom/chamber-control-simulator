namespace ChamberControlSimulator.Plc.Abstractions;

public enum PlcTransportWriteStatus
{
	Written,
	Failed
}

public sealed record PlcWriteReceipt
{
	public PlcWriteReceipt(long commandId, PlcTransportWriteStatus transportStatus)
	{
		if (commandId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(commandId));
		}

		if (!Enum.IsDefined(transportStatus))
		{
			throw new ArgumentOutOfRangeException(nameof(transportStatus));
		}

		CommandId = commandId;
		TransportStatus = transportStatus;
	}

	public long CommandId { get; }

	public PlcTransportWriteStatus TransportStatus { get; }
}
