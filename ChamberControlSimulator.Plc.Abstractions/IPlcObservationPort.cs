namespace ChamberControlSimulator.Plc.Abstractions;

public interface IPlcObservationPort : IAsyncDisposable
{
	PlcConnectionState ConnectionState { get; }

	Task ConnectAsync(CancellationToken cancellationToken);

	Task DisconnectAsync(CancellationToken cancellationToken);

	Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken);
}
