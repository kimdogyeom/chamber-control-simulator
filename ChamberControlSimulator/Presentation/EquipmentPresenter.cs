using System;
using System.Collections.Generic;
using System.Text;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentPresenter : IAsyncDisposable
	{
		private readonly IEquipmentView _view;
		private readonly ThermalController _controller;
		private readonly IEquipmentObservationRuntime _observationRuntime;
		private readonly IEquipmentCommandRuntime _commandRuntime;
		private readonly CancellationTokenSource _shutdown = new();
		private readonly object _lifecycleLock = new();
		private Task? _activeCycle;
		private Task? _activeCommand;
		private Task? _teardownTask;
		private long _pendingElapsedTicks;
		private int _cycleInProgress;
		private int _commandInProgress;
		private int _isDisposed;

		public EquipmentPresenter(
			IEquipmentView view,
			ThermalController controller,
			IEquipmentObservationRuntime observationRuntime,
			IEquipmentCommandRuntime commandRuntime)
		{
			_view = view ?? throw new ArgumentNullException(nameof(view));
			_controller = controller ?? throw new ArgumentNullException(nameof(controller));
			_observationRuntime = observationRuntime ?? throw new ArgumentNullException(nameof(observationRuntime));
			_commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));

			_view.StartRequested += OnStartRequestedAsync;
			_view.StopRequested += OnStopRequestedAsync;
			_view.AcknowledgeRequested += OnAcknowledgeRequested;
			_view.ResetRequested += OnResetRequestedAsync;
			_view.DoorToggleRequested += OnDoorToggleRequested;
			_view.ApplyTemperatureRequested += OnApplyTemperatureRequested;
			_view.PauseFeedbackRequested += OnPauseFeedbackRequested;
			_view.ResumeFeedbackRequested += OnResumeFeedbackRequested;
			_view.ClosingRequested += OnClosingRequestedAsync;
			_view.TimerTicked += OnTimerTickedAsync;
			_view.RecipeSelectionRequested += OnRecipeSelectionRequested;

			_view.ShowRecipeOptions(_controller.Recipes);
			RefreshView();
		}

		public ValueTask DisposeAsync()
		{
			Task teardownTask;
			lock (_lifecycleLock)
			{
				if (_teardownTask is null)
				{
					_commandRuntime.StopAdmission();
					Interlocked.Exchange(ref _isDisposed, 1);
					_shutdown.Cancel();
					UnsubscribeViewEvents();
					_teardownTask = TeardownAsync(_activeCycle, _activeCommand);
				}

				teardownTask = _teardownTask;
			}

			return new ValueTask(teardownTask);
		}

		private async Task TeardownAsync(Task? activeCycle, Task? activeCommand)
		{
			try
			{
				await Task.WhenAll(
					ObserveOwnedWorkAsync(activeCycle),
					ObserveOwnedWorkAsync(activeCommand));
			}
			finally
			{
				try
				{
					await _observationRuntime.DisposeAsync();
				}
				finally
				{
					_shutdown.Dispose();
				}
			}
		}

		private async Task ObserveOwnedWorkAsync(Task? work)
		{
			if (work is null)
			{
				return;
			}

			try
			{
				await work;
			}
			catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
		}

		private void UnsubscribeViewEvents()
		{
			_view.StartRequested -= OnStartRequestedAsync;
			_view.StopRequested -= OnStopRequestedAsync;
			_view.AcknowledgeRequested -= OnAcknowledgeRequested;
			_view.ResetRequested -= OnResetRequestedAsync;
			_view.DoorToggleRequested -= OnDoorToggleRequested;
			_view.ApplyTemperatureRequested -= OnApplyTemperatureRequested;
			_view.PauseFeedbackRequested -= OnPauseFeedbackRequested;
			_view.ResumeFeedbackRequested -= OnResumeFeedbackRequested;
			_view.ClosingRequested -= OnClosingRequestedAsync;
			_view.TimerTicked -= OnTimerTickedAsync;
			_view.RecipeSelectionRequested -= OnRecipeSelectionRequested;
		}

		private void RefreshView()
		{
			_view.ShowSnapshot(_controller.Snapshot);
			_view.ShowEventLog(_controller.EventHistory);
		}

		private Task OnStartRequestedAsync() =>
			OnCommandRequestedAsync(_commandRuntime.RequestStartAsync);

		private Task OnStopRequestedAsync() =>
			OnCommandRequestedAsync(_commandRuntime.RequestStopAsync);

		private Task OnResetRequestedAsync() =>
			OnCommandRequestedAsync(_commandRuntime.RequestResetAsync);

		private Task OnCommandRequestedAsync(
			Func<CancellationToken, Task<ChamberControlSimulator.Application.EquipmentCommandRequestResult>> requestCommand)
		{
			if (Volatile.Read(ref _isDisposed) != 0 ||
				Interlocked.CompareExchange(ref _commandInProgress, 1, 0) != 0)
			{
				return Task.CompletedTask;
			}

			TaskCompletionSource<bool>? activeCommandCompletion = null;
			try
			{
				lock (_lifecycleLock)
				{
					if (_teardownTask is not null)
					{
						Volatile.Write(ref _commandInProgress, 0);
						return Task.CompletedTask;
					}

					activeCommandCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					_activeCommand = activeCommandCompletion.Task;
					var activeCommand = requestCommand(_shutdown.Token);
					return CompleteCommandAsync(activeCommand, activeCommandCompletion);
				}
			}
			catch (Exception exception)
			{
				if (activeCommandCompletion is not null)
				{
					lock (_lifecycleLock)
					{
						if (ReferenceEquals(_activeCommand, activeCommandCompletion.Task))
						{
							_activeCommand = null;
						}
					}

					activeCommandCompletion.TrySetResult(true);
				}

				System.Diagnostics.Trace.TraceError(exception.ToString());
				Volatile.Write(ref _commandInProgress, 0);
				return Task.CompletedTask;
			}
		}

		private void OnAcknowledgeRequested(object? sender, EventArgs e)
		{
			_controller.AcknowledgeAlarm();
			RefreshView();
		}

		private void OnDoorToggleRequested(object? sender, EventArgs e)
		{
			var nextDoorState = !_controller.Snapshot.IsDoorOpen;

			_observationRuntime.SetDoorClosed(!nextDoorState);
			RefreshView();
		}

		private void OnApplyTemperatureRequested(object? sender, EventArgs e)
		{
			_observationRuntime.SetCurrentTemperature(_view.SimulatedTemperature);
			RefreshView();
		}

		private void OnPauseFeedbackRequested(object? sender, EventArgs e)
		{
			_observationRuntime.SetSensorHealthy(false);
			RefreshView();
		}

		private void OnResumeFeedbackRequested(object? sender, EventArgs e)
		{
			_observationRuntime.SetSensorHealthy(true);
			RefreshView();
		}

		private void OnRecipeSelectionRequested(object? sender, RecipeSelectionRequestedEventArgs e)
		{
			_controller.SelectRecipe(e.RecipeName);
			RefreshView();
		}

		private Task OnClosingRequestedAsync() => DisposeAsync().AsTask();

		private Task OnTimerTickedAsync(TimerTickedEventArgs e)
		{
			if (Volatile.Read(ref _isDisposed) != 0)
			{
				return Task.CompletedTask;
			}

			Interlocked.Add(ref _pendingElapsedTicks, e.Elapsed.Ticks);
			if (Interlocked.CompareExchange(ref _cycleInProgress, 1, 0) != 0)
			{
				return Task.CompletedTask;
			}

			TaskCompletionSource<bool>? activeCycleCompletion = null;
			try
			{
				lock (_lifecycleLock)
				{
					if (_teardownTask is not null)
					{
						Volatile.Write(ref _cycleInProgress, 0);
						return Task.CompletedTask;
					}

					var elapsed = TimeSpan.FromTicks(Interlocked.Exchange(ref _pendingElapsedTicks, 0));
					activeCycleCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					_activeCycle = activeCycleCompletion.Task;
					var activeCycle = _observationRuntime.CycleAsync(elapsed, _shutdown.Token);
					return CompleteCycleAsync(activeCycle, activeCycleCompletion);
				}
			}
			catch (Exception exception)
			{
				if (activeCycleCompletion is not null)
				{
					lock (_lifecycleLock)
					{
						if (ReferenceEquals(_activeCycle, activeCycleCompletion.Task))
						{
							_activeCycle = null;
						}
					}

					activeCycleCompletion.TrySetResult(true);
				}

				System.Diagnostics.Trace.TraceError(exception.ToString());
				Volatile.Write(ref _cycleInProgress, 0);
				return Task.CompletedTask;
			}
		}

		private async Task CompleteCommandAsync(
			Task<ChamberControlSimulator.Application.EquipmentCommandRequestResult> activeCommand,
			TaskCompletionSource<bool> activeCommandCompletion)
		{
			try
			{
				await activeCommand;
				if (Volatile.Read(ref _isDisposed) == 0)
				{
					RefreshView();
				}
			}
			catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
			finally
			{
				lock (_lifecycleLock)
				{
					if (ReferenceEquals(_activeCommand, activeCommandCompletion.Task))
					{
						_activeCommand = null;
					}
				}

				Volatile.Write(ref _commandInProgress, 0);
				activeCommandCompletion.TrySetResult(true);
			}
		}

		private async Task CompleteCycleAsync(
			Task activeCycle,
			TaskCompletionSource<bool> activeCycleCompletion)
		{
			try
			{
				await activeCycle;
				if (Volatile.Read(ref _isDisposed) == 0)
				{
					RefreshView();
				}
			}
			catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
			finally
			{
				lock (_lifecycleLock)
				{
					if (ReferenceEquals(_activeCycle, activeCycleCompletion.Task))
					{
						_activeCycle = null;
					}
				}

				Volatile.Write(ref _cycleInProgress, 0);
				activeCycleCompletion.TrySetResult(true);
			}
		}
	}
}
