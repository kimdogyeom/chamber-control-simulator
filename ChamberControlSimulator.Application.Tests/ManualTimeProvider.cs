namespace ChamberControlSimulator.Application.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
	private readonly object _gate = new();
	private readonly List<ManualTimer> _timers = new();
	private long _timestamp;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override long GetTimestamp()
	{
		lock (_gate)
		{
			return _timestamp;
		}
	}

	public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());

	public override ITimer CreateTimer(
		TimerCallback callback,
		object? state,
		TimeSpan dueTime,
		TimeSpan period)
	{
		ArgumentNullException.ThrowIfNull(callback);
		var timer = new ManualTimer(this, callback, state);
		lock (_gate)
		{
			_timers.Add(timer);
			timer.ChangeUnderLock(dueTime, period);
		}

		return timer;
	}

	public void Advance(TimeSpan elapsed)
	{
		if (elapsed < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsed));
		}

		List<(TimerCallback Callback, object? State)> callbacks = new();
		lock (_gate)
		{
			_timestamp = checked(_timestamp + elapsed.Ticks);
			foreach (var timer in _timers.ToArray())
			{
				timer.CollectDueCallbacksUnderLock(_timestamp, callbacks);
			}
		}

		foreach (var callback in callbacks)
		{
			callback.Callback(callback.State);
		}
	}

	private sealed class ManualTimer : ITimer
	{
		private readonly ManualTimeProvider _owner;
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private long? _nextTimestamp;
		private long? _periodTicks;
		private bool _disposed;

		public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
		{
			_owner = owner;
			_callback = callback;
			_state = state;
		}

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (_owner._gate)
			{
				if (_disposed)
				{
					return false;
				}

				ChangeUnderLock(dueTime, period);
				return true;
			}
		}

		public void Dispose()
		{
			lock (_owner._gate)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_nextTimestamp = null;
				_owner._timers.Remove(this);
			}
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		public void ChangeUnderLock(TimeSpan dueTime, TimeSpan period)
		{
			ValidateTimeout(dueTime, nameof(dueTime));
			ValidateTimeout(period, nameof(period));
			_periodTicks = period == Timeout.InfiniteTimeSpan ? null : period.Ticks;
			_nextTimestamp = dueTime == Timeout.InfiniteTimeSpan
				? null
				: checked(_owner._timestamp + dueTime.Ticks);
		}

		public void CollectDueCallbacksUnderLock(
			long timestamp,
			List<(TimerCallback Callback, object? State)> callbacks)
		{
			while (!_disposed && _nextTimestamp is not null && _nextTimestamp.Value <= timestamp)
			{
				callbacks.Add((_callback, _state));
				if (_periodTicks is null || _periodTicks.Value == 0)
				{
					_nextTimestamp = null;
					return;
				}

				_nextTimestamp = checked(_nextTimestamp.Value + _periodTicks.Value);
			}
		}

		private static void ValidateTimeout(TimeSpan value, string parameterName)
		{
			if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
			{
				throw new ArgumentOutOfRangeException(parameterName);
			}
		}
	}
}
