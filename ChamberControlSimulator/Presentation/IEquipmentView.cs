using System;
using System.Collections.Generic;
using System.Text;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Presentation
{
	public interface IEquipmentView
	{
		event EventHandler? StartRequested;
		event EventHandler? StopRequested;
		event EventHandler? AcknowledgeRequested;
		event EventHandler? ResetRequested;

		event EventHandler? DoorToggleRequested;
		event EventHandler? ApplyTemperatureRequested;
		event EventHandler? PauseFeedbackRequested;
		event EventHandler? ResumeFeedbackRequested;

		event EventHandler<TimerTickedEventArgs>? TimerTicked;
		event EventHandler<RecipeSelectionRequestedEventArgs>? RecipeSelectionRequested;

		double SimulatedTemperature { get; }

		void ShowRecipeOptions(IReadOnlyList<Recipe> recipes);
		void ShowSnapshot(ControllerSnapshot snapshot);
		void ShowEventLog(IReadOnlyList<EventLogEntry> entries);

	}
}
