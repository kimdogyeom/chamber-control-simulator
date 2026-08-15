namespace ChamberControlSimulator.Plc.Simulation;

public sealed record VirtualPlcOptions
{
	public VirtualPlcOptions(
		double initialTemperature,
		double heatingRatePerSecond = 5d,
		TimeSpan acknowledgementDelay = default)
	{
		if (!double.IsFinite(initialTemperature))
		{
			throw new ArgumentOutOfRangeException(nameof(initialTemperature));
		}

		if (!double.IsFinite(heatingRatePerSecond) || heatingRatePerSecond < 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(heatingRatePerSecond));
		}

		if (acknowledgementDelay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(acknowledgementDelay));
		}

		InitialTemperature = initialTemperature;
		HeatingRatePerSecond = heatingRatePerSecond;
		AcknowledgementDelay = acknowledgementDelay;
	}

	public double InitialTemperature { get; }

	public double HeatingRatePerSecond { get; }

	public TimeSpan AcknowledgementDelay { get; }

	public static VirtualPlcOptions Illustrative { get; } = new(20d);
}
