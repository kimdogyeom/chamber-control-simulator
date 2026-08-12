using System;
using System.Collections.Generic;
using System.Text;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentPresenter
	{
		private readonly IEquipmentView _view;
		private readonly ThermalController _controller;

		public EquipmentPresenter(
			IEquipmentView view,
			ThermalController controller)
		{
			_view = view ?? throw new ArgumentNullException(nameof(view));
			_controller = controller ?? throw new ArgumentNullException(nameof(controller));

			_view.StartRequested += OnStartRequested;
			_view.StopRequested += OnStopRequested;
			_view.AcknowledgeRequested += OnAcknowledgeRequested;
			_view.ResetRequested += OnResetRequested;
			_view.DoorToggleRequested += OnDoorToggleRequested;
			_view.ApplyTemperatureRequested += OnApplyTemperatureRequested;
			_view.PauseFeedbackRequested += OnPauseFeedbackRequested;
			_view.ResumeFeedbackRequested += OnResumeFeedbackRequested;
			_view.TimerTicked += OnTimerTicked;
			_view.RecipeSelectionRequested += OnRecipeSelectionRequested;

			_view.ShowRecipeOptions(_controller.Recipes);
			RefreshView();
		}

		private void RefreshView()
		{
			_view.ShowSnapshot(_controller.Snapshot);
			_view.ShowEventLog(_controller.EventHistory);
		}

		private void OnStartRequested(object? sender, EventArgs e)
		{
			_controller.Start();
			RefreshView();
		}

		private void OnStopRequested(object? sender, EventArgs e)
		{
			_controller.Stop();
			RefreshView();
		}

		private void OnAcknowledgeRequested(object? sender, EventArgs e)
		{
			_controller.AcknowledgeAlarm();
			RefreshView();
		}

		private void OnResetRequested(object? sender, EventArgs e)
		{
			_controller.Reset();
			RefreshView();
		}

		private void OnDoorToggleRequested(object? sender, EventArgs e)
		{
			var nextDoorState = !_controller.Snapshot.IsDoorOpen;

			_controller.SetDoorOpen(nextDoorState);
			RefreshView();
		}

		private void OnApplyTemperatureRequested(object? sender, EventArgs e)
		{
			_controller.ReportTemperature(_view.SimulatedTemperature);
			RefreshView();
		}

		private void OnPauseFeedbackRequested(object? sender, EventArgs e)
		{
			_controller.PauseFeedback();
			RefreshView();
		}

		private void OnResumeFeedbackRequested(object? sender, EventArgs e)
		{
			_controller.ResumeFeedback();
			RefreshView();
		}

		private void OnRecipeSelectionRequested(object? sender, RecipeSelectionRequestedEventArgs e)
		{
			_controller.SelectRecipe(e.RecipeName);
			RefreshView();
		}

		private void OnTimerTicked(object? sender, TimerTickedEventArgs e)
		{
			_controller.Tick(e.Elapsed);
			RefreshView();
		}
	}
}
