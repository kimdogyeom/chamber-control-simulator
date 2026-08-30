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

		_ = CreatePresenter(view, controller, new PassiveObservationRuntime());

		CollectionAssert.AreEqual(
			new[] { standard, highTemperature },
			view.RecipeOptions.ToArray());
		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(ControllerState.Idle, view.LastSnapshot!.State);
		Assert.AreEqual(standard.Name, view.LastSnapshot.RecipeName);
		Assert.IsEmpty(view.LastEventLog);
	}
	// 목적: Event Log 새로고침이 매 cycle마다 호출되어도 View가 최신 status와 함께 로그를 받는지 검증한다.
	// 예상 결과: ShowEventLog 호출 횟수는 cycle마다 증가하고 LastStatus connection이 Connected다.
	// 완료 조건: Core EventLogEntry 스키마를 바꾸지 않고 status stamp 경로가 유지된다.
	[TestMethod]
	public void TimerTicked_ForwardsEventLogWithLatestEquipmentStatus()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(new Recipe("Standard", 250, 300), SimulationSettings.Illustrative);
		var runtime = new StatusObservationRuntime(
			controller,
			PlcConnectionState.Connected,
			ConnectionSynchronizationState.Synchronized,
			EquipmentCommandLifecycleDisposition.NoCommand,
			null,
			null);
		_ = CreatePresenter(view, controller, runtime, runtime);
		var rendersAfterConstruct = view.EventLogRenderCount;
		view.RaiseTimerTicked(TimeSpan.Zero);
		Assert.AreEqual(rendersAfterConstruct + 1, view.EventLogRenderCount);
		Assert.AreEqual(PlcConnectionState.Connected, view.LastStatus!.ConnectionState);
	}
	// 목적: WinForms timer cycle이 Virtual PLC Advance를 호출해 ACK delay 뒤 Start가 Core Heating으로 끝나는지 검증한다.
	// 예상 결과: Start 직후는 AwaitingAcknowledgement/Idle이고, delay보다 큰 elapsed cycle 뒤 Completed/Heating이다.
	// 완료 조건: UI observation path가 벽시계만 흘리고 virtual semantic time을 멈춘 채 AcknowledgementTimedOut으로 죽지 않는다.
	[TestMethod]
	public async Task TimerTicked_AdvancesVirtualPlcAndCompletesStartAfterAcknowledgementDelay()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(
			new Recipe("Standard", 250, 300),
			SimulationSettings.Illustrative);
		var plc = new VirtualPlcClient(new VirtualPlcOptions(20d, 5d, TimeSpan.FromMilliseconds(400)));
		await using var commandRuntime = new EquipmentCommandRuntime(controller, plc, plc, TimeProvider.System);
		var observationRuntime = new EquipmentObservationRuntime(
			commandRuntime,
			plc.ObservationInputControl,
			plc.SimulationControl);
		await using var presenter = CreatePresenter(
			view,
			controller,
			observationRuntime,
			new EquipmentCommandFacade(commandRuntime));

		await view.RaiseTimerTickedAsync(TimeSpan.Zero);
		await view.RaiseStartRequestedAsync();

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, view.LastStatus!.CommandDisposition);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);

		await view.RaiseTimerTickedAsync(TimeSpan.FromMilliseconds(500));

		Assert.AreEqual(EquipmentCommandLifecycleDisposition.Completed, view.LastStatus.CommandDisposition);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
	}
	// 목적: Alarm에서 Stop 거절이 command 라벨 disposition으로만 보이는지 검증한다.
	// 예상 결과: LastStatus.CommandDisposition이 AdmissionRejected이고 IsAutomatic은 false다.
	// 완료 조건: Event Log 스키마를 바꾸지 않고 거절이 라벨에 남는다.
	[TestMethod]
	public async Task StopRequested_WhileAlarm_MapsAdmissionRejectedWithoutAutomaticFlag()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		ThermalControllerTestCommands.CompleteStart(controller);
		controller.SetDoorOpen(true);
		Assert.AreEqual(ControllerState.Alarm, controller.Snapshot.State);
		var runtime = new PassiveCommandRuntime();
		await using var presenter = CreatePresenter(view, controller, new PassiveObservationRuntime(), runtime);
		await view.RaiseStopRequestedAsync();
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AdmissionRejected, view.LastStatus!.CommandDisposition);
		Assert.IsFalse(view.LastStatus.IsAutomatic);
		Assert.AreEqual(EquipmentCommandRejectionReason.CoreIneligible, view.LastStatus.RejectionReason);
		Assert.AreEqual(ControllerCommandKind.Stop, view.LastStatus.RejectedKind);
	}
	// 목적: Completed InputSnapshot 전에는 Door 토글이 PLC에 쓰지 않는지 검증한다.
	// 예상 결과: 첫 DoorToggle 후 LastDoorClosed는 null이고, Completed cycle 뒤 토글만 false를 기록한다.
	// 완료 조건: Core snapshot 문이 아니라 PLC 관측이 세팅된 뒤에만 SetDoorClosed가 호출된다.
	[TestMethod]
	public void DoorToggle_BeforeCompletedInputSnapshot_DoesNotWrite()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new RecordingObservationRuntime(controller, observedTemperature: 20d);
		_ = CreatePresenter(view, controller, runtime);

		view.RaiseDoorToggleRequested();
		Assert.IsFalse(runtime.LastDoorClosed.HasValue);

		view.RaiseTimerTicked(TimeSpan.Zero);
		view.RaiseDoorToggleRequested();
		Assert.IsTrue(runtime.LastDoorClosed.HasValue);
		Assert.IsFalse(runtime.LastDoorClosed.Value);
	}

	// 목적: Complete 자동 Stop 라벨이 Writing/AwaitingAck에서 (auto)를 붙이고 운영자 Stop에는 없는지 검증한다.
	// 예상 결과: 자동 Stop 상태는 Command : Stop #2 (auto) AwaitingAcknowledgement, 운영자 Stop은 (auto) 없음.
	// 완료 조건: Event Log를 바꾸지 않고 command 라벨만 구분한다.
	[TestMethod]
	public void CommandLabel_AutomaticStop_UsesAutoMarkerOnlyForCompleteStop()
	{
		Assert.AreEqual(
			"Command : Stop #2 (auto) AwaitingAcknowledgement",
			FormatCommandLabel(new EquipmentStatusViewModel(
				PlcConnectionState.Connected,
				ConnectionSynchronizationState.Synchronized,
				EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement,
				2,
				ControllerCommandKind.Stop,
				true)));
		Assert.AreEqual(
			"Command : Stop #2 (auto) Writing",
			FormatCommandLabel(new EquipmentStatusViewModel(
				PlcConnectionState.Connected,
				ConnectionSynchronizationState.Synchronized,
				EquipmentCommandLifecycleDisposition.Writing,
				2,
				ControllerCommandKind.Stop,
				true)));
		Assert.AreEqual(
			"Command : Stop #3 AwaitingAcknowledgement",
			FormatCommandLabel(new EquipmentStatusViewModel(
				PlcConnectionState.Connected,
				ConnectionSynchronizationState.Synchronized,
				EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement,
				3,
				ControllerCommandKind.Stop,
				false)));
	}

	private static string FormatCommandLabel(EquipmentStatusViewModel status) =>
		Form1.FormatCommandStatus(status);


	// 목적: observation cycle이 Connected이면서 WaitingForFreshInput이고 command가 AwaitingAck여도 View가 두 상태와 command를 따로 받는지 검증한다.
	// 예상 결과: LastStatus connection/sync/command가 cycle 값과 같고 CanReset은 snapshot 플래그 그대로다.
	// 완료 조건: Presenter가 복구 규칙을 계산하지 않고 Application 결과를 매핑만 한다.
	[TestMethod]
	public void TimerTicked_MapsConnectionSynchronizationAndCommandWithoutJudgingRecovery()
	{
		var view = new FakeEquipmentView();
		var controller = new ThermalController(new Recipe("Standard", 250, 300), SimulationSettings.Illustrative);
		ThermalControllerTestCommands.CompleteStart(controller);
		controller.ReportCommunicationLost();
		var runtime = new StatusObservationRuntime(
			controller,
			PlcConnectionState.Connected,
			ConnectionSynchronizationState.WaitingForFreshInput,
			EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement,
			1,
			ControllerCommandKind.Start);
		_ = CreatePresenter(view, controller, runtime, runtime);

		view.RaiseTimerTicked(TimeSpan.Zero);

		Assert.IsNotNull(view.LastStatus);
		Assert.AreEqual(PlcConnectionState.Connected, view.LastStatus!.ConnectionState);
		Assert.AreEqual(ConnectionSynchronizationState.WaitingForFreshInput, view.LastStatus.SynchronizationState);
		Assert.AreEqual(EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement, view.LastStatus.CommandDisposition);
		Assert.AreEqual(1L, view.LastStatus.CommandId);
		Assert.AreEqual(ControllerCommandKind.Start, view.LastStatus.CommandKind);
		Assert.AreEqual(AlarmKind.CommunicationLost, view.LastSnapshot!.ActiveAlarm);
		Assert.IsFalse(view.LastSnapshot.CanReset);
		Assert.IsFalse(view.LastSnapshot.IsRecoveryReady);
	}

	// 목적: Start/Stop/Reset command와 timer/closing lifecycle이 모두 owned awaitable View contract인지 검증한다.
	// 예상 결과: 세 command event와 ClosingRequested는 Func<Task>, TimerTicked는 Func<TimerTickedEventArgs, Task>다.
	// 완료 조건: command-family UI routing과 teardown이 fire-and-forget seam을 갖지 않는다.
	[TestMethod]
	public void ViewLifecycleEvents_ExposeAwaitableCommandTimerAndClosingHandlers()
	{
		var startRequested = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.StartRequested));
		var stopRequested = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.StopRequested));
		var resetRequested = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.ResetRequested));
		var timerTicked = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.TimerTicked));
		var closingRequested = typeof(IEquipmentView).GetEvent(nameof(IEquipmentView.ClosingRequested));

		Assert.AreEqual(typeof(Func<Task>), startRequested!.EventHandlerType);
		Assert.AreEqual(typeof(Func<Task>), stopRequested!.EventHandlerType);
		Assert.AreEqual(typeof(Func<Task>), resetRequested!.EventHandlerType);
		Assert.AreEqual(typeof(Func<TimerTickedEventArgs, Task>), timerTicked!.EventHandlerType);
		Assert.AreEqual(typeof(Func<Task>), closingRequested!.EventHandlerType);
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
		_ = CreatePresenter(view, controller, runtime);
		ThermalControllerTestCommands.CompleteStart(controller);

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
		_ = CreatePresenter(view, controller, runtime);

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
		_ = CreatePresenter(view, controller, runtime);
		view.RaiseTimerTicked(TimeSpan.Zero);

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
		var commandRuntime = new EquipmentCommandRuntime(controller, virtualPlc, virtualPlc, TimeProvider.System);
		await using var runtime = new EquipmentObservationRuntime(
			commandRuntime,
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
		var commandRuntime = new EquipmentCommandRuntime(controller, virtualPlc, virtualPlc, TimeProvider.System);
		await using var runtime = new EquipmentObservationRuntime(
			commandRuntime,
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
		_ = CreatePresenter(view, CreateController(), runtime);

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
		var presenter = CreatePresenter(view, CreateController(), runtime);
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
		var presenter = CreatePresenter(view, CreateController(), runtime);

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
		var presenter = CreatePresenter(view, CreateController(), runtime);

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
		var presenter = CreatePresenter(view, CreateController(), runtime);
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
		var presenter = CreatePresenter(view, CreateController(), runtime);
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

	// 목적: 실제 Program composition이 one shared P3/P4 command runtime과 P3-only simulation input facade를 함께 주입하는지 검증한다.
	// 예상 결과: wrapper는 exact EquipmentCommandRuntime과 ObservationInputControl을 보유하고 broad SimulationControl/fault API를 노출하지 않는다.
	// 완료 조건: production Start/write/read가 shared serialization boundary를 사용하면서 UI input capability는 좁게 유지된다.
	[TestMethod]
	public async Task ProgramComposition_InjectsSharedCommandRuntimeAndDistinctObservationInputFacade()
	{
		var controller = CreateController();
		var virtualPlc = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
		var commandRuntime = new EquipmentCommandRuntime(controller, virtualPlc, virtualPlc, TimeProvider.System);
		EquipmentObservationRuntime? runtime = null;

		try
		{
			var factory = typeof(global::ChamberControlSimulator.Program).GetMethod(
				"CreateObservationRuntime",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
				binder: null,
				types: [typeof(EquipmentCommandRuntime), typeof(VirtualPlcClient)],
				modifiers: null);

			Assert.IsNotNull(factory);
			runtime = factory.Invoke(null, [commandRuntime, virtualPlc]) as EquipmentObservationRuntime;
			Assert.IsNotNull(runtime);

			var commandRuntimeField = typeof(EquipmentObservationRuntime).GetField(
				"_commandRuntime",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var simulationControlField = typeof(EquipmentObservationRuntime).GetField(
				"_simulationControl",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.IsNotNull(commandRuntimeField);
			Assert.IsNotNull(simulationControlField);

			var observationInputProperty = typeof(VirtualPlcClient).GetProperty("ObservationInputControl");
			Assert.IsNotNull(observationInputProperty);
			var injectedControl = simulationControlField.GetValue(runtime);
			var observationInputControl = observationInputProperty.GetValue(virtualPlc);

			Assert.AreSame(commandRuntime, commandRuntimeField.GetValue(runtime));
			Assert.IsNotNull(injectedControl);
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
				await commandRuntime.DisposeAsync();
			}
		}
	}


	// 목적: ACK 억제와 transport disconnect가 operator command가 아니라 observation runtime simulation API로만 가는지 검증한다.
	// 예상 결과: SuppressAckCount와 ForceDisconnectCount가 1씩 증가하고 Core snapshot은 직접 바뀌지 않는다.
	// 완료 조건: fault-injection 경로가 Start/Reset 권한을 호출하지 않는다.
	[TestMethod]
	public void FaultInjectionRequests_AreForwardedThroughObservationRuntimeWithoutOperatorCommands()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new RecordingObservationRuntime(controller, observedTemperature: 20d);
		_ = CreatePresenter(view, controller, runtime);
		view.RaiseSuppressNextAcknowledgementRequested();
		view.RaiseForceTransportDisconnectRequested();
		Assert.AreEqual(1, runtime.SuppressAckCount);
		Assert.AreEqual(1, runtime.ForceDisconnectCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
	}

	// 목적: P3 coordinator는 observation-only port를 유지하고 WinForms wrapper는 command runtime plus input-control과 선택적 transport simulation만 의존하는지 검증한다.
	// 예상 결과: coordinator port에는 write가 없고 wrapper는 EquipmentCommandRuntime/IPlcObservationInputControl/optional VirtualPlcSimulationControl이다.
	// 완료 조건: input-control facade는 P4 fault API를 갖지 않으며 transport simulation은 별도 선택 인자다.
	[TestMethod]
	public void RuntimeComposition_PreservesObservationOnlyP3AndNarrowSimulationInput()
	{
		var coordinatorConstructor = typeof(EquipmentCoordinator).GetConstructors()
			.Single(constructor => constructor.GetParameters().Length == 2);
		var runtimeConstructor = typeof(EquipmentObservationRuntime).GetConstructors()
			.Single(constructor => constructor.GetParameters().Length == 3);
		var coordinatorPort = coordinatorConstructor.GetParameters()[1].ParameterType;
		var runtimeParameters = runtimeConstructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

		Assert.AreEqual("IPlcObservationPort", coordinatorPort.Name);
		Assert.AreEqual(typeof(EquipmentCommandRuntime), runtimeParameters[0]);
		Assert.AreEqual(typeof(IPlcObservationInputControl), runtimeParameters[1]);
		Assert.AreEqual("VirtualPlcSimulationControl", runtimeParameters[2].Name);
		Assert.IsNull(coordinatorPort.GetMethod("WriteOutputsAsync"));
		Assert.IsNull(runtimeParameters[1].GetMethod("Advance"));
		Assert.IsNull(runtimeParameters[1].GetMethod("ForceTransportDisconnect"));
		Assert.IsNull(runtimeParameters[1].GetMethod("SuppressNextAcknowledgement"));
		Assert.IsNotNull(typeof(IEquipmentObservationRuntime).GetMethod("ForceTransportDisconnect"));
		Assert.IsNull(typeof(IEquipmentObservationRuntime).GetMethod("RequestStartAsync"));
		Assert.IsNull(typeof(IEquipmentObservationRuntime).GetMethod("StopAdmission"));
		Assert.IsNotNull(typeof(IEquipmentCommandRuntime).GetMethod("RequestStartAsync"));
		Assert.IsNotNull(typeof(IEquipmentCommandRuntime).GetMethod("StopAdmission"));
		Assert.IsNull(typeof(IEquipmentCommandRuntime).GetMethod("CycleAsync"));
		Assert.IsNull(typeof(IEquipmentCommandRuntime).GetMethod("SetCurrentTemperature"));
		Assert.IsFalse(typeof(IEquipmentCommandRuntime).IsAssignableFrom(typeof(EquipmentObservationRuntime)));
		Assert.IsFalse(typeof(IEquipmentObservationRuntime).IsAssignableFrom(typeof(EquipmentCommandFacade)));
		Assert.IsTrue(typeof(IEquipmentObservationRuntime).IsAssignableFrom(typeof(EquipmentObservationRuntime)));
		Assert.IsTrue(typeof(IEquipmentCommandRuntime).IsAssignableFrom(typeof(EquipmentCommandFacade)));
	}

	// 목적: runtime CycleAsync가 raw Task를 반환하기 전에 reentrant DisposeAsync가 발생해도 runtime disposal이 cycle completion보다 앞서지 않는지 검증한다.
	// 예상 결과: cycle publication 중에는 runtime dispose가 0회이고, cycle release 뒤 shared teardown이 정확히 한 번 dispose한다.
	// 완료 조건: admitted-but-unpublished handoff에서도 disposer가 reservation completion을 await해 P3 runtime을 조기 dispose하지 않는다.
	[TestMethod]
	public async Task DisposeAsync_DuringCycleTaskPublication_WaitsForPublishedCycleBeforeRuntimeDisposal()
	{
		var view = new FakeEquipmentView();
		var runtime = new ReentrantDisposingObservationRuntime();
		var presenter = CreatePresenter(view, CreateController(), runtime);
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
		_ = CreatePresenter(view, controller, new PassiveObservationRuntime());

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
		_ = CreatePresenter(view, controller, new PassiveObservationRuntime());
		ThermalControllerTestCommands.CompleteStart(controller);

		view.RaiseRecipeSelectionRequested(highTemperature.Name);

		Assert.IsNotNull(view.LastSnapshot);
		Assert.AreEqual(standard.Name, view.LastSnapshot!.RecipeName);
		Assert.AreEqual(standard.TargetTemperature, view.LastSnapshot.TargetTemperature);
		Assert.IsFalse(view.LastSnapshot.CanSelectRecipe);
	}

	// 목적: awaitable Start request가 Application command runtime을 통해서만 흐르고 direct Core Start를 호출하지 않는지 검증한다.
	// 예상 결과: handler Task는 controlled request completion까지 대기하고 Core는 Idle/event-empty이며 runtime request count는 1이다.
	// 완료 조건: UI Start가 P4 receipt/ACK authority를 우회하지 않는 owned Task seam이다.
	[TestMethod]
	public async Task StartRequestedAsync_AwaitsCommandRuntimeWithoutDirectCoreStart()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new BlockingCommandObservationRuntime();
		await using var presenter = CreatePresenter(view, controller, runtime, runtime);

		var requestTask = view.RaiseStartRequestedAsync();
		await runtime.RequestStarted;

		Assert.IsFalse(requestTask.IsCompleted);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
		runtime.ReleaseRequest();
		await requestTask;
		Assert.AreEqual(1, runtime.RequestCount);
		Assert.AreEqual(ControllerState.Idle, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory);
	}

	// 목적: awaitable Stop request가 command runtime을 통해서만 흐르고 legacy direct Core Stop을 호출하지 않는지 검증한다.
	// 예상 결과: handler는 controlled completion까지 대기하고 runtime kind는 Stop이며 Core는 Heating/no Stop event다.
	// 완료 조건: Stop이 priority/preemption 또는 direct-Core UI shortcut을 갖지 않는다.
	[TestMethod]
	public async Task StopRequestedAsync_AwaitsCommandRuntimeWithoutDirectCoreStop()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		ThermalControllerTestCommands.CompleteStart(controller);
		var runtime = new BlockingCommandObservationRuntime();
		await using var presenter = CreatePresenter(view, controller, runtime, runtime);

		var requestTask = view.RaiseStopRequestedAsync();
		await runtime.RequestStarted;

		Assert.IsFalse(requestTask.IsCompleted);
		Assert.AreEqual(ControllerCommandKind.Stop, runtime.LastRequestedKind);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == "Stop"));
		runtime.ReleaseRequest();
		await requestTask;
		Assert.AreEqual(1, runtime.RequestCount);
		Assert.AreEqual(ControllerState.Heating, controller.Snapshot.State);
	}

	// 목적: awaitable Reset request가 command runtime을 통해서만 흐르고 legacy direct Core Reset을 호출하지 않는지 검증한다.
	// 예상 결과: handler는 controlled completion까지 대기하고 runtime kind는 Reset이며 Core는 Recovery/no Reset event다.
	// 완료 조건: Reset UI request가 Core Recovery policy나 semantic ACK를 우회하지 않는다.
	[TestMethod]
	public async Task ResetRequestedAsync_AwaitsCommandRuntimeWithoutDirectCoreReset()
	{
		var view = new FakeEquipmentView();
		var controller = CreateRecoveryReadyController();
		var runtime = new BlockingCommandObservationRuntime();
		await using var presenter = CreatePresenter(view, controller, runtime, runtime);

		var requestTask = view.RaiseResetRequestedAsync();
		await runtime.RequestStarted;

		Assert.IsFalse(requestTask.IsCompleted);
		Assert.AreEqual(ControllerCommandKind.Reset, runtime.LastRequestedKind);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		Assert.IsEmpty(controller.EventHistory.Where(entry => entry.Event == "Reset"));
		runtime.ReleaseRequest();
		await requestTask;
		Assert.AreEqual(1, runtime.RequestCount);
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
	}

	// 목적: one Presenter command owner가 active Start 동안 Stop/Reset re-entry를 종류와 무관하게 차단하는지 검증한다.
	// 예상 결과: Stop/Reset View task는 추가 runtime request 없이 끝나고 original Start request count만 1이다.
	// 완료 조건: Stop no-preemption과 global command invocation fence가 UI boundary에서도 유지된다.
	[TestMethod]
	public async Task CommandRequestedAsync_WhileAnotherKindIsActive_DoesNotStartSecondOwnedRequest()
	{
		var view = new FakeEquipmentView();
		var runtime = new BlockingCommandObservationRuntime();
		await using var presenter = CreatePresenter(view, CreateController(), runtime, runtime);
		var startTask = view.RaiseStartRequestedAsync();
		await runtime.RequestStarted;

		await view.RaiseStopRequestedAsync();
		await view.RaiseResetRequestedAsync();

		Assert.AreEqual(1, runtime.RequestCount);
		Assert.AreEqual(ControllerCommandKind.Start, runtime.LastRequestedKind);
		runtime.ReleaseRequest();
		await startTask;
	}

	// 목적: Form closing이 active Start/Stop/Reset admission을 먼저 닫고 owned cancellation을 관찰한 뒤 runtime을 한 번 dispose하는지 검증한다.
	// 예상 결과: 각 command cancellation 시 StopAdmission이 이미 호출되고 close 뒤 dispose count 1, late snapshot render 0이다.
	// 완료 조건: 새 async Stop/Reset 경로도 Start와 같은 admission/cancel/join/dispose owner를 사용한다.
	[TestMethod]
	[DataRow(ControllerCommandKind.Start)]
	[DataRow(ControllerCommandKind.Stop)]
	[DataRow(ControllerCommandKind.Reset)]
	public async Task ClosingRequested_StopsAdmissionCancelsAndJoinsActiveCommandWithoutLateRender(
		ControllerCommandKind commandKind)
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new BlockingCommandObservationRuntime(cancellationCooperative: true);
		var presenter = CreatePresenter(view, controller, runtime, runtime);
		var initialRenderCount = view.SnapshotRenderCount;
		try
		{
			var requestTask = commandKind switch
			{
				ControllerCommandKind.Start => view.RaiseStartRequestedAsync(),
				ControllerCommandKind.Stop => view.RaiseStopRequestedAsync(),
				ControllerCommandKind.Reset => view.RaiseResetRequestedAsync(),
				_ => throw new ArgumentOutOfRangeException(nameof(commandKind))
			};
			await runtime.RequestStarted;
			Assert.AreEqual(commandKind, runtime.LastRequestedKind);
			await view.RaiseClosingRequestedAsync();
			await requestTask;

			Assert.IsTrue(runtime.CancellationObservedAfterStopAdmission);
			Assert.AreEqual(1, runtime.DisposeCount);
			Assert.AreEqual(initialRenderCount, view.SnapshotRenderCount);
		}
		finally
		{
			await presenter.DisposeAsync();
		}
	}

	// 목적: cancellation을 무시하는 active command가 실제 settle하기 전 Form closing이 teardown 완료를 거짓 보고하지 않는지 검증한다.
	// 예상 결과: release 전 close Task와 runtime disposal은 미완료이고 release 뒤 dispose 한 번과 no late render가 성립한다.
	// 완료 조건: noncooperative transport ambiguity가 close에서 조기 lease/dispose로 축소되지 않는다.
	[TestMethod]
	public async Task ClosingRequested_NonCooperativeCommandWaitsForActualSettlementBeforeDispose()
	{
		var view = new FakeEquipmentView();
		var controller = CreateController();
		var runtime = new BlockingCommandObservationRuntime(cancellationCooperative: false);
		var presenter = CreatePresenter(view, controller, runtime, runtime);
		var initialRenderCount = view.SnapshotRenderCount;
		try
		{
			var requestTask = view.RaiseStartRequestedAsync();
			await runtime.RequestStarted;
			var closeTask = view.RaiseClosingRequestedAsync();
			await Task.Yield();

			Assert.IsFalse(closeTask.IsCompleted);
			Assert.AreEqual(0, runtime.DisposeCount);
			runtime.ReleaseRequest();
			await Task.WhenAll(requestTask, closeTask);
			Assert.AreEqual(1, runtime.DisposeCount);
			Assert.AreEqual(initialRenderCount, view.SnapshotRenderCount);
		}
		finally
		{
			await presenter.DisposeAsync();
		}
	}

	private static EquipmentPresenter CreatePresenter(
		IEquipmentView view,
		ThermalController controller,
		IEquipmentObservationRuntime observationRuntime,
		IEquipmentCommandRuntime? commandRuntime = null) =>
		new(
			view,
			controller,
			observationRuntime,
			commandRuntime ?? new PassiveCommandRuntime());

	private static ThermalController CreateRecoveryReadyController()
	{
		var controller = CreateController();
		ThermalControllerTestCommands.CompleteStart(controller);
		controller.SetDoorOpen(true);
		controller.SetDoorOpen(false);
		controller.AcknowledgeAlarm();
		Assert.AreEqual(ControllerState.Recovery, controller.Snapshot.State);
		return controller;
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_ = DisposeDuringCycle?.Invoke();
			_cycleStarted.TrySetResult(true);
			return AwaitDisconnectedAsync(_releaseCycle.Task);
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			ReceivedElapsed.Add(elapsed);
			if (ReceivedElapsed.Count == 1)
			{
				_firstCycleStarted.TrySetResult(true);
				await _releaseFirstCycle.Task.WaitAsync(cancellationToken);
			}

			return CreateDisconnectedCycle();
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_cycleStarted.TrySetResult(true);
			await _releaseCycle.Task;
			return CreateDisconnectedCycle();
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			_cycleStarted.TrySetResult(true);
			await _faultCycle.Task;
			return CreateDisconnectedCycle();
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
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
			return CreateDisconnectedCycle();
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public async Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CycleCallCount++;
			_cycleStarted.TrySetResult(true);
			await _releaseCycle.Task.WaitAsync(cancellationToken);
			return CreateDisconnectedCycle();
		}

		public void ReleaseCycle() => _releaseCycle.TrySetResult(true);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PassiveCommandRuntime : IEquipmentCommandRuntime
	{
		public EquipmentCommandLifecycleState CurrentState { get; private set; } = new(
			EquipmentCommandLifecycleDisposition.NoCommand,
			null,
			null,
			null,
			null);

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			Reject(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
			Reject(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
			Reject(cancellationToken);
		public Task<EquipmentCommandRequestResult> RequestAbortAsync(CancellationToken cancellationToken) =>
			Reject(cancellationToken);

		private Task<EquipmentCommandRequestResult> Reject(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CurrentState = new EquipmentCommandLifecycleState(
				EquipmentCommandLifecycleDisposition.AdmissionRejected,
				null,
				null,
				null,
				null,
				false,
				EquipmentCommandRejectionReason.CoreIneligible,
				ControllerCommandKind.Stop);
			return Task.FromResult(new EquipmentCommandRequestResult(
				EquipmentCommandLifecycleDisposition.AdmissionRejected,
				null,
				EquipmentCommandRejectionReason.CoreIneligible));
		}

		public void StopAdmission()
		{
		}
	}

	private sealed class BlockingCommandObservationRuntime : IEquipmentObservationRuntime, IEquipmentCommandRuntime
	{
		private readonly bool _cancellationCooperative;
		private readonly TaskCompletionSource<bool> _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _releaseRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public BlockingCommandObservationRuntime(bool cancellationCooperative = false)
		{
			_cancellationCooperative = cancellationCooperative;
		}

		public Task RequestStarted => _requestStarted.Task;
		public int RequestCount { get; private set; }
		public ControllerCommandKind? LastRequestedKind { get; private set; }
		public int DisposeCount { get; private set; }
		public bool StopAdmissionCalled { get; private set; }
		public bool CancellationObservedAfterStopAdmission { get; private set; }

		public EquipmentCommandLifecycleState CurrentState { get; } = new(
			EquipmentCommandLifecycleDisposition.NoCommand,
			null,
			null,
			null,
			null);
		public void StopAdmission() => StopAdmissionCalled = true;
		public void ReleaseRequest() => _releaseRequest.TrySetResult(true);
		public void SetCurrentTemperature(double currentTemperature) { }
		public void SetSensorHealthy(bool sensorHealthy) { }
		public void SetDoorClosed(bool doorClosed) { }
		public void SuppressNextAcknowledgement() { }
		public void ForceTransportDisconnect() { }
		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken) =>
			Task.FromResult(CreateDisconnectedCycle());

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			RequestCommandAsync(ControllerCommandKind.Start, cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
			RequestCommandAsync(ControllerCommandKind.Stop, cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
			RequestCommandAsync(ControllerCommandKind.Reset, cancellationToken);
		public Task<EquipmentCommandRequestResult> RequestAbortAsync(CancellationToken cancellationToken) =>
			RequestCommandAsync(ControllerCommandKind.Abort, cancellationToken);

		private async Task<EquipmentCommandRequestResult> RequestCommandAsync(
			ControllerCommandKind kind,
			CancellationToken cancellationToken)
		{
			RequestCount++;
			LastRequestedKind = kind;
			_requestStarted.TrySetResult(true);
			if (_cancellationCooperative)
			{
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					CancellationObservedAfterStopAdmission = StopAdmissionCalled;
					throw;
				}
			}

			await _releaseRequest.Task;
			return new EquipmentCommandRequestResult(
				EquipmentCommandLifecycleDisposition.AwaitingAcknowledgement,
				1);
		}

		public ValueTask DisposeAsync()
		{
			DisposeCount++;
			return ValueTask.CompletedTask;
		}
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

		public void SuppressNextAcknowledgement()
		{
		}

		public void ForceTransportDisconnect()
		{
		}

		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(CreateDisconnectedCycle());
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
		public int SuppressAckCount { get; private set; }
		public int ForceDisconnectCount { get; private set; }
		public void SuppressNextAcknowledgement() => SuppressAckCount++;
		public void ForceTransportDisconnect() => ForceDisconnectCount++;

		public void SetSensorHealthy(bool sensorHealthy) => LastSensorHealthy = sensorHealthy;

		public void SetCurrentTemperature(double currentTemperature) => LastRequestedTemperature = currentTemperature;

		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CycleCallCount++;
			LastElapsed = elapsed;
			_controller.ApplyObservation(
				new ThermalObservation(isDoorOpen: false, sensorHealthy: true, currentTemperature: _observedTemperature),
				elapsed);
			return Task.FromResult(CreateCycle(_controller, PlcConnectionState.Connected, ConnectionSynchronizationState.Synchronized, includeInputSnapshot: true));
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}



	private static async Task<EquipmentCommandCycleResult> AwaitDisconnectedAsync(Task gate)
	{
		await gate;
		return CreateDisconnectedCycle();
	}

	private static EquipmentCommandCycleResult CreateDisconnectedCycle() =>
		CreateCycle(
			new ThermalController(new Recipe("Standard", 250, 300), SimulationSettings.Illustrative),
			PlcConnectionState.Disconnected,
			ConnectionSynchronizationState.WaitingForFreshInput);

	private static EquipmentCommandCycleResult CreateCycle(
		ThermalController controller,
		PlcConnectionState connectionState,
		ConnectionSynchronizationState synchronizationState,
		EquipmentCommandLifecycleDisposition commandDisposition = EquipmentCommandLifecycleDisposition.NoCommand,
		long? commandId = null,
		bool includeInputSnapshot = false) =>
		new(
			new EquipmentCycleResult(
				EquipmentCycleDisposition.Completed,
				controller.Snapshot,
				connectionState,
				includeInputSnapshot
					? new PlcInputSnapshot(
						doorClosed: true,
						sensorHealthy: true,
						currentTemperature: controller.Snapshot.CurrentTemperature,
						machineState: PlcMachineState.Idle,
						acknowledgedCommandId: 0,
						observationSequence: 1,
						sourceTransportIncarnation: new PlcSourceTransportIncarnation(Guid.Parse("11111111-1111-1111-1111-111111111111")),
						heaterEnabled: false)
					: null,
				synchronizationState,
				0,
				ReconnectFailureKind.None),
			commandDisposition,
			commandId);


	private sealed class StatusObservationRuntime : IEquipmentObservationRuntime, IEquipmentCommandRuntime
	{
		private readonly ThermalController _controller;
		private readonly PlcConnectionState _connectionState;
		private readonly ConnectionSynchronizationState _synchronizationState;
		private readonly EquipmentCommandLifecycleDisposition _disposition;
		private readonly long? _commandId;
		private readonly ControllerCommandKind? _kind;

		public StatusObservationRuntime(
			ThermalController controller,
			PlcConnectionState connectionState,
			ConnectionSynchronizationState synchronizationState,
			EquipmentCommandLifecycleDisposition disposition,
			long? commandId,
			ControllerCommandKind? kind)
		{
			_controller = controller;
			_connectionState = connectionState;
			_synchronizationState = synchronizationState;
			_disposition = disposition;
			_commandId = commandId;
			_kind = kind;
		}

		public EquipmentCommandLifecycleState CurrentState => new(
			_disposition,
			_commandId,
			_kind,
			null,
			null);

		public void SetCurrentTemperature(double currentTemperature) { }
		public void SetSensorHealthy(bool sensorHealthy) { }
		public void SetDoorClosed(bool doorClosed) { }
		public void SuppressNextAcknowledgement() { }
		public void ForceTransportDisconnect() { }
		public void StopAdmission() { }

		public Task<EquipmentCommandCycleResult> CycleAsync(TimeSpan elapsed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(CreateCycle(_controller, _connectionState, _synchronizationState, _disposition, _commandId));
		}

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			Task.FromResult(new EquipmentCommandRequestResult(_disposition, _commandId));
		public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
			RequestStartAsync(cancellationToken);
		public Task<EquipmentCommandRequestResult> RequestAbortAsync(CancellationToken cancellationToken) =>
			RequestStartAsync(cancellationToken);
		public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
			RequestStartAsync(cancellationToken);
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class FakeEquipmentView : IEquipmentView
	{
		public event Func<Task>? StartRequested;
		public event Func<Task>? StopRequested;
		public event Func<Task>? AbortRequested;
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

		public double SimulatedTemperature { get; set; }
		public IReadOnlyList<Recipe> RecipeOptions { get; private set; } = [];
		public ControllerSnapshot? LastSnapshot { get; private set; }
		public int SnapshotRenderCount { get; private set; }
		public IReadOnlyList<EventLogEntry> LastEventLog { get; private set; } = [];

		public async Task RaiseStartRequestedAsync()
		{
			if (StartRequested is null)
			{
				return;
			}

			foreach (var handler in StartRequested.GetInvocationList().Cast<Func<Task>>())
			{
				await handler();
			}
		}
		public Task RaiseStopRequestedAsync() => RaiseCommandRequestedAsync(StopRequested);
		public void RaiseAcknowledgeRequested() => AcknowledgeRequested?.Invoke(this, EventArgs.Empty);
		public Task RaiseResetRequestedAsync() => RaiseCommandRequestedAsync(ResetRequested);
		public Task RaiseAbortRequestedAsync() => RaiseCommandRequestedAsync(AbortRequested);

		private static async Task RaiseCommandRequestedAsync(Func<Task>? requested)
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
		public void RaiseDoorToggleRequested() => DoorToggleRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseApplyTemperatureRequested() => ApplyTemperatureRequested?.Invoke(this, EventArgs.Empty);
		public void RaisePauseFeedbackRequested() => PauseFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseResumeFeedbackRequested() => ResumeFeedbackRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseSuppressNextAcknowledgementRequested() => SuppressNextAcknowledgementRequested?.Invoke(this, EventArgs.Empty);
		public void RaiseForceTransportDisconnectRequested() => ForceTransportDisconnectRequested?.Invoke(this, EventArgs.Empty);
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

		public EquipmentStatusViewModel? LastStatus { get; private set; }

		public void ShowSnapshot(ControllerSnapshot snapshot)
		{
			LastSnapshot = snapshot;
			SnapshotRenderCount++;
		}

		public void ShowEquipmentStatus(EquipmentStatusViewModel status) => LastStatus = status;

		public int EventLogRenderCount { get; private set; }

		public void ShowEventLog(IReadOnlyList<EventLogEntry> entries)
		{
			EventLogRenderCount++;
			LastEventLog = entries.ToArray();
		}
	}
}
