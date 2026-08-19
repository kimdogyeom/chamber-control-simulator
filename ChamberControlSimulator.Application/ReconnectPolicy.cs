namespace ChamberControlSimulator.Application;

public sealed class ReconnectPolicy
{
	private readonly TimeSpan[] _attemptDelays;

	public static ReconnectPolicy Conservative { get; } = new(
		TimeSpan.FromMilliseconds(250),
		TimeSpan.FromMilliseconds(500),
		TimeSpan.FromSeconds(1));

	public ReconnectPolicy(params TimeSpan[] attemptDelays)
	{
		ArgumentNullException.ThrowIfNull(attemptDelays);
		if (attemptDelays.Length != 3)
		{
			throw new ArgumentException("Exactly three reconnect attempt delays are required.", nameof(attemptDelays));
		}

		for (var index = 0; index < attemptDelays.Length; index++)
		{
			if (attemptDelays[index] <= TimeSpan.Zero || attemptDelays[index] > TimeSpan.FromSeconds(1))
			{
				throw new ArgumentOutOfRangeException(nameof(attemptDelays));
			}
			if (index > 0 && attemptDelays[index] < attemptDelays[index - 1])
			{
				throw new ArgumentException("Reconnect attempt delays must not decrease.", nameof(attemptDelays));
			}
		}

		if (attemptDelays[0] != TimeSpan.FromMilliseconds(250) ||
			attemptDelays[1] != TimeSpan.FromMilliseconds(500) ||
			attemptDelays[2] != TimeSpan.FromSeconds(1))
		{
			throw new ArgumentException("Reconnect delays must be 250 ms, 500 ms, and 1 second.", nameof(attemptDelays));
		}

		_attemptDelays = (TimeSpan[])attemptDelays.Clone();
	}

	public int MaximumAttemptCount => _attemptDelays.Length;

	public TimeSpan GetDelayBeforeAttempt(int attemptNumber)
	{
		if (attemptNumber < 1 || attemptNumber > MaximumAttemptCount)
		{
			throw new ArgumentOutOfRangeException(nameof(attemptNumber));
		}

		return _attemptDelays[attemptNumber - 1];
	}
}
