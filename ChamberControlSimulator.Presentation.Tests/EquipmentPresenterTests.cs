using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using ChamberControlSimulator.Plc.Simulation;
using ChamberControlSimulator.Presentation;

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

		_ = new EquipmentPresenter(view, controller, new PassiveObservationRuntime());

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
		_ = new EquipmentPresenter(view, controller, new PassiveObservationRuntime());

		view.RaiseStartRequested();

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Heating, view.LastSnapshot!.State);
		CollectionAssert.AreEqual(
			new[] { "Start", "Phase: Precheck", "Phase: Heating" },
			view.LastEventLog.Select(entry => entry.Event).ToArray());
	}

	// 목적: WinForms timer와 Form closing이 async observation cycle 및 teardown completion을 await할 수 있는 View contract인지 검증한다.
	// 예상 결과: TimerTicked는 Func<TimerTickedEventArgs, Task>, ClosingRequested는 Func<Task> event handler를 노출한다.
	// 완료 조건: Form이 busy elapsed와 close teardown을 fire-and-forget 하지 않는 awaitable seam을 가진다.
	[TestMethod]
	public void ViewLifecycleEvents_ExposeAwaitableTimerAndClosingHandlers()
	{
		var timerTicked = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.TimerTicked));
		var closingRequested = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.ClosingRequested));

		Assert.IsNotNull(timerTicked);
		Assert.IsNotNull(closingRequested);
		Assert.AreEqual(typeof(Func<TimerTickedEventArgs, Task>), timerTicked.EventHandlerType);
		Assert.AreEqual(typeof(Func<Task>), closingRequested.EventHandlerType);
	}

	// 목적: Timer tick이 observation runtime cycle 이후 View를 refresh하고 direct Core Tick으로 plant temperature를 합성하지 않는지 검증한다.
	// 예상 결과: external observation의 20°C가 Heating snapshot에 그대로 render되고 runtime은 elapsed를 한 번 받는다.
	// 완료 조건: Presenter timer가 injected observation runtime을 통해서만 observed snapshot을 갱신한다.
	[TestMethod]
	public void TimerTicked_RefreshesViewWithoutSynthesizingPlantTemperature()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			new Recipe("Slow Heat", 100, 110),
			SimulationSettings.Illustrative);
		var runtime = new RecordingObservationRuntime(controller, observedTemperature: 20d);
		_ = new EquipmentPresenter(view, controller, runtime);
		view.RaiseStartRequested();

		view.RaiseTimerTicked(TimeSpan.FromSeconds(2));

		Assert.AreEqual(1, runtime.CycleCallCount);
		Assert.AreEqual(TimeSpan.FromSeconds(2), runtime.LastElapsed);
		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Heating, view.LastSnapshot!.State);
		Assert.AreEqual(20d, view.LastSnapshot.CurrentTemperature);
	}

	// 목적: Timer tick이 injected observation runtime을 통해 external temperature를 Core와 View에 반영하는지 검증한다.
	// 예상 결과: runtime은 한 번만 elapsed를 받고, View는 runtime observation의 30°C snapshot을 render한다.
	// 완료 조건: Presenter가 Core Tick 대신 observation runtime을 호출하는 compile-time contract가 RED로 고정된다.
	[TestMethod]
	public void TimerTicked_UsesObservationRuntimeToRenderObservedTemperature()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			new Recipe("Observed", 100d, 110d),
			SimulationSettings.Illustrative);
		var runtime = new RecordingObservationRuntime(controller, observedTemperature: 30d);
		_ = new EquipmentPresenter(view, controller, runtime);

		view.RaiseTimerTicked(TimeSpan.FromSeconds(1));

		Assert.AreEqual(1, runtime.CycleCallCount);
		Assert.AreEqual(TimeSpan.FromSeconds(1), runtime.LastElapsed);
		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(30d, view.LastSnapshot!.CurrentTemperature);
	}

	// 목적: simulation input 버튼이 Core state를 직접 바꾸지 않고 observation runtime으로 PLC-side input을 전달하는지 검증한다.
	// 예상 결과: door, temperature, sensor-health 요청이 runtime에 기록되고 controller snapshot은 다음 observation cycle 전까지 직접 변경되지 않는다.
	// 완료 조건: Presenter의 simulation input 경로가 P3 observed-input boundary를 우회하지 않는다.
	[TestMethod]
	public void SimulationInputRequests_AreForwardedThroughObservationRuntimeWithoutDirectCoreMutation()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new RecordingObservationRuntime(controller, observedTemperature: 20d);
		_ = new EquipmentPresenter(view, controller, runtime);

		view.RaiseDoorToggleRequested();

		Assert.IsTrue(runtime.LastDoorClosed.HasValue);
		Assert.IsFalse(runtime.LastDoorClosed.Value);
		Assert.IsFalse(controller.Snapshot.IsDoorOpen);

		view.SimulatedTemperature = 30d;
		view.RaiseApplyTemperatureRequested();

		Assert.IsTrue(runtime.LastRequestedTemperature.HasValue);
		Assert.AreEqual(30d, runtime.LastRequestedTemperature.Value);
		Assert.AreEqual(20d, controller.Snapshot.CurrentTemperature);

		view.RaisePauseFeedbackRequested();

		Assert.IsTrue(runtime.LastSensorHealthy.HasValue);
		Assert.IsFalse(runtime.LastSensorHealthy.Value);
		Assert.IsFalse(controller.Snapshot.IsFeedbackPaused);

		view.RaiseResumeFeedbackRequested();

		Assert.IsTrue(runtime.LastSensorHealthy.Value);
		Assert.IsFalse(controller.Snapshot.IsFeedbackPaused);
	}

	// 목적: concrete Virtual PLC control, Coordinator, Core observation mapping의 runtime chain을 검증한다.
	// 예상 결과: Virtual PLC에 설정한 30°C observed input이 one cycle 뒤 controller snapshot의 30°C가 된다.
	// 완료 조건: runtime composition이 PLC input을 읽고 Core policy에 적용하며 output write 없이 test가 통과한다.
	[TestMethod]
	public async Task ObservationRuntime_MapsVirtualPlcControlledTemperatureThroughCoordinator()
	{
		var controller = CreateController();
		var virtualPlc = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		var coordinator = new EquipmentCoordinator(controller, virtualPlc);
		await using var runtime = new EquipmentObservationRuntime(
			coordinator,
			virtualPlc.ObservationInputControl);

		runtime.SetCurrentTemperature(30d);
		await runtime.CycleAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.AreEqual(30d, controller.Snapshot.CurrentTemperature);
	}

	// 목적: P3 observation cycle이 pre-seeded P4 command의 virtual time, acknowledgement, heater state를 진행시키지 않는지 검증한다.
	// 예상 결과: pending Start command 뒤에도 P3 cycle은 20°C input과 acknowledged command ID 0을 유지한다.
	// 완료 조건: P3 runtime은 output write/ACK lifecycle을 호출하거나 간접 진행하지 않는다.
	[TestMethod]
	public async Task ObservationRuntime_Cycle_DoesNotAdvancePendingP4CommandState()
	{
		var controller = CreateController();
		var virtualPlc = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		var coordinator = new EquipmentCoordinator(controller, virtualPlc);
		await using var runtime = new EquipmentObservationRuntime(
			coordinator,
			virtualPlc.ObservationInputControl);

		await virtualPlc.ConnectAsync(CancellationToken.None);
		await virtualPlc.WriteOutputsAsync(
			new PlcOutputCommand(commandId: 42, PlcCommandKind.Start),
			CancellationToken.None);

		await runtime.CycleAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
		var inputAfterP3Cycle = await virtualPlc.ReadInputsAsync(CancellationToken.None);

		Assert.AreEqual(20d, controller.Snapshot.CurrentTemperature);
		Assert.AreEqual(0L, inputAfterP3Cycle.AcknowledgedCommandId);
	}

	// 목적: Timer event가 이전 observation cycle이 끝나기 전에 다시 발생해도 concurrent PLC read/apply cycle을 만들지 않는지 검증한다.
	// 예상 결과: 첫 cycle이 대기 중일 때 두 번째 tick은 runtime CycleAsync를 추가 호출하지 않는다.
	// 완료 조건: in-flight cycle 하나만 유지되어 duplicated observed-input 처리가 방지된다.
	[TestMethod]
	public async Task TimerTicked_WhileObservationCycleIsInFlight_DoesNotStartOverlappingCycle()
	{
		var view = new FakeEquipmentView();
		var runtime = new BlockingObservationRuntime();
		_ = new EquipmentPresenter(view, CreateController(), runtime);

		view.RaiseTimerTicked(TimeSpan.FromSeconds(1));
		await runtime.CycleStarted;

		try
		{
			view.RaiseTimerTicked(TimeSpan.FromSeconds(1));

			Assert.AreEqual(1, runtime.CycleCallCount);
		}
		finally
		{
			runtime.ReleaseCycle();
		}
	}

	// 목적: view closing이 in-flight observation cycle의 cancellation을 먼저 전달하고 runtime disposal을 한 번만 수행하며 late View refresh를 막는지 검증한다.
	// 예상 결과: active cycle cancellation이 disposal 전에 관찰되고 closing 뒤 snapshot render 수는 증가하지 않는다.
	// 완료 조건: Form shutdown 중 cancellation → cycle 종료 → runtime disposal 순서와 no-late-render contract가 함께 증명된다.
	[TestMethod]
	public async Task ClosingRequested_CancelsActiveObservationCycleBeforeRuntimeDisposal()
	{
		var view = new FakeEquipmentView();
		var runtime = new CancellableObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);
		var initialSnapshotRenderCount = view.SnapshotRenderCount;

		try
		{
			view.RaiseTimerTicked(TimeSpan.FromSeconds(1));
			await runtime.CycleStarted;

			view.RaiseClosingRequested();
			await presenter.DisposeAsync();
		}
		finally
		{
			await presenter.DisposeAsync();
		}

		Assert.IsTrue(runtime.CancellationObserved.IsCompletedSuccessfully);
		Assert.IsTrue(runtime.DisposeObservedCancellation);
		Assert.AreEqual(1, runtime.DisposeCallCount);
		Assert.AreEqual(initialSnapshotRenderCount, view.SnapshotRenderCount);
	}

	// 목적: busy observation cycle 중 발생한 timer elapsed가 다음 admitted cycle에 누적되는지 검증한다.
	// 예상 결과: 1초 active cycle 뒤 busy 2초·3초와 다음 4초 tick은 두 번째 cycle에 총 9초로 전달된다.
	// 완료 조건: non-overlap 때문에 버려진 UI timer interval이 Core timeout·holding 시간에서 사라지지 않는다.
	[TestMethod]
	public async Task TimerTicked_WhileBusy_AccumulatesElapsedForNextAdmittedCycle()
	{
		var view = new FakeEquipmentView();
		var runtime = new ElapsedRecordingBlockingObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);

		try
		{
			var firstTimerTick = view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(1));
			await runtime.FirstCycleStarted;

			await view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(2));
			await view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(3));

			runtime.ReleaseFirstCycle();
			await firstTimerTick;
			await view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(4));

			CollectionAssert.AreEqual(
				new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9) },
				runtime.ReceivedElapsed.ToArray());
		}
		finally
		{
			runtime.ReleaseFirstCycle();
			await presenter.DisposeAsync();
		}
	}

	// 목적: awaitable closing request가 active observation cycle cancellation과 runtime disposal을 끝낼 때까지 완료되지 않는지 검증한다.
	// 예상 결과: closing task 완료 뒤 cancellation은 관찰됐고 runtime disposal은 정확히 한 번 끝난다.
	// 완료 조건: Form close가 Application.Run 종료 뒤의 fire-and-forget teardown에 의존하지 않는다.
	[TestMethod]
	public async Task ClosingRequestedAsync_AwaitsRuntimeTeardownBeforeCloseCanContinue()
	{
		var view = new FakeEquipmentView();
		var runtime = new CancellableObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);

		try
		{
			var timerTick = view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(1));
			await runtime.CycleStarted;

			await view.RaiseClosingRequestedAsync();
			await timerTick;

			Assert.IsTrue(runtime.CancellationObserved.IsCompletedSuccessfully);
			Assert.IsTrue(runtime.DisposeObservedCancellation);
			Assert.AreEqual(1, runtime.DisposeCallCount);
		}
		finally
		{
			await presenter.DisposeAsync();
		}
	}

	// 목적: 동시에 호출된 DisposeAsync caller가 같은 active-cycle teardown completion을 함께 await하는지 검증한다.
	// 예상 결과: non-cooperative cycle이 release되기 전에는 첫 번째와 두 번째 disposer 모두 완료되지 않는다.
	// 완료 조건: duplicate dispose가 조기 성공으로 teardown 완료를 거짓 보고하지 않고 runtime은 한 번만 dispose된다.
	[TestMethod]
	public async Task DisposeAsync_ConcurrentCallersShareActiveTeardownCompletion()
	{
		var view = new FakeEquipmentView();
		var runtime = new NonCooperativeObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);
		Task? firstDispose = null;
		Task? secondDispose = null;
		Task? timerTick = null;

		try
		{
			timerTick = view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(1));
			await runtime.CycleStarted;

			firstDispose = presenter.DisposeAsync().AsTask();
			secondDispose = presenter.DisposeAsync().AsTask();

			Assert.IsFalse(firstDispose.IsCompleted);
			Assert.IsFalse(secondDispose.IsCompleted);

			runtime.ReleaseCycle();
			await Task.WhenAll(firstDispose, secondDispose, timerTick);

			Assert.AreEqual(1, runtime.DisposeCallCount);
		}
		finally
		{
			runtime.ReleaseCycle();
			if (firstDispose is not null)
			{
				try { await firstDispose; } catch (OperationCanceledException) { }
			}
			if (secondDispose is not null)
			{
				try { await secondDispose; } catch (OperationCanceledException) { }
			}
			if (timerTick is not null)
			{
				try { await timerTick; } catch (OperationCanceledException) { }
			}
			await presenter.DisposeAsync();
		}
	}

	// 목적: active observation cycle이 non-cancellation fault로 끝나도 timer path와 teardown이 runtime disposal을 보장하는지 검증한다.
	// 예상 결과: timer/DisposeAsync 모두 fault를 외부로 전파하지 않고 runtime dispose는 한 번 수행된다.
	// 완료 조건: shutdown 중 cycle fault가 cleanup finally를 건너뛰거나 async event fault로 누출되지 않는다.
	[TestMethod]
	public async Task FaultedCycle_DoesNotSkipRuntimeDisposalOrEscapeTimerPath()
	{
		var view = new FakeEquipmentView();
		var runtime = new FaultingObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);
		Task? timerTick = null;
		Task? teardown = null;

		try
		{
			timerTick = view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(1));
			await runtime.CycleStarted;

			teardown = presenter.DisposeAsync().AsTask();
			runtime.FaultCycle();

			await timerTick;
			await teardown;

			Assert.AreEqual(1, runtime.DisposeCallCount);
		}
		finally
		{
			runtime.FaultCycle();
			if (timerTick is not null)
			{
				try { await timerTick; } catch (InvalidOperationException) { }
			}
			if (teardown is not null)
			{
				try { await teardown; } catch (InvalidOperationException) { }
			}
			await presenter.DisposeAsync();
		}
	}

	// 목적: 실제 Program composition이 broad simulation facade 대신 P3-only concrete input facade를 runtime에 주입하는지 검증한다.
	// 예상 결과: injected object는 ObservationInputControl과 동일하지만 SimulationControl과 다르고 Advance·ACK suppression·transport fault API를 갖지 않는다.
	// 완료 조건: interface 선언뿐 아니라 P3 실제 객체 graph에서도 P4 virtual-time/ACK/fault capability가 도달 불가능하다.
	[TestMethod]
	public async Task ProgramComposition_InjectsDistinctP3ObservationInputFacadeWithoutP4Controls()
	{
		var controller = CreateController();
		var virtualPlc = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		var coordinator = new EquipmentCoordinator(controller, virtualPlc);
		EquipmentObservationRuntime? runtime = null;

		try
		{
			var factory = typeof(global::ChamberControlSimulator.Program).GetMethod(
				"CreateObservationRuntime",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
				binder: null,
				types: [typeof(EquipmentCoordinator), typeof(VirtualPlcClient)],
				modifiers: null);

			Assert.IsNotNull(factory);
			runtime = factory.Invoke(null, [coordinator, virtualPlc]) as EquipmentObservationRuntime;
			Assert.IsNotNull(runtime);

			var simulationControlField = typeof(EquipmentObservationRuntime).GetField(
				"_simulationControl",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.IsNotNull(simulationControlField);

			var observationInputProperty = typeof(VirtualPlcClient).GetProperty("ObservationInputControl");
			Assert.IsNotNull(observationInputProperty);
			var injectedControl = simulationControlField.GetValue(runtime);
			var observationInputControl = observationInputProperty.GetValue(virtualPlc);

			Assert.IsNotNull(injectedControl);
			Assert.IsNotNull(observationInputControl);
			Assert.AreSame(observationInputControl, injectedControl);
			Assert.AreNotSame(virtualPlc.SimulationControl, injectedControl);
			Assert.IsNull(injectedControl.GetType().GetMethod("Advance"));
			Assert.IsNull(injectedControl.GetType().GetMethod("ForceTransportDisconnect"));
			Assert.IsNull(injectedControl.GetType().GetMethod("SuppressNextAcknowledgement"));
		}
		finally
		{
			if (runtime is not null)
			{
				await runtime.DisposeAsync();
			}
			else
			{
				await virtualPlc.DisposeAsync();
			}
		}
	}

	// 목적: P3 WinForms runtime과 coordinator가 P4 output/ACK capability를 갖지 않는 read-only port만 의존하는지 검증한다.
	// 예상 결과: coordinator constructor는 IPlcObservationPort, runtime constructor는 IPlcObservationInputControl을 받고 input control에는 Advance·ACK fault API가 없다.
	// 완료 조건: P3 observation path가 compile-time capability로 P4 write/virtual-time state를 간접 전진시키지 않는다.
	[TestMethod]
	public void P3Runtime_UsesObservationOnlyPortsWithoutP4ControlCapability()
	{
		var coordinatorConstructor = typeof(EquipmentCoordinator).GetConstructors()
			.Single(constructor => constructor.GetParameters().Length == 2);
		var runtimeConstructor = typeof(EquipmentObservationRuntime).GetConstructors()
			.Single(constructor => constructor.GetParameters().Length == 2);
		var coordinatorPort = coordinatorConstructor.GetParameters()[1].ParameterType;
		var runtimeInputControl = runtimeConstructor.GetParameters()[1].ParameterType;

		Assert.AreEqual("IPlcObservationPort", coordinatorPort.Name);
		Assert.AreEqual("IPlcObservationInputControl", runtimeInputControl.Name);
		Assert.IsNull(coordinatorPort.GetMethod("WriteOutputsAsync"));
		Assert.IsNull(runtimeInputControl.GetMethod("Advance"));
		Assert.IsNull(runtimeInputControl.GetMethod("ForceTransportDisconnect"));
		Assert.IsNull(runtimeInputControl.GetMethod("SuppressNextAcknowledgement"));
	}

	// 목적: runtime CycleAsync가 raw Task를 반환하기 전에 reentrant DisposeAsync가 발생해도 runtime disposal이 cycle completion보다 앞서지 않는지 검증한다.
	// 예상 결과: cycle publication 중에는 runtime dispose가 0회이고, cycle release 뒤 shared teardown이 정확히 한 번 dispose한다.
	// 완료 조건: admitted-but-unpublished handoff에서도 disposer가 reservation completion을 await해 P3 runtime을 조기 dispose하지 않는다.
	[TestMethod]
	public async Task DisposeAsync_DuringCycleTaskPublication_WaitsForPublishedCycleBeforeRuntimeDisposal()
	{
		var view = new FakeEquipmentView();
		var runtime = new ReentrantDisposingObservationRuntime();
		var presenter = new EquipmentPresenter(view, CreateController(), runtime);
		runtime.DisposeDuringCycle = presenter.DisposeAsync;
		Task? timerTick = null;

		try
		{
			timerTick = view.RaiseTimerTickedAsync(TimeSpan.FromSeconds(1));
			await runtime.CycleStarted;

			Assert.AreEqual(0, runtime.DisposeCallCount);

			runtime.ReleaseCycle();
			await timerTick;
			await presenter.DisposeAsync();

			Assert.AreEqual(1, runtime.DisposeCallCount);
		}
		finally
		{
			runtime.ReleaseCycle();
			if (timerTick is not null)
			{
				await timerTick;
			}
			await presenter.DisposeAsync();
		}
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
		_ = new EquipmentPresenter(view, controller, new PassiveObservationRuntime());

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
		_ = new EquipmentPresenter(view, controller, new PassiveObservationRuntime());
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

	private sealed class ReentrantDisposingObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _cycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _releaseCycle = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Func<ValueTask>? DisposeDuringCycle { get; set; }
		public Task CycleStarted => _cycleStarted.Task;
		public int DisposeCallCount { get; private set; }

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_ = DisposeDuringCycle?.Invoke();
			_cycleStarted.TrySetResult(true);
			return _releaseCycle.Task;
		}

		public void ReleaseCycle() => _releaseCycle.TrySetResult(true);

		public ValueTask DisposeAsync()
		{
			DisposeCallCount++;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ElapsedRecordingBlockingObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _firstCycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _releaseFirstCycle = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public List<TimeSpan> ReceivedElapsed { get; } = [];
		public Task FirstCycleStarted => _firstCycleStarted.Task;

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			ReceivedElapsed.Add(elapsed);
			if (ReceivedElapsed.Count == 1)
			{
				_firstCycleStarted.TrySetResult(true);
				await _releaseFirstCycle.Task.WaitAsync(cancellationToken);
			}
		}

		public void ReleaseFirstCycle() => _releaseFirstCycle.TrySetResult(true);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class NonCooperativeObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _cycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _releaseCycle = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task CycleStarted => _cycleStarted.Task;
		public int DisposeCallCount { get; private set; }

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_cycleStarted.TrySetResult(true);
			await _releaseCycle.Task;
		}

		public void ReleaseCycle() => _releaseCycle.TrySetResult(true);

		public ValueTask DisposeAsync()
		{
			DisposeCallCount++;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class FaultingObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _cycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _faultCycle = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task CycleStarted => _cycleStarted.Task;
		public int DisposeCallCount { get; private set; }

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_cycleStarted.TrySetResult(true);
			await _faultCycle.Task;
		}

		public void FaultCycle() => _faultCycle.TrySetException(new InvalidOperationException("Expected test cycle fault."));

		public ValueTask DisposeAsync()
		{
			DisposeCallCount++;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class CancellableObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _cycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task CycleStarted => _cycleStarted.Task;
		public Task CancellationObserved => _cancellationObserved.Task;
		public int DisposeCallCount { get; private set; }
		public bool DisposeObservedCancellation { get; private set; }

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_cycleStarted.TrySetResult(true);

			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				_cancellationObserved.TrySetResult(true);
				throw;
			}
		}

		public ValueTask DisposeAsync()
		{
			DisposeCallCount++;
			DisposeObservedCancellation = _cancellationObserved.Task.IsCompletedSuccessfully;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class BlockingObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly TaskCompletionSource<bool> _cycleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _releaseCycle = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int CycleCallCount { get; private set; }
		public Task CycleStarted => _cycleStarted.Task;

		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public async Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CycleCallCount++;
			_cycleStarted.TrySetResult(true);
			await _releaseCycle.Task.WaitAsync(cancellationToken);
		}

		public void ReleaseCycle() => _releaseCycle.TrySetResult(true);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PassiveObservationRuntime : IEquipmentObservationRuntime
	{
		public void SetCurrentTemperature(double currentTemperature)
		{
		}

		public void SetSensorHealthy(bool sensorHealthy)
		{
		}

		public void SetDoorClosed(bool doorClosed)
		{
		}

		public Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class RecordingObservationRuntime : IEquipmentObservationRuntime
	{
		private readonly ThermalController _controller;
		private readonly double _observedTemperature;

		public RecordingObservationRuntime(ThermalController controller, double observedTemperature)
		{
			_controller = controller;
			_observedTemperature = observedTemperature;
		}

		public int CycleCallCount { get; private set; }
		public TimeSpan LastElapsed { get; private set; }
		public bool? LastDoorClosed { get; private set; }
		public bool? LastSensorHealthy { get; private set; }
		public double? LastRequestedTemperature { get; private set; }

		public void SetDoorClosed(bool doorClosed) => LastDoorClosed = doorClosed;

		public void SetSensorHealthy(bool sensorHealthy) => LastSensorHealthy = sensorHealthy;

		public void SetCurrentTemperature(double currentTemperature) => LastRequestedTemperature = currentTemperature;

		public Task CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CycleCallCount++;
			LastElapsed = elapsed;
			_controller.ApplyObservation(
				new ThermalObservation(isDoorOpen: false, sensorHealthy: true, currentTemperature: _observedTemperature),
				elapsed);
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

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
		public event Func<Task>? ClosingRequested;
		public event Func<TimerTickedEventArgs, Task>? TimerTicked;
		public event EventHandler<RecipeSelectionRequestedEventArgs>? RecipeSelectionRequested;

		public double SimulatedTemperature { get; set; }
		public IReadOnlyList<Recipe> RecipeOptions { get; private set; } = [];
		public ControllerSnapshot? LastSnapshot { get; private set; }
		public int SnapshotRenderCount { get; private set; }
		public IReadOnlyList<EventLogEntry> LastEventLog { get; private set; } = [];

		public void RaiseStartRequested() => StartRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseStopRequested() => StopRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseAcknowledgeRequested() => AcknowledgeRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseResetRequested() => ResetRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseDoorToggleRequested() => DoorToggleRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseApplyTemperatureRequested() => ApplyTemperatureRequested?.Invoke(this, EventArgs.Empty);
		public void RaisePauseFeedbackRequested() => PauseFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseResumeFeedbackRequested() => ResumeFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseClosingRequested() => _ = RaiseClosingRequestedAsync();

		public async Task RaiseClosingRequestedAsync()
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

		public void RaiseTimerTicked(TimeSpan elapsed) => _ = RaiseTimerTickedAsync(elapsed);

		public async Task RaiseTimerTickedAsync(TimeSpan elapsed)
		{
			if (TimerTicked is null)
			{
				return;
			}

			foreach (var handler in TimerTicked.GetInvocationList().Cast<Func<TimerTickedEventArgs, Task>>())
			{
				await handler(new TimerTickedEventArgs(elapsed));
			}
		}
		public void RaiseRecipeSelectionRequested(string recipeName) =>
			RecipeSelectionRequested?.Invoke(this, new RecipeSelectionRequestedEventArgs(recipeName));

		public void ShowRecipeOptions(IReadOnlyList<Recipe> recipes) => RecipeOptions = recipes.ToArray();

		public void ShowSnapshot(ControllerSnapshot snapshot)
		{
			LastSnapshot = snapshot;
			SnapshotRenderCount++;
		}

		public void ShowEventLog(IReadOnlyList<EventLogEntry> entries) => LastEventLog = entries.ToArray();
	}
}