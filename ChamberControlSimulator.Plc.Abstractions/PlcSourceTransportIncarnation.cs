namespace ChamberControlSimulator.Plc.Abstractions;

public sealed record PlcSourceTransportIncarnation
{
	public PlcSourceTransportIncarnation(Guid value)
	{
		if (value == Guid.Empty)
		{
			throw new ArgumentOutOfRangeException(nameof(value));
		}

		Value = value;
	}

	public Guid Value { get; }
}
