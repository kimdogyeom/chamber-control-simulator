namespace ChamberControlSimulator.Plc.Simulation;

public sealed record VirtualPlcOptions
{
	public VirtualPlcOptions(
		double initialTemperature,
		double heatingRatePerSecond = 5d,
		TimeSpan acknowledgementDelay = default,
		double overTemperatureLimit = 500d)
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

		if (!double.IsFinite(overTemperatureLimit) || overTemperatureLimit <= 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(overTemperatureLimit));
		}

		InitialTemperature = initialTemperature;
		HeatingRatePerSecond = heatingRatePerSecond;
		AcknowledgementDelay = acknowledgementDelay;
		OverTemperatureLimit = overTemperatureLimit;
	}

	public double InitialTemperature { get; }

	public double HeatingRatePerSecond { get; }

	public TimeSpan AcknowledgementDelay { get; }

	public double OverTemperatureLimit { get; }

	public static VirtualPlcOptions Illustrative { get; } = new(20d);
}
