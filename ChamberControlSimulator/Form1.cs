using ChamberControlSimulator.Presentation;
using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using System.Drawing;

namespace ChamberControlSimulator
{
	public partial class Form1 : Form, IEquipmentView
	{
		private readonly System.Diagnostics.Stopwatch _stopwatch = new();
		private int _renderedEventCount;
		private EquipmentStatusViewModel? _lastStatus;
		private bool _closeTeardownStarted;
		private bool _allowCloseAfterTeardown;

		public double SimulatedTemperature => (double)nudSimulatedTemperature.Value;

		public Form1()
		{
			InitializeComponent();
			FormClosing += Form1_FormClosing;
			cmbRecipe.SelectionChangeCommitted += cmbRecipe_SelectionChangeCommitted;
		}

		public event Func<Task>? StartRequested;
		public event Func<Task>? StopRequested;
		public event EventHandler? AcknowledgeRequested;
		public event Func<Task>? ResetRequested;
		public event EventHandler? DoorToggleRequested;
		public event EventHandler? ApplyTemperatureRequested;
		public event EventHandler? PauseFeedbackRequested;
		public event EventHandler? ResumeFeedbackRequested;
		public event EventHandler? SuppressNextAcknowledgementRequested;
		public event EventHandler? ForceTransportDisconnectRequested;
		public event Func<Task>? ClosingRequested;
		public event Func<TimerTickedEventArgs, Task>? TimerTicked;
		public event EventHandler<RecipeSelectionRequestedEventArgs>? RecipeSelectionRequested;

		private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
		{
			if (_allowCloseAfterTeardown)
			{
				return;
			}

			e.Cancel = true;
			if (_closeTeardownStarted)
			{
				return;
			}

			_closeTeardownStarted = true;
			tmSimulationTick.Stop();
			try
			{
				await InvokeClosingRequestedAsync();
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
			finally
			{
				_allowCloseAfterTeardown = true;
				if (!IsDisposed)
				{
					Close();
				}
			}
		}

		private static async Task InvokeCommandRequestedAsync(Func<Task>? requested)
		{
			if (requested is null)
			{
				return;
			}

			foreach (var handler in requested.GetInvocationList().Cast<Func<Task>>())
			{
				await handler();
			}
		}

		private async Task InvokeClosingRequestedAsync()
		{
			if (ClosingRequested is null)
			{
				return;
			}

			foreach (var handler in ClosingRequested.GetInvocationList().Cast<Func<Task>>())
			{
				await handler();
			}
		}

		private async Task InvokeTimerTickedAsync(TimerTickedEventArgs timerTickedEventArgs)
		{
			if (TimerTicked is null)
			{
				return;
			}

			foreach (var handler in TimerTicked.GetInvocationList().Cast<Func<TimerTickedEventArgs, Task>>())
			{
				await handler(timerTickedEventArgs);
			}
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			_stopwatch.Start();
			tmSimulationTick.Start();
		}

		private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
		{

		}

		private void panel5_Paint(object sender, PaintEventArgs e)
		{

		}

		private async void btnStart_Click(object sender, EventArgs e)
		{
			try
			{
				await InvokeCommandRequestedAsync(StartRequested);
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
		}

		private async void btnStop_Click(object sender, EventArgs e)
		{
			try
			{
				await InvokeCommandRequestedAsync(StopRequested);
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
		}

		private void btnAcknowledge_Click(object sender, EventArgs e)
		{
			AcknowledgeRequested?.Invoke(this, EventArgs.Empty);
		}

		private async void btnReset_Click(object sender, EventArgs e)
		{
			try
			{
				await InvokeCommandRequestedAsync(ResetRequested);
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
		}

		private void btnDoorToggle_Click(object sender, EventArgs e)
		{
			DoorToggleRequested?.Invoke(this, EventArgs.Empty);
		}

		private void btnApplyTemperature_Click(object sender, EventArgs e)
		{
			ApplyTemperatureRequested?.Invoke(this, EventArgs.Empty);
		}

		private void btnPauseFeedback_Click(object sender, EventArgs e)
		{
			PauseFeedbackRequested?.Invoke(this, EventArgs.Empty);
		}

		private void btnResumeFeedback_Click(object sender, EventArgs e)
		{
			ResumeFeedbackRequested?.Invoke(this, EventArgs.Empty);
		}

		private void btnSuppressAck_Click(object sender, EventArgs e)
		{
			SuppressNextAcknowledgementRequested?.Invoke(this, EventArgs.Empty);
		}

		private void btnForceDisconnect_Click(object sender, EventArgs e)
		{
			ForceTransportDisconnectRequested?.Invoke(this, EventArgs.Empty);
		}

		private void cmbRecipe_SelectionChangeCommitted(object? sender, EventArgs e)
		{
			if (cmbRecipe.SelectedItem is Recipe selectedRecipe)
			{
				RecipeSelectionRequested?.Invoke(
					this,
					new RecipeSelectionRequestedEventArgs(selectedRecipe.Name));
			}
		}

		public void ShowRecipeOptions(IReadOnlyList<Recipe> recipes)
		{
			cmbRecipe.BeginUpdate();
			try
			{
				cmbRecipe.Items.Clear();

				foreach (var recipe in recipes)
				{
					cmbRecipe.Items.Add(recipe);
				}
			}
			finally
			{
				cmbRecipe.EndUpdate();
			}
		}

		private async void tmSimulationTick_Tick(object sender, EventArgs e)
		{
			var elapsed = _stopwatch.Elapsed;
			_stopwatch.Restart();

			try
			{
				await InvokeTimerTickedAsync(new TimerTickedEventArgs(elapsed));
			}
			catch (Exception exception)
			{
				System.Diagnostics.Trace.TraceError(exception.ToString());
			}
		}

		public void ShowSnapshot(ControllerSnapshot snapshot)
		{
			lblControllerState.Text = snapshot.State.ToString().ToUpperInvariant();
			(lblControllerState.BackColor, lblControllerState.ForeColor) = snapshot.State switch
			{
				ControllerState.Idle => (Color.FromArgb(227, 242, 253), Color.FromArgb(21, 101, 192)),
				ControllerState.Precheck or ControllerState.Heating or ControllerState.Holding or ControllerState.Cooling
					=> (Color.FromArgb(255, 243, 224), Color.FromArgb(230, 81, 0)),
				ControllerState.Complete => (Color.FromArgb(232, 245, 233), Color.FromArgb(46, 125, 50)),
				ControllerState.Alarm => (Color.FromArgb(255, 235, 238), Color.FromArgb(198, 40, 40)),
				ControllerState.Recovery => (Color.FromArgb(243, 229, 245), Color.FromArgb(123, 31, 162)),
				_ => (SystemColors.Control, SystemColors.ControlText)
			};
			lblEquipmentState.Text = $"상태 : {snapshot.State}";
			lblCurrentTemp.Text = $"현재 온도 : {snapshot.CurrentTemperature:F2} °C";
			lblTargetTemp.Text = $"목표 온도 : {snapshot.TargetTemperature:F2} °C";
			lblProgressStage.Text = $"진행 단계 : {snapshot.State}";
			var temperatureRange = snapshot.TargetTemperature - snapshot.AmbientTemperature;
			var rawProgress = temperatureRange > 0
				? (snapshot.CurrentTemperature - snapshot.AmbientTemperature)
				/ temperatureRange * 100 : 0;

			var progressValue = (int)Math.Round(Math.Clamp(rawProgress, 0, 100));
			prgTemperature.Value = progressValue;

			lblRecipeTargetTemp.Text = $"Target : {snapshot.TargetTemperature:F2} °C";

			cmbRecipe.Enabled = snapshot.CanSelectRecipe;

			var activeRecipe = cmbRecipe.Items
				.OfType<Recipe>()
				.FirstOrDefault(recipe => recipe.Name == snapshot.RecipeName);

			if (activeRecipe is not null && !Equals(cmbRecipe.SelectedItem, activeRecipe))
			{
				cmbRecipe.SelectedItem = activeRecipe;
			}
			lblDoorState.Text =
				$"Door : {(snapshot.IsDoorOpen ? "Open" : "Closed")}";

			lblFeedbackState.Text =
				$"Sensor Feedback : {(snapshot.IsFeedbackPaused ? "Paused" : "Active")}";

			lblActiveAlarm.Text =
				$"Active Alarm : {snapshot.ActiveAlarm?.ToString() ?? "None"}";

			lblRecoveryReady.Text =
				$"Recovery Ready : {(snapshot.IsRecoveryReady ? "Yes" : "No")}";

			btnStart.Enabled = snapshot.CanStart;
			btnAcknowledge.Enabled = snapshot.CanAcknowledge;
			btnReset.Enabled = snapshot.CanReset;

			btnDoorToggle.Text = snapshot.IsDoorOpen
				? "Close Door"
				: "Open Door";

			btnPauseFeedback.Enabled = !snapshot.IsFeedbackPaused;
			btnResumeFeedback.Enabled = snapshot.IsFeedbackPaused;
		}
		public void ShowEquipmentStatus(EquipmentStatusViewModel status)
		{
			_lastStatus = status;
			lblPlcConnection.Text = $"PLC Connection : {status.ConnectionState}";
			lblSynchronization.Text = $"Synchronization : {status.SynchronizationState}";
			lblCommandStatus.Text = status.CommandDisposition == EquipmentCommandLifecycleDisposition.NoCommand
				? "Command : None"
				: $"Command : {status.CommandKind?.ToString() ?? "—"} #{status.CommandId?.ToString() ?? "—"} {status.CommandDisposition}";
		}

		private bool IsEventLogAtBottom()
		{
			if (lvwEventLog.Items.Count == 0)
			{
				return true;
			}

			var lastItem = lvwEventLog.Items[^1];

			return lastItem.Bounds.Bottom <= lvwEventLog.ClientRectangle.Bottom;
		}

		public void ShowEventLog(IReadOnlyList<EventLogEntry> entries)
		{
			var wasAtBottom = IsEventLogAtBottom();

			if (entries.Count < _renderedEventCount)
			{
				lvwEventLog.Items.Clear();
				_renderedEventCount = 0;
				wasAtBottom = true;
			}
			var newEventsWereAdded = entries.Count > _renderedEventCount;

			if (!newEventsWereAdded)
			{
				return;
			}

			lvwEventLog.BeginUpdate();
			try
			{
				for ( var index = _renderedEventCount; index < entries.Count; index++)
				{
					var entry = entries[index];
					var item = new ListViewItem(entry.Elapsed.ToString(@"hh\:mm\:ss\.ff"));
					item.SubItems.Add(entry.State.ToString());
					item.SubItems.Add(entry.Event);
					item.SubItems.Add(entry.Alarm?.ToString() ?? string.Empty);
					item.SubItems.Add(_lastStatus?.ConnectionState.ToString() ?? string.Empty);
					var commandText = _lastStatus is null || _lastStatus.CommandDisposition == EquipmentCommandLifecycleDisposition.NoCommand
						? string.Empty
						: $"{_lastStatus.CommandKind?.ToString() ?? "—"} #{_lastStatus.CommandId?.ToString() ?? "—"} {_lastStatus.CommandDisposition}";
					item.SubItems.Add(commandText);
					lvwEventLog.Items.Add(item);
				}

				_renderedEventCount = entries.Count;

				if (wasAtBottom && lvwEventLog.Items.Count > 0)
				{
					lvwEventLog.EnsureVisible(lvwEventLog.Items.Count - 1);
				}
			}
			finally
			{
				lvwEventLog.EndUpdate();
			}
		}

		private void grpEquipmentStatus_Enter(object sender, EventArgs e)
		{

		}

	}
}
