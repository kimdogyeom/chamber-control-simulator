namespace ChamberControlSimulator.Plc.Abstractions;

public interface IPlcOutputPort
{
	Task<PlcWriteReceipt> WriteOutputsAsync(
		PlcOutputCommand command,
		CancellationToken cancellationToken);
}
