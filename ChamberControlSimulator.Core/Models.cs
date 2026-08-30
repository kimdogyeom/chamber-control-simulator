namespace ChamberControlSimulator.Core;

public enum ControllerState
{
	Idle,
	Precheck,
	Heating,
	Holding,
	Cooling,
	Complete,
	Alarm,
	Recovery
}

public enum ControllerCommandKind
{
	Start,
	Stop,
	Reset,
	Abort
}

public sealed class ControllerCommandReservation
{
	internal ControllerCommandReservation(ControllerCommandKind kind)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		Kind = kind;
	}

	internal ControllerCommandKind Kind { get; }

	internal bool IsInvalidated { get; private set; }

	internal void Invalidate() => IsInvalidated = true;
}

public enum AlarmKind
{
	DoorOpen,
	OverTemperature,
	SensorTimeout,
	CommunicationLost
}

public sealed record Recipe
{
	public Recipe(string name, double targetTemperature, double safetyTemperature)
		: this(name, targetTemperature, safetyTemperature, TimeSpan.FromSeconds(3))
	{
	}

	public Recipe(string name, double targetTemperature, double safetyTemperature, TimeSpan holdDuration)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Recipe name is required.", nameof(name));
		}

		if (!double.IsFinite(targetTemperature) || targetTemperature <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(targetTemperature));
		}

		if (!double.IsFinite(safetyTemperature) || safetyTemperature <= targetTemperature)
		{
			throw new ArgumentOutOfRangeException(nameof(safetyTemperature));
		}

		if (holdDuration <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(holdDuration));
		}

		Name = name;
		TargetTemperature = targetTemperature;
		SafetyTemperature = safetyTemperature;
		HoldDuration = holdDuration;
	}

	public Recipe(double targetTemperature, double safetyTemperature)
		: this($"Target {targetTemperature:F0}C", targetTemperature, safetyTemperature)
	{
	}

	public string Name { get; }

	public double TargetTemperature { get; }

	public double SafetyTemperature { get; }

	public TimeSpan HoldDuration { get; }

	public override string ToString() => Name;
}

public sealed record SimulationSettings
{
	public SimulationSettings(double ambientTemperature, TimeSpan feedbackTimeout)
	{
		if (!double.IsFinite(ambientTemperature))
		{
			throw new ArgumentOutOfRangeException(nameof(ambientTemperature));
		}

		if (feedbackTimeout <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(feedbackTimeout));
		}

		AmbientTemperature = ambientTemperature;
		FeedbackTimeout = feedbackTimeout;
	}

	public double AmbientTemperature { get; }

	public TimeSpan FeedbackTimeout { get; }

	public static SimulationSettings Illustrative { get; } = new(20, TimeSpan.FromSeconds(3));
}

public sealed record ThermalObservation
{
	public ThermalObservation(
		bool isDoorOpen,
		bool sensorHealthy,
		double currentTemperature)
	{
		if (!double.IsFinite(currentTemperature))
		{
			throw new ArgumentOutOfRangeException(nameof(currentTemperature));
		}

		IsDoorOpen = isDoorOpen;
		SensorHealthy = sensorHealthy;
		CurrentTemperature = currentTemperature;
	}

	public bool IsDoorOpen { get; }

	public bool SensorHealthy { get; }

	public double CurrentTemperature { get; }
}

public sealed record ControllerSnapshot(
	ControllerState State,
	string RecipeName,
	double CurrentTemperature,
	double TargetTemperature,
	double AmbientTemperature,
	bool IsDoorOpen,
	AlarmKind? ActiveAlarm,
	bool CanStart,
	bool CanSelectRecipe,
	bool CanAcknowledge,
	bool CanReset,
	bool IsRecoveryReady,
	bool IsFeedbackPaused);

public sealed record EventLogEntry(
	TimeSpan Elapsed,
	ControllerState State,
	string Event,
	AlarmKind? Alarm);
