namespace ChamberControlSimulator.Plc.Abstractions;

public interface IPlcClient : IPlcObservationPort
{
	new PlcConnectionState ConnectionState { get; }

	new Task ConnectAsync(CancellationToken cancellationToken);

	new Task DisconnectAsync(CancellationToken cancellationToken);

	new Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken);

	Task<PlcWriteReceipt> WriteOutputsAsync(
		PlcOutputCommand command,
		CancellationToken cancellationToken);
}
