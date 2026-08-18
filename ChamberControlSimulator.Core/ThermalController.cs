namespace ChamberControlSimulator.Core;

public sealed class ThermalController
{
	private Recipe _recipe;
	private readonly IReadOnlyList<Recipe> _recipes;
	private readonly SimulationSettings _settings;
	private readonly List<EventLogEntry> _events = [];
	private readonly HashSet<AlarmKind> _pendingAlarms = [];
	private ControllerState _state = ControllerState.Idle;
	private double _currentTemperature;
	private TimeSpan _elapsed;
	private TimeSpan _holdingElapsed;
	private TimeSpan _feedbackPausedElapsed;
	private bool _doorOpen;
	private bool _feedbackPaused;
	private bool _hasFreshFeedbackTick;
	private AlarmKind? _activeAlarm;
	private bool _alarmAcknowledged;
	private bool _recoveryReady;
	private ControllerCommandReservation? _commandReservation;
	private readonly IReadOnlyList<EventLogEntry> _eventHistory;

	public ThermalController(Recipe recipe, SimulationSettings settings)
		: this([recipe], settings)
	{
	}

	public ThermalController(IEnumerable<Recipe> recipes, SimulationSettings settings)
	{
		ArgumentNullException.ThrowIfNull(recipes);

		var recipeList = recipes.ToList();
		if (recipeList.Count == 0)
		{
			throw new ArgumentException("At least one recipe is required.", nameof(recipes));
		}

		if (recipeList.Any(recipe => recipe is null))
		{
			throw new ArgumentException("Recipes cannot contain null.", nameof(recipes));
		}

		if (recipeList.Select(recipe => recipe.Name).Distinct(StringComparer.Ordinal).Count() != recipeList.Count)
		{
			throw new ArgumentException("Recipe names must be unique.", nameof(recipes));
		}

		_recipes = recipeList.AsReadOnly();
		_recipe = recipeList[0];
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_currentTemperature = _settings.AmbientTemperature;
		_eventHistory = _events.AsReadOnly();
		PublishSnapshot();
	}

	public ControllerSnapshot Snapshot { get; private set; } = null!;
	public IReadOnlyList<Recipe> Recipes => _recipes;
	public IReadOnlyList<EventLogEntry> EventHistory => _eventHistory;

	public ControllerCommandReservation? TryReserveCommand(ControllerCommandKind kind)
	{
		EnsureCommandKindDefined(kind);
		if (_commandReservation is not null || !IsCommandEligible(kind))
		{
			return null;
		}

		_commandReservation = new ControllerCommandReservation(kind);
		return _commandReservation;
	}

	internal bool TryCompleteAcknowledgedCommand(ControllerCommandReservation reservation)
	{
		ArgumentNullException.ThrowIfNull(reservation);
		if (!ReferenceEquals(_commandReservation, reservation) ||
			reservation.IsInvalidated ||
			IsCommandEligible(reservation.Kind) == false)
		{
			return false;
		}

		_commandReservation = null;
		ApplyCommand(reservation.Kind);
		return true;
	}

	public void Start()
	{
		TryApplyImmediateCommand(ControllerCommandKind.Start);
	}

	public bool SelectRecipe(string recipeName)
	{
		if (_state != ControllerState.Idle || _commandReservation is not null || string.IsNullOrWhiteSpace(recipeName))
		{
			return false;
		}

		var selectedRecipe = _recipes.FirstOrDefault(recipe =>
			string.Equals(recipe.Name, recipeName, StringComparison.Ordinal));

		if (selectedRecipe is null || ReferenceEquals(_recipe, selectedRecipe))
		{
			return false;
		}

		_recipe = selectedRecipe;
		AddEvent($"Recipe selected: {_recipe.Name}");
		PublishSnapshot();
		return true;
	}

	public void ReportTemperature(double temperature)
	{
		if (double.IsNaN(temperature) || double.IsInfinity(temperature))
			throw new ArgumentOutOfRangeException(nameof(temperature));

		_currentTemperature = temperature;
		MarkReservationInvalidIfIneligible();
		if (IsSafetyMonitored() && _currentTemperature >= _recipe.SafetyTemperature)
		{
			RaiseAlarm(AlarmKind.OverTemperature);
			return;
		}

		TryMarkRecoveryReady();
	}

	public void ApplyObservation(
		ThermalObservation observation,
		TimeSpan elapsed)
	{
		ArgumentNullException.ThrowIfNull(observation);

		if (elapsed < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsed));
		}

		_elapsed += elapsed;
		_doorOpen = observation.IsDoorOpen;
		_currentTemperature = observation.CurrentTemperature;
		MarkReservationInvalidIfIneligible();

		if (!observation.SensorHealthy && !_feedbackPaused)
		{
			_feedbackPaused = true;
			_feedbackPausedElapsed = TimeSpan.Zero;
			_hasFreshFeedbackTick = false;
		}
		else if (observation.SensorHealthy && _feedbackPaused)
		{
			_feedbackPaused = false;
		}

		if (IsSafetyMonitored())
		{
			if (_doorOpen)
			{
				RaiseAlarm(AlarmKind.DoorOpen);
			}

			if (_currentTemperature >= _recipe.SafetyTemperature)
			{
				RaiseAlarm(AlarmKind.OverTemperature);
			}
		}

		if (_feedbackPaused && IsSafetyMonitored())
		{
			_feedbackPausedElapsed += elapsed;
			if (_feedbackPausedElapsed >= _settings.FeedbackTimeout)
			{
				RaiseAlarm(AlarmKind.SensorTimeout);
			}
			else
			{
				PublishSnapshot();
			}

			return;
		}

		if (!_feedbackPaused && elapsed > TimeSpan.Zero && _pendingAlarms.Contains(AlarmKind.SensorTimeout))
		{
			_hasFreshFeedbackTick = true;
		}

		TryMarkRecoveryReady();
		AdvancePhaseFromObservedTemperature(elapsed);
		PublishSnapshot();
	}

	public void Stop()
	{
		TryApplyImmediateCommand(ControllerCommandKind.Stop);
	}
	public void SetDoorOpen(bool isOpen)
	{
		_doorOpen = isOpen;
		MarkReservationInvalidIfIneligible();
		if (isOpen && IsSafetyMonitored())
		{
			RaiseAlarm(AlarmKind.DoorOpen);
			return;
		}
		TryMarkRecoveryReady();
	}

	public void PauseFeedback()
	{
		if (!_feedbackPaused)
		{
			_feedbackPaused = true;
			_feedbackPausedElapsed = TimeSpan.Zero;
			_hasFreshFeedbackTick = false;
		}
		PublishSnapshot();
	}

	public void ResumeFeedback()
	{
		_feedbackPaused = false;
		PublishSnapshot();
	}

	public void AcknowledgeAlarm()
	{
		if (_state != ControllerState.Alarm || _activeAlarm is null || _alarmAcknowledged) return;
		_alarmAcknowledged = true;
		AddEvent("Acknowledgement", _activeAlarm);
		TryMarkRecoveryReady();
	}

	public void Reset()
	{
		TryApplyImmediateCommand(ControllerCommandKind.Reset);
	}

	public void Tick(TimeSpan elapsed)
	{
		if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
		_elapsed += elapsed;

		if (_feedbackPaused && IsSafetyMonitored())
		{
			_feedbackPausedElapsed += elapsed;
			if (_feedbackPausedElapsed >= _settings.FeedbackTimeout)
				RaiseAlarm(AlarmKind.SensorTimeout);
			else
				PublishSnapshot();
			return;
		}

		if (!_feedbackPaused && elapsed > TimeSpan.Zero && _pendingAlarms.Contains(AlarmKind.SensorTimeout))
		{
			_hasFreshFeedbackTick = true;
			TryMarkRecoveryReady();
		}

		PublishSnapshot();
	}

	private bool TryApplyImmediateCommand(ControllerCommandKind kind)
	{
		EnsureCommandKindDefined(kind);
		if (_commandReservation is not null || !IsCommandEligible(kind))
		{
			return false;
		}

		ApplyCommand(kind);
		return true;
	}

	private bool IsCommandEligible(ControllerCommandKind kind) => kind switch
	{
		ControllerCommandKind.Start => _state == ControllerState.Idle && !_doorOpen && _currentTemperature < _recipe.SafetyTemperature,
		ControllerCommandKind.Stop => _state is not ControllerState.Idle and not ControllerState.Alarm and not ControllerState.Recovery,
		ControllerCommandKind.Reset => _state == ControllerState.Recovery && _recoveryReady,
		_ => false
	};

	private void ApplyCommand(ControllerCommandKind kind)
	{
		switch (kind)
		{
			case ControllerCommandKind.Start:
				AddEvent("Start");
				TransitionTo(ControllerState.Precheck);
				TransitionTo(ControllerState.Heating);
				return;
			case ControllerCommandKind.Stop:
				_state = ControllerState.Idle;
				_activeAlarm = null;
				_pendingAlarms.Clear();
				_alarmAcknowledged = false;
				_recoveryReady = false;
				_feedbackPaused = false;
				_feedbackPausedElapsed = TimeSpan.Zero;
				AddEvent("Stop");
				PublishSnapshot();
				return;
			case ControllerCommandKind.Reset:
				_pendingAlarms.Clear();
				_activeAlarm = null;
				_alarmAcknowledged = false;
				_recoveryReady = false;
				_feedbackPausedElapsed = TimeSpan.Zero;
				_state = ControllerState.Idle;
				AddEvent("Reset");
				PublishSnapshot();
				return;
			default:
				throw new ArgumentOutOfRangeException(nameof(kind));
		}
	}

	private void MarkReservationInvalidIfIneligible()
	{
		if (_commandReservation is not null && !_commandReservation.IsInvalidated && !IsCommandEligible(_commandReservation.Kind))
		{
			_commandReservation.Invalidate();
		}
	}

	private static void EnsureCommandKindDefined(ControllerCommandKind kind)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}
	}

	private bool IsActivePhase() => _state is ControllerState.Precheck or ControllerState.Heating or ControllerState.Holding or ControllerState.Cooling;

	private void RaiseAlarm(AlarmKind alarm)
	{
		var wasAlreadyAlarm = _state == ControllerState.Alarm;
		var isNewAlarm = _pendingAlarms.Add(alarm);

		if (!isNewAlarm && wasAlreadyAlarm)
		{
			return;
		}

		_activeAlarm ??= alarm;
		_alarmAcknowledged = false;
		_recoveryReady = false;
		_state = ControllerState.Alarm;
		MarkReservationInvalidIfIneligible();
		AddEvent(isNewAlarm ? $"Alarm: {alarm}" : $"Alarm reasserted: {alarm}", alarm);
		PublishSnapshot();
	}
	private void TryMarkRecoveryReady()
	{
		if (_state != ControllerState.Alarm || !_alarmAcknowledged || _pendingAlarms.Any(alarm => !IsAlarmConditionCleared(alarm)))
		{
			PublishSnapshot();
			return;
		}
		_recoveryReady = true;
		_state = ControllerState.Recovery;
		AddEvent("Recovery ready", _activeAlarm);
		PublishSnapshot();
	}

	private void AdvancePhaseFromObservedTemperature(TimeSpan elapsed)
	{
		switch (_state)
		{
			case ControllerState.Heating:
				if (_currentTemperature >= _recipe.TargetTemperature)
				{
					TransitionTo(ControllerState.Holding);
				}
				break;
			case ControllerState.Holding:
				_holdingElapsed += elapsed;
				if (_holdingElapsed >= _recipe.HoldDuration)
				{
					TransitionTo(ControllerState.Cooling);
				}
				break;
			case ControllerState.Cooling:
				if (_currentTemperature <= _settings.AmbientTemperature)
				{
					TransitionTo(ControllerState.Complete);
				}
				break;
		}
	}

	private bool IsSafetyMonitored() => IsActivePhase() || _state is ControllerState.Alarm or ControllerState.Recovery;

	private bool IsAlarmConditionCleared(AlarmKind alarm) => alarm switch
	{
		AlarmKind.DoorOpen => !_doorOpen,
		AlarmKind.OverTemperature => _currentTemperature < _recipe.SafetyTemperature,
		AlarmKind.SensorTimeout => !_feedbackPaused && _hasFreshFeedbackTick,
		_ => false
	};
	private void TransitionTo(ControllerState state)
	{
		if (state == ControllerState.Holding)
		{
			_holdingElapsed = TimeSpan.Zero;
		}

		_state = state;
		AddEvent($"Phase: {state}");
		PublishSnapshot();
	}

	private void AddEvent(string eventName, AlarmKind? alarm = null) => _events.Add(new EventLogEntry(_elapsed, _state, eventName, alarm));

	private void PublishSnapshot() => Snapshot = new ControllerSnapshot(
		_state, _recipe.Name, _currentTemperature, _recipe.TargetTemperature, _settings.AmbientTemperature, _doorOpen, _activeAlarm,
		_state == ControllerState.Idle && !_doorOpen && _currentTemperature < _recipe.SafetyTemperature,
		_state == ControllerState.Idle,
		_state == ControllerState.Alarm && _activeAlarm is not null && !_alarmAcknowledged,
		_state == ControllerState.Recovery && _recoveryReady,
		_recoveryReady, _feedbackPaused);
}
