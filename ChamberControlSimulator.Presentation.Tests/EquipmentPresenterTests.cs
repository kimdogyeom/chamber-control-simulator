using ChamberControlSimulator.Presentation;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Presentation.Tests;

[TestClass]
public sealed class EquipmentPresenterTests
{
	[TestMethod]
	public void Constructor_RendersRecipeOptionsAndInitialControllerState()
	{
		var standard = new Recipe("Standard", 250, 300);
		var highTemperature = new Recipe("High Temperature", 300, 350);
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			[standard, highTemperature],
			SimulationSettings.Illustrative);

		_ = new EquipmentPresenter(view, controller);

		CollectionAssert.AreEqual(
			new[] { standard, highTemperature },
			view.RecipeOptions.ToArray());
		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Idle, view.LastSnapshot!.State);
		Assert.AreEqual(standard.Name, view.LastSnapshot.RecipeName);
		Assert.IsEmpty(view.LastEventLog);
	}

	[TestMethod]
	public void StartRequested_RendersHeatingSnapshotAndNewEventHistory()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		_ = new EquipmentPresenter(view, controller);

		view.RaiseStartRequested();

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Heating, view.LastSnapshot!.State);
		CollectionAssert.AreEqual(
			new[] { "Start", "Phase: Precheck", "Phase: Heating" },
			view.LastEventLog.Select(entry => entry.Event).ToArray());
	}

	// 목적: Timer tick이 view를 refresh하되 legacy UI가 plant temperature를 합성하지 않는지 검증한다.
	// 예상 결과: elapsed 전달 뒤 Heating snapshot은 다시 render되지만 observed temperature는 20°C로 유지된다.
	// 완료 조건: Presenter가 Core Tick에 의존해 PLC-originated temperature를 바꾸지 않는다는 contract가 통과한다.
	[TestMethod]
	public void TimerTicked_RefreshesViewWithoutSynthesizingPlantTemperature()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			new Recipe("Slow Heat", 100, 110),
			SimulationSettings.Illustrative);
		_ = new EquipmentPresenter(view, controller);
		view.RaiseStartRequested();

		view.RaiseTimerTicked(TimeSpan.FromSeconds(2));

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Heating, view.LastSnapshot!.State);
		Assert.AreEqual(20d, view.LastSnapshot.CurrentTemperature);
	}

	[TestMethod]
	public void RecipeSelectionRequested_WhenIdle_RendersSelectedRecipe()
	{
		var standard = new Recipe("Standard", 250, 300);
		var highTemperature = new Recipe("High Temperature", 300, 350);
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			[standard, highTemperature],
			SimulationSettings.Illustrative);
		_ = new EquipmentPresenter(view, controller);

		view.RaiseRecipeSelectionRequested(highTemperature.Name);

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(highTemperature.Name, view.LastSnapshot!.RecipeName);
		Assert.AreEqual(highTemperature.TargetTemperature, view.LastSnapshot.TargetTemperature);
	}

	[TestMethod]
	public void RecipeSelectionRequested_WhenHeating_RendersOriginalRecipe()
	{
		var standard = new Recipe("Standard", 250, 300);
		var highTemperature = new Recipe("High Temperature", 300, 350);
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			[standard, highTemperature],
			SimulationSettings.Illustrative);
		_ = new EquipmentPresenter(view, controller);
		view.RaiseStartRequested();

		view.RaiseRecipeSelectionRequested(highTemperature.Name);

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(standard.Name, view.LastSnapshot!.RecipeName);
		Assert.AreEqual(standard.TargetTemperature, view.LastSnapshot.TargetTemperature);
		Assert.IsFalse(view.LastSnapshot.CanSelectRecipe);
	}

	private static ThermalController CreateController() => new(
		new Recipe("Standard", 250, 300),
		SimulationSettings.Illustrative);

	private sealed class FakeEquipmentView : IEquipmentView
	{
		public event EventHandler? StartRequested;
		public event EventHandler? StopRequested;
		public event EventHandler? AcknowledgeRequested;
		public event EventHandler? ResetRequested;
		public event EventHandler? DoorToggleRequested;
		public event EventHandler? ApplyTemperatureRequested;
		public event EventHandler? PauseFeedbackRequested;
		public event EventHandler? ResumeFeedbackRequested;
		public event EventHandler<TimerTickedEventArgs>? TimerTicked;
		public event EventHandler<RecipeSelectionRequestedEventArgs>? RecipeSelectionRequested;

		public double SimulatedTemperature { get; set; }
		public IReadOnlyList<Recipe> RecipeOptions { get; private set; } = [];
		public ControllerSnapshot? LastSnapshot { get; private set; }
		public IReadOnlyList<EventLogEntry> LastEventLog { get; private set; } = [];

		public void RaiseStartRequested() => StartRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseStopRequested() => StopRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseAcknowledgeRequested() => AcknowledgeRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseResetRequested() => ResetRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseDoorToggleRequested() => DoorToggleRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseApplyTemperatureRequested() => ApplyTemperatureRequested?.Invoke(this, EventArgs.Empty);
		public void RaisePauseFeedbackRequested() => PauseFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseResumeFeedbackRequested() => ResumeFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseTimerTicked(TimeSpan elapsed) =>
			TimerTicked?.Invoke(this, new TimerTickedEventArgs(elapsed));
		public void RaiseRecipeSelectionRequested(string recipeName) =>
			RecipeSelectionRequested?.Invoke(this, new RecipeSelectionRequestedEventArgs(recipeName));

		public void ShowRecipeOptions(IReadOnlyList<Recipe> recipes) => RecipeOptions = recipes.ToArray();
		public void ShowSnapshot(ControllerSnapshot snapshot) => LastSnapshot = snapshot;
		public void ShowEventLog(IReadOnlyList<EventLogEntry> entries) => LastEventLog = entries.ToArray();
	}
}