namespace ChamberControlSimulator.Plc.Abstractions;

public interface IPlcClient : IAsyncDisposable
{
	PlcConnectionState ConnectionState { get; }

	Task ConnectAsync(CancellationToken cancellationToken);

	Task DisconnectAsync(CancellationToken cancellationToken);

	Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken);

	Task<PlcWriteReceipt> WriteOutputsAsync(
		PlcOutputCommand command,
		CancellationToken cancellationToken);
}
