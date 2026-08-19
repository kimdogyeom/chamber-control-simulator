# P5-T2 bounded observation reconnect verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `ca68f6696a0df9b37dff2ccab7d9200f73a343bd`
- Baseline/parent: `96a24831270096a0068f48fbacc93342c766a7ad`
- Source tree: `7e59e6c8dd6e103117a93c90fe41c50750268a8a`
- Subject: `feat: add bounded PLC reconnect policy`
- Evidence state: Completed — source commit and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `ca68f66`와 P5-T2를 `Completed` 상태로 bound한다. 아래 evidence는 abstract observation-port bounded reconnect만 설명하며 strict synchronization, alarm recovery, UI/device/safety 또는 publication 완료를 뜻하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`
2. `ChamberControlSimulator.Application.Tests/EquipmentCoordinatorTests.cs`
3. `ChamberControlSimulator.Application/EquipmentCommandRuntime.cs`
4. `ChamberControlSimulator.Application/EquipmentCoordinator.cs`
5. `ChamberControlSimulator.Application/ReconnectPolicy.cs`

Source commit에는 위 다섯 경로만 포함된다. Core, PLC abstractions/simulation, Presentation, WinForms, project, documentation, roadmap, protocol, image 경로는 포함되지 않는다.

## Implemented contract

### Observation-only reconnect epoch

`EquipmentCoordinator`가 `IPlcObservationPort` reconnect policy를 소유한다. injected `TimeProvider`와 fixed `ReconnectPolicy.Conservative`는 attempt 1 전 **250 ms**, 첫 confirmed failure 뒤 attempt 2 전 **+500 ms**, 둘째 confirmed failure 뒤 attempt 3 전 **+1 s**를 요구하며 maximum attempt count는 정확히 3이다.

- active safety-monitored `ReadInputsAsync`의 confirmed typed `PlcTransportException`은 P5-T1대로 Core `CommunicationLost`를 보고하고 reconnect epoch를 연다.
- fault를 확인한 같은 cycle은 `TransportFailed`, `WaitingForReconnect`, attempt count 0을 반환하고 `ConnectAsync`를 호출하지 않는다.
- due attempt 직전에 actual observation-port state를 다시 확인한다. `Disconnected` 또는 `Faulted`일 때만 `ConnectAsync`를 호출하며, 외부에서 이미 `Connected`가 된 port에는 stale한 사전 상태로 connect하지 않는다.
- reconnect-success 직후 typed read fault는 같은 epoch의 consumed attempt count를 보존하고 다음 delay를 그 failure timestamp에서 다시 시작한다.
- 세 번째 consumed attempt 뒤 typed read fault는 같은 cycle에서 즉시 `ReconnectExhausted`/count 3이 되며 추가 timestamp 조회, 네 번째 connect/read/write를 수행하지 않는다.

`EquipmentCycleResult`는 `ConnectionSynchronizationState`, reconnect attempt count, `ReconnectFailureKind`만 노출한다. raw exception object, credential, endpoint 또는 secret metadata는 노출하지 않는다. 이 visibility는 P5-T2 epoch evidence이며 P5-T3 strict source-incarnation/fresh-watermark synchronization을 구현하거나 증명하지 않는다.

### Non-overlap and terminal behavior

`CycleAsync`는 active connect/read cycle과 겹치면 기다리거나 queue하지 않고 `SkippedBusy`를 반환한다. pending `ConnectAsync`는 하나뿐이며 skipped cycle은 connect/read/write count를 늘리지 않는다.

- `ConnectAsync`의 typed transport failure는 P5-T1 경계대로 non-alarm `TransportFailed`다.
- reconnect due/failure timestamp를 얻는 `TimeProvider` 또는 policy-time exception은 communication alarm이 아니다. metadata를 fail-closed로 남기고 원래 exception을 전파한다.
- due reconnect `ConnectAsync`의 cancellation은 원래 cancellation을 전파하고 epoch를 `ReconnectExhausted` + `Canceled` metadata로 terminalize한다. 같은 시각이나 이후 시각의 uncanceled cycle도 reconnect를 replay하지 않는다.
- 세 attempt exhaustion 뒤 later cycle은 connection state와 관계없이 PLC I/O, alarm/recovery mutation 또는 reschedule을 수행하지 않는다.

### Output-fault boundary and preserved holds

confirmed typed output write fault는 P5-T1 `CommunicationLost`와 P4 command ID/kind, terminal reconciliation/timeout evidence, closed admission, one-write/no-retry/no-replay fence를 유지한다. `EquipmentCommandRuntime`은 observation synchronization만 invalidate한다.

Observation port와 output port는 distinct object일 수 있다. output fault 뒤 actual observation port가 계속 `Connected`이면 coordinator는 observation reconnect를 추론하거나 `ConnectAsync`를 호출하지 않는다. P5-T2는 output command 생성, retry/replay, command admission release, semantic ACK consumption, Reset 또는 Recovery를 추가하지 않는다.

## TDD and Windows validation

### Expected RED evidence

- **R1 isolated Windows replay:** pre-repair epoch-reset behavior를 temporary replay했을 때 `CycleAsync_WhenReconnectConnectSucceedsButReadFaults_PreservesAttemptBackoffAndCap`은 first reconnect-success/read-fault count가 expected 1 대신 actual 0이어서 expected RED였다. Final source는 count와 500 ms/1 s progression을 보존한다.
- **R2 isolated Windows replay:** pre-repair cancellation behavior를 temporary replay했을 때 `CycleAsync_WhenInFlightReconnectConnectIsCanceled_TerminatesEpochWithoutReplay`은 expected `ReconnectExhausted` 대신 actual `WaitingForReconnect`를 반환해 expected RED였다. Final source는 `Canceled` terminal metadata와 no replay를 보존한다.
- **R3 RED 1:** third reconnect-success/read-fault assertion은 expected `ReconnectExhausted` 대신 actual `WaitingForReconnect`를 관찰했다.
- **R3 RED 2:** `CycleAsync_WhenThirdReconnectReadFaults_ExhaustsWithoutPostFaultTimestampQuery`은 old behavior의 `BeginReconnectEpoch()`에서 controlled `InvalidOperationException` (`forbidden post-third-read-fault timestamp query`)을 throw해 0/1로 expected RED였다.

R1/R2 replay와 R3 RED용 temporary old behavior는 final source bytes에 남지 않았다. 각 repair 뒤 focused GREEN을 확인했고 final frozen candidate에서 다음 Windows validation을 통과했다.

- PLC abstractions: **19/19 passed**
- Core: **40/40 passed**
- Application: **69/69 passed**
- Presentation: **26/26 passed**
- PLC simulation: **20/20 passed**
- Full solution: **174/174 passed**
- Debug solution build: **0 warnings / 0 errors**
- Source `git diff --check` before commit: **passed**
- Final independent frozen-byte code review: **PASS**
- Final independent frozen-byte test/spec review: **PASS**
- Final source manifest SHA-256: `5ad1b6979107b838f34bc839c07b2a185f1763c46194f76e17debbf35810864e`

Named final regressions include:

- `ReconnectPolicy_WhenConfigurationIsInvalid_RejectsOutsideBoundedThreeAttemptSchedule`
- `CycleAsync_WhenConnectCycleOverlaps_ReturnsSkippedBusyWithoutPlcWork`
- `CycleAsync_AfterReadFault_AttemptsReconnectOnlyAtBoundariesAndStopsAfterThreeFailures`
- `CycleAsync_WhenReconnectFailureIsDelayed_SchedulesNextDelayFromFailureTime`
- `CycleAsync_WhenReconnectConnectSucceedsButReadFaults_PreservesAttemptBackoffAndCap`
- `CycleAsync_WhenThirdReconnectReadFaults_ExhaustsWithoutPostFaultTimestampQuery`
- `CycleAsync_WhenInFlightReconnectConnectIsCanceled_TerminatesEpochWithoutReplay`
- `CycleAsync_WhenReconnectTimeProviderThrows_PropagatesWithoutConnectOrNewAlarm`
- `CycleAsync_WhenConnectThrowsTransportException_ReturnsNonAlarmTransportFailure`
- `RequestStopAsync_DistinctOutputFault_InvalidatesSynchronizationWithoutInventedObservationReconnect`
- `CycleAsync_ReconnectAfterAcknowledgementTimeout_CannotClearHoldOrResend`

## Reproduction

Windows repository root에서 source commit `ca68f6696a0df9b37dff2ccab7d9200f73a343bd`를 checkout한 뒤 실행한다.

```powershell
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

## Explicit nonclaims

이 source-SHA-bound documentation checkpoint는 다음을 주장하지 않는다.

- P5-T3 strict source-incarnation/session identity, post-fault fresh watermark 또는 stale pre-incarnation input rejection
- P5-T4 qualified fresh-safe-input evidence, post-evidence acknowledgement, `CommunicationLost` clearing, Recovery-ready 또는 Reset 성공
- P5-T5 `CommunicationLost`와 다른 alarm cause의 composite precedence/clear/completion
- Core alarm clearing, command uncertainty clearing, admission release, output retry/replay 또는 automatic compensating output
- Presentation/WinForms UI runtime, connection badge, Alarm rendering, Event Log/UI expansion, current screenshot 또는 operator smoke
- Modbus/TCP adapter/protocol, real PLC/device/equipment/chamber/sensor/heater/actuator behavior
- E-Stop, Safety PLC, hardware safety circuit, device/human/physical safety validation
- push, tag, release 또는 publication
