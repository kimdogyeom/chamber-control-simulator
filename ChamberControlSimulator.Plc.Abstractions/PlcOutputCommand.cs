namespace ChamberControlSimulator.Plc.Abstractions;

public enum PlcCommandKind
{
	Start,
	Stop,
	Reset,
	Abort
}

public sealed record PlcOutputCommand
{
	public PlcOutputCommand(long commandId, PlcCommandKind kind)
	{
		if (commandId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(commandId));
		}

		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		CommandId = commandId;
		Kind = kind;
	}

	public long CommandId { get; }

	public PlcCommandKind Kind { get; }
}
