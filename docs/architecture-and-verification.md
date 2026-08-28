# 아키텍처와 검증 기록

## 1. 문서 상태와 증거 경계

이 문서는 `2026-08-19` Windows authoritative repository를 기준으로 작성한 architecture/progress record다. 아래 네 상태를 섞지 않는다.

| evidence state | 의미 |
| --- | --- |
| Completed | source local commit, source-bound 자동 검증, frozen review, tracked documentation checkpoint가 모두 있다. |
| In progress — reviewed but uncommitted | source·test·review 후보는 있으나 아직 local commit이 없다. |
| Planned | roadmap만 존재하며 source가 없다. |
| Historical baseline | 과거 source SHA와 연결된 화면 또는 test evidence다. 현재 wiring의 증거가 아니다. |

P3-T2 atomic observation mapping의 source anchor는 `3a7398d` (`feat: map PLC observations atomically`)다. focused Core 3/3, focused Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte independent source review를 통과했다. 뒤따르는 documentation checkpoint는 의도적으로 별도 commit으로 남긴다.

P3-T3 Core plant simulation separation의 source anchor는 `b949e6c` (`feat: separate Core plant temperature policy`)다. focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66, Windows-byte independent source review를 통과했다. 이 문서와 P3-T3 verification receipt도 source commit 뒤의 별도 documentation checkpoint로 남긴다.

P3-T4 WinForms observation composition의 implementation source anchor는 `2e502fa` (`feat: compose WinForms observation runtime`)다. concrete P3 input facade, non-overlapping observation cycle, close teardown, P3/P4 capability boundary를 포함하며 Debug build 0 warnings/0 errors, full regression 80/80, Windows-byte independent architecture/lifecycle review PASS와 artifact-integrity review PASS를 통과했다. current solution baseline `9c3ad95`에서 user-driven Session 1 manual smoke는 observed input `20 → 30 → Apply` 후 `30.00 °C` rendering, Idle 유지, 오류 없음을 보고했고 UI 종료 후 application process absence를 확인했다.

P4-T1 command reservation/admission의 source anchor는 `8f32ce7` (`feat: reserve command admission before dispatch`)다. opaque Core reservation, no-transition admission, one retained Application pending command, positive monotonic process-local IDs, invalidation-retained duplicate fence를 포함하며 focused Core 7/7, focused Application 5/5, Debug build 0 warnings/0 errors, full regression 92/92, Windows-byte final source review PASS를 통과했다.

P4-T2 narrow output/transport receipt의 source anchor는 `254c546` (`feat: dispatch pending command through output port`)다. `IPlcOutputPort` 분리, empty `IPlcClient` composite, retained pending command one-shot dispatch, exact matching `Written`의 ACK-wait classification, mismatch/`Failed`/throw/cancel delivery-indeterminate fence를 포함하며 focused Abstractions 19/19, Application 14/14, Debug 0 warnings/0 errors, full 96/96, frozen Windows-byte review PASS를 통과했다. 이 evidence는 semantic ACK, Core completion, timeout/reconciliation, UI/Program/Presenter routing, Virtual PLC semantic effect를 증명하지 않는다.

P4-T3 Start-only exact fresh semantic ACK의 source anchor는 `7a874e8` (`feat: complete Start on exact fresh PLC acknowledgement`)다. internal/friend Core completion, latest accepted P3 baseline, ACK high-water ID allocation, shared P3/P4 I/O serialization, strictly later exact ACK completion, terminal ambiguity holds, exact Virtual semantic-time slicing을 포함한다. Core 36/36, Application 24/24, Simulation 16/16, Debug 0 warnings/0 errors, full 114/114를 통과했다. initial frozen source review의 semantic-time partition blocker와 ambiguity-test advisory를 repair한 v2 generation이 independent PASS를 받았다.

P4-T4 monotonic lifecycle hold와 awaited Start/close ownership의 primary source anchor는 `0e2f6d2` (`feat: enforce monotonic command lifecycle holds`), late physical-write diagnostic repair는 `cdbca25` (`fix: preserve late write failure evidence`)다. injected Application `TimeProvider`, exact 3초 receipt/ACK epochs, explicit timeout state, noncooperative write lease transfer, stable terminal evidence, separate Presentation command/observation capabilities, awaited Start와 close task ownership을 포함한다. final Application 36/36, Simulation 16/16, Presentation 21/21, Debug 0 warnings/0 errors, full 128/128를 통과했다. initial source-review blockers와 mandatory cleaner late-fault blocker를 각각 repair했고 final cleaner, cumulative architect review, completion red-team이 모두 PASS했다.

P4-T5 complete command-family source anchor는 `8127888` (`feat: complete P4 command family lifecycle`)다. 이 commit은 superseded `c320a09`와 동일한 parent/tree/source bytes를 보존하면서 plan §3.5 factual Lore만 보강한 current authority다. Start/Stop/Reset named wrappers, one private Application lifecycle, success-only global-fence release, exact fresh per-kind ACK, Recovery-gated Reset, virtual Stop heater-off/Reset plant-neutral semantics, awaited three-command Presentation ownership을 포함한다. Debug 0 warnings/0 errors, Core 39/39, Application 49/49, Simulation 20/20, Presentation 26/26, Abstractions 19/19, full 153/153를 통과했다. initial QA가 지적한 per-kind stale/nonfresh/mismatched ACK, Core-ineligible, ACK-timeout, close-owner coverage를 source에서 보강했고 repaired cleaner PASS, architect CLEAR/APPROVE, QA red-team PASS를 받았다.

Final P4 consistency audit는 tracked docs HEAD `62a675a`/tree `8b9b61d2`에서 T1–T5의 12-commit source/docs lineage, 다섯 receipt, historical P0 demo inventory, ignored continuity, source/evidence hash chain을 대조했다. 같은 baseline에서 Windows Debug 0 warnings/0 errors와 full 153/153를 재실행했고 immutable closure generation `9ed3d38217bc49753f3daf2361db2e81d1ba8713a540535950754e16a8c044ad`에 cleaner PASS, architect CLEAR/APPROVE, QA red-team PASS가 합류했다. 이 audit은 source behavior를 추가하지 않으며 상세 authority와 held scope는 [`p4-final-consistency-closure.md`](verification/p4-final-consistency-closure.md)에 기록한다.

P5-T1 confirmed typed communication-loss alarm의 source anchor는 `8fabaeb` (`feat: record PLC communication-loss alarms`), baseline은 `ef01e09`다. active safety-monitored read/write의 `PlcTransportException`만 Core `CommunicationLost`로 보고하고, Core pending-alarm progression hold 및 P4 command fences를 유지한다. Debug build 0 warnings/0 errors, Abstractions 19/19, Core 40/40, Presentation 26/26, Application 56/56, Simulation 20/20, full 161/161, final frozen-byte code와 test/spec review PASS를 통과했다. review manifest는 `40c624dc133a00c3f88a93531b6a9b23a8215d7901f5cca2d80e91c52108f706`다. tracked documentation checkpoint가 이 source-bound evidence를 기록한다.
P5-T2 bounded observation reconnect의 source anchor는 `ca68f66` (`feat: add bounded PLC reconnect policy`), parent는 `96a2483`다. `EquipmentCoordinator` 아래 observation-only epoch, injected `TimeProvider`, fixed 250 ms → +500 ms → +1 s / maximum three-attempt policy, non-queueing `SkippedBusy`, visible non-secret metadata, terminal exhaustion/cancellation, distinct observation/output-port boundary를 포함한다. Debug build 0 warnings/0 errors, Abstractions 19/19, Core 40/40, Application 69/69, Presentation 26/26, Simulation 20/20, full 174/174, final frozen-byte code 및 test/spec reviews PASS를 통과했고 source manifest는 `5ad1b6979107b838f34bc839c07b2a185f1763c46194f76e17debbf35810864e`다. 이 tracked documentation checkpoint가 source-bound evidence를 기록하여 P5-T2를 `Completed`로 표시한다.

## 2. 범위와 안전 한계

이 프로젝트는 C# WinForms 기반의 **가상 열처리 챔버 제어 시뮬레이터**다. 실제 챔버, 생산 PLC, 산업 통신, 온도 센서, 히터와 연결하지 않는다. 수치와 fault는 설명·테스트를 위한 illustrative simulation 값이다.

PC application의 Door/temperature/sensor interlock은 software policy demonstration이다. E-Stop, Safety PLC, hardware safety circuit, human safety를 보장하거나 대체한다고 주장하지 않는다.

## 3. 구현 상태

| 범위 | 상태 | source / verification evidence |
| --- | --- | --- |
| P0 baseline UI/Core | Completed | `1497a06`, `40716fa`; 27 tests, app-only IDLE baseline capture |
| P1 PLC contracts | Completed | `587b519`까지; full regression 48/48 |
| P2 Virtual PLC | Completed | `1935c5f`, `64eb20d`; full regression 59/59, two independent reviews PASS |
| P3-T1 read-only coordinator | Completed | `54e8303`; full regression 61/61, independent review PASS |
| P3-T2 atomic observation mapping | Completed | `3a7398d`; focused Core 3/3, Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65; manifest `1f65b461e9a08e08f0559b9018f2af27a6e600decf53114c756221283f42a090` PASS |
| P3-T3 plant simulation 분리 | Completed | `b949e6c`; focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66; manifest `e7e94da9061b06428e9370c3fdbf1af28a28e2d5f451070d8de0e292039f540f` PASS |
| P3-T4 WinForms observation composition | Completed | implementation `2e502fa`; current solution baseline `9c3ad95`; P3-only concrete input facade, async cycle/close teardown, 80/80, two independent reviews PASS; user-driven Session 1 input-to-render/close smoke completed |
| P4 command lifecycle | P4-T1–P4-T5 Completed; final consistency closure reviewed | reservation/admission `8f32ce7`; output/receipt `254c546`; Start exact ACK `7a874e8`; monotonic holds `0e2f6d2` + diagnostic repair `cdbca25`; complete Start/Stop/Reset family `8127888`; final audit at docs `62a675a`, Debug 0/0, full 153/153, one-hash closure cleaner/architect/QA PASS |
| P5-T1 confirmed typed communication-loss alarm | Completed | `8fabaeb`; active typed read/write failure → Core `CommunicationLost`; Debug 0/0; full 161/161; frozen code/test-spec reviews PASS; [receipt](verification/p5-t1-communication-lost.md) |
| P5-T2 bounded observation reconnect | Completed | `ca68f66` (parent `96a2483`); observation-only epoch, fixed three-attempt backoff, `SkippedBusy`, terminal failure metadata; Debug 0/0, Application 69/69, full 174/174, final frozen-byte reviews PASS; [P5-T2 receipt](verification/p5-t2-bounded-reconnect.md) |
| P5-T3 source-backed connection synchronization | Completed | `fc37338` (repair `7a2ceec`, parent `ef16772`); source incarnation/fresh watermark, no alarm clear; Debug 0/0, full 180/180, manifest `385059c126795da79972fb1564572bfa7193291d50c680a6e8fdbfb046f67442`; [P5-T3 receipt](verification/p5-t3-source-synchronization.md) |
| P5-T4 fresh-safe CommunicationLost recovery | Completed | `00a1df2` (parent `d2e9f0c`); safe evidence + new Acknowledge → Recovery-ready; Reset not invoked; Debug 184/184, manifest `5900dd7f62240513a7e39160ec307cbecb9260ccad6102997105343c5f95b0f4`; [P5-T4 receipt](verification/p5-t4-fresh-safe-recovery.md) |
| P5-T5 composite CommunicationLost precedence | Completed | `ee89095`; DoorOpen/OT pending and P4 hold block Recovery/Reset; Debug 187/187, manifest `9a48bd4af9b3000eed4e9dee5abc6333b42b05cdd696923d2a6749e27ca3bf76`; [P5-T5 receipt](verification/p5-t5-composite-alarms.md) |
| P6-T1 connection/command/synchronization status rendering | Completed | `ad7e5fc`; display-only mapping; Debug 188/188, manifest `89f1b835e7f8cfc379ffbc544f0dd110e57a0ea4137e2b12cf7041964065c37c`; [P6-T1 receipt](verification/p6-t1-status-rendering.md) |
| P6-T2 simulation / fault-injection chrome | Completed | `e8f6a28`; operator vs Simulation / Fault Injection; Debug 189/189, manifest `d52a202fc7aafcb211f316900420c69d3a519c0d034988daa31650ecfed7697f`; [P6-T2 receipt](verification/p6-t2-simulation-chrome.md) |
| P6-T3 event-log connection/command columns | Planned | Event Log extra columns are not this receipt |

각 P3/P4/P5/P6-T1/T2 자동 검증 수치는 해당 source SHA와 verification receipt에만 bound된다. P6-T2 수치를 P6-T3, P7 captures, production release 또는 safety claim으로 확장하지 않는다.

## 4. 현재 실행 경계

### 4.1 P0 UI baseline — historical runtime path

직접 `Form1 → EquipmentPresenter → ThermalController` wiring과 screenshots는 P3-T4 이전 P0 baseline이다. `docs/demo/SCENARIOS.md`는 그 historical procedure를 보존하며 current observation composition 또는 current UI screenshot evidence가 아니다.

### 4.2 P3-T4-derived current WinForms observation composition

```text
Form1 / IEquipmentView
  → EquipmentPresenter
  ├→ IEquipmentObservationRuntime → EquipmentCoordinator(IPlcObservationPort)
  └→ IEquipmentCommandRuntime → EquipmentCommandRuntime(IPlcOutputPort)
       → one shared P3/P4 semaphore + monotonic lifecycle state
  → ThermalObservation / exact Start·Stop·Reset ACK / ControllerSnapshot rendering
```

`Program.CreateObservationRuntime(...)`은 concrete `EquipmentObservationRuntime` owner와 narrow `VirtualPlcObservationInputControl`을 구성한다. owner는 observation-only `IEquipmentObservationRuntime`과 named Start/Stop/Reset + admission-stop `IEquipmentCommandRuntime`을 따로 구현하고 Presenter는 두 interface reference를 분리해 받는다. input facade에는 `Advance`, ACK suppression, transport fault API가 없다.

### 4.3 P1/P2/P3 Application boundary

```text
EquipmentCoordinator (Application)
  ├→ ThermalController (Core safety/process authority)
  └→ IPlcObservationPort (connect/disconnect/read only)
       └→ VirtualPlcClient (implemented)

EquipmentCommandCoordinator
  ├→ ThermalController (reservation authority)
  └→ IPlcOutputPort (write only)

IPlcClient : IPlcObservationPort, IPlcOutputPort
  └→ empty compatibility composite
```

`ChamberControlSimulator.Application`은 `Core`와 `Plc.Abstractions`만 reference한다. `Plc.Simulation`, WinForms, Modbus protocol package를 reference하지 않는다. 구체 adapter 선택은 composition root의 책임이다.

### 4.4 P4-T1 reservation/admission boundary

```text
ThermalController.TryReserveCommand(...)
  → opaque ControllerCommandReservation
  → EquipmentCommandCoordinator.TryAdmit(...)
  → positive process-local monotonic CommandId
  → one retained pending admission
```

Admission은 Core state/event transition이 아니며 PLC output capability도 아니다. P4-T1 source `8f32ce7`에서 `EquipmentCommandCoordinator`는 `ThermalController`만 받았다. current P4-T2 source는 별도 `IPlcOutputPort`를 추가하지만 reservation이 invalidated되거나 delivery가 indeterminate여도 held fence를 유지하고 duplicate Start/Stop/Reset을 queue·replace·release하지 않는다.

P4-T1 reservation/admission, P4-T2 narrow output/transport receipt, P4-T3 exact fresh Start ACK, P4-T4 monotonic lifecycle holds, P4-T5 complete Start/Stop/Reset family and awaited Presentation ownership: implemented.

### 4.5 P4-T2 narrow output and transport-receipt boundary

```text
TryAdmit(kind)
  → retained pending admission + opaque Core reservation
DispatchPendingAsync(token)
  → claim dispatch-started under coordinator gate
  → map kind exhaustively
  → await IPlcOutputPort.WriteOutputsAsync outside lock
  → exact matching Written: AwaitingAcknowledgement
  → mismatch / Failed: DeliveryIndeterminate hold
```

Written은 completed/successful semantic result가 아니다. P4-T4에서 timely exact `Written`만 ACK epoch를 시작하고, timeout/mismatch/`Failed`/exception/cancellation 어느 branch도 Core reservation을 적용·해제·교체하거나 새 ID/write를 허용하지 않는다.

### 4.6 P4-T3 exact fresh Start semantic ACK boundary

```text
EquipmentCommandRuntime.RequestStartAsync(token)
  → latest Completed P3 result + non-null snapshot
  → TryAdmitAfter(Start, observed ACK)
  → DispatchPendingAsync(token)
  → Written: AwaitingAcknowledgement

EquipmentCommandRuntime.CycleAsync(elapsed, token)
  → same SemaphoreSlim as write path
  → P3 accepted observation mapping first
  → sequence > pre-dispatch sequence + ACK == pending ID
  → internal Core revalidation / one-shot Start
```

`ThermalController.TryCompleteAcknowledgedCommand(...)`는 `Application`과 `Core.Tests` friend assembly에만 보이는 internal seam이다. exact owned active reservation, invalidation, current eligibility를 Core가 확인한다. unsafe exact ACK는 `AcknowledgedButCoreIneligible`, higher/wrong ACK와 write ambiguity는 `ReconciliationRequired` terminal hold다. lower/stale ACK는 대기하고 어느 hold도 retry, replay, release, retroactive completion을 수행하지 않는다.

Virtual Start는 transport write 때가 아니라 delayed semantic timestamp에 적용된다. `Advance`는 due event까지 plant를 integrate하고 semantic command를 적용한 뒤 remaining interval을 integrate하므로 one-step overshoot와 equivalent split step이 같다. suppression은 observed ACK만 숨긴다. T3 source는 Start-only/non-UI였고 T4 source가 deadline과 awaited UI Start/close containment를 추가했다. Current P4-T5는 같은 lifecycle을 Stop/Reset과 awaited three-command Presentation path로 확장했다.

### 4.7 P4-T4 monotonic lifecycle and Presentation ownership boundary

```text
write invocation timestamp
  → Writing
  → elapsed >= 3s: ReceiptTimedOut (exact tie included)
  → timely exact Written: acknowledgement timestamp
  → elapsed >= 3s: AcknowledgementTimedOut

Form closing
  → StopAdmission
  → cancel command + observation lifetime
  → join active command and cycle
  → dispose one concrete owner once; no late render
```

`TimeProvider.GetTimestamp/GetElapsedTime`만 deadline authority다. receipt deadline은 output invocation, ACK deadline은 timely exact matching `Written`에서 시작하며 admission/Core/wall clock에 들어가지 않는다. timeout/cancellation 당시 physical write가 settle하지 않았으면 gate release를 continuation에 양도해 P3 read와 later write를 계속 막는다. eventual receipt, late exact ACK, reconnect observation은 evidence일 뿐 terminal state를 revive하지 않는다. late physical fault는 `TraceError`에 기록되고 settlement `finally`에서만 gate가 풀린다.

Presentation capability는 `IEquipmentObservationRuntime`과 `IEquipmentCommandRuntime`으로 분리된다. P4-T5에서 Start/Stop/Reset event 모두 one awaitable command-owner path를 사용하며 legacy direct Core Stop/Reset routing은 제거됐다. Acknowledge와 local simulation input은 command output authority가 아니다.

### 4.8 P4-T5 complete command-family boundary

```text
named Start / Stop / Reset request
  -> one private RequestCommandAsync(kind)
  -> one coordinator pending reservation + one output write
  -> exact matching Written (transport evidence only)
  -> strictly later exact ACK
  -> opaque Core revalidation
  -> success-only global-fence release
```

Stop은 priority/preemption 예외가 아니며 pending/timeout/reconciliation/Core-ineligible command가 있으면 세 kind 모두 새 ID/write 없이 거절된다. successful completion만 coordinator reservation과 runtime active fields를 지우고 `Completed` terminal evidence는 유지한다.

Virtual semantic time에서 Start는 heater-on, Stop은 heater-off를 적용한 뒤 optional ACK publication을 처리한다. 따라서 suppressed Stop ACK는 no-effect proof가 아니다. Reset은 command/ACK만 모델링하며 plant, alarm, safety, reconciliation state를 지우는 shortcut이 없다.

Recovery-ready 이전 Reset은 Core reservation 단계에서 거절돼 write가 없다. stale/lower/same-sequence ACK는 completion authority가 아니어서 대기를 유지한다. dispatch 뒤 eligibility invalidation, higher/mismatched reconciliation, exact 3초 ACK timeout, timeout 뒤 delayed exact ACK는 fail-closed terminal hold다.

### 4.9 P5-T1 confirmed typed communication-loss boundary

```text
active safety-monitored I/O
  → typed PlcTransportException
  → Application reports Core CommunicationLost
  → Core Alarm + pending cause + progression hold
  → Stop/Reset cannot bypass
```

`EquipmentCoordinator`는 `ConnectAsync`와 `ReadInputsAsync`를 구분한다. connect 시도의 typed failure는 non-alarm `TransportFailed`이고, active read의 typed failure만 `ReportCommunicationLost()`로 매핑한다. `Faulted` observation port도 자동 reconnect 없이 read 1회를 수행해 같은 typed classification에 도달한다.

`EquipmentCommandRuntime`은 write가 실제 시작된 뒤의 typed failure를 매핑한다. receipt timeout 뒤 늦게 settle한 typed failure와 exact-deadline typed failure도 alarm을 보고하지만, P4 `ReceiptTimedOut`, 원래 command ID/kind, closed admission, one-write/no-retry/no-replay fence를 변경하지 않는다. write 이전 `TimeProvider` failure는 communication alarm이 아니다.

P5-T1 source `8fabaeb`의 `CommunicationLost`에는 clearing/reconnect/synchronization authority가 없었다. P5-T2는 bounded observation reconnect만 추가했다. P5-T3는 source-incarnation/fresh-watermark synchronization만 추가했다. Current P5-T4는 동기화된 안전 증거와 새 Acknowledge로 Recovery-ready만 만들고 Reset을 호출하지 않는다. P5-T5는 DoorOpen/OverTemperature pending과 미해결 P4 hold가 통신 증거만으로 Recovery/Reset을 통과하지 못하게 한다. Presentation/UI runtime, Event Log/UI 확장은 이 source slice에서 검증하지 않았다.

### 4.10 P5-T2 bounded observation reconnect boundary — completed

```text
confirmed active observation read fault
  → Core CommunicationLost + one reconnect epoch
  → same-cycle reconnect 없음
  → 250 ms → +500 ms → +1 s under TimeProvider
  → maximum three attempts / terminal ReconnectExhausted
```

`EquipmentCoordinator`의 epoch는 `IPlcObservationPort`만 다룬다. due attempt 직전에 actual `ConnectionState`를 다시 읽고 `Disconnected` 또는 `Faulted`일 때만 `ConnectAsync`를 호출한다. pending connect를 포함한 active cycle과 겹친 호출은 semaphore를 기다리거나 queue하지 않고 `SkippedBusy`를 반환한다.

confirmed typed `ReadInputsAsync` fault는 P5-T1 alarm을 보고하고 epoch를 열지만 같은 cycle에서 reconnect하지 않는다. reconnect-success 직후 typed read fault는 같은 epoch의 consumed count와 다음 failure-time delay를 보존한다. 세 번째 attempt 뒤 read fault는 추가 clock 조회나 네 번째 I/O 없이 즉시 `ReconnectExhausted`가 되며 cancellation도 `Canceled` metadata로 epoch를 terminalize한다.

`EquipmentCycleResult`는 synchronization state, reconnect attempt count, 마지막 `ReconnectFailureKind`만 노출하고 exception object나 secret을 포함하지 않는다. `ConnectAsync` typed failure, cancellation, `TimeProvider`/policy-time exception은 communication alarm이 아니다. 이 metadata는 P5-T2 epoch visibility이며 P5-T3 strict source-incarnation/fresh-watermark synchronization 증거가 아니다.

typed output write fault는 P5-T1 `CommunicationLost`와 P4 command ID/terminal hold/no-replay fence를 그대로 유지하고 observation synchronization만 invalidate한다. observation/output port가 distinct이고 actual observation port가 계속 `Connected`이면 coordinator는 reconnect를 추론하거나 호출하지 않는다. P5-T2는 output write, retry/replay, admission release, Reset, Recovery 또는 acknowledgement consumption을 추가하지 않는다.
### 4.11 P5-T3 source-backed synchronization boundary — completed

```text
accepted source identity A/n
  → typed read fault or output-fault barrier
  → copied A or equal/lower same-incarnation sample rejected
  → current B/0 or later same-incarnation sample may Synchronize
  → CommunicationLost uncleared / no Recovery
```

`PlcInputSnapshot`과 `IPlcObservationPort`는 source-issued `PlcSourceTransportIncarnation`을 요구한다. Virtual PLC는 Connected 전이마다 새 identity와 sequence 0을 발급한다. Coordinator는 포트의 현재 identity와 다른 snapshot, 그리고 barrier 이후 같은 incarnation의 비증가 sequence를 `StaleObservation`/`WaitingForFreshInput`으로 거부한다. 연결된 observation port의 output fault는 `ConnectAsync`를 추론하지 않는다. exact semantic ACK는 admitted baseline incarnation과 더 큰 sequence가 아니면 명령을 완료하지 않는다.

이 경계는 P5-T3 synchronization evidence이며 Recovery/Reset 성공을 뜻하지 않는다.

### 4.12 P5-T4 fresh-safe recovery boundary — completed

```text
synchronized safe observation
  → ReportFreshSafeCommunicationEvidence
  → invalidates prior Acknowledge
  → new Acknowledge → Recovery-ready
  → Reset not invoked
```

문 열린 동기화 입력은 증거를 올리지 않는다. 증거 없는 Acknowledge/Stop/Reset은 `CommunicationLost`를 우회하지 못한다.

### 4.13 P5-T5 composite precedence boundary — completed

```text
CommunicationLost + DoorOpen or OverTemperature
  → comms-only evidence + Acknowledge
  → Alarm remains / not Recovery-ready
P4 ReceiptTimedOut hold
  → RequestReset AdmissionRejected even if Core Recovery-ready
```


## 5. 구성 요소별 책임

| 구성 요소 | 현재 책임 | 하지 않는 일 |
| --- | --- | --- |
| `Form1` | UI event 발생과 snapshot/event log rendering | Alarm, Recovery, Reset 판단 / PLC I/O |
| `IEquipmentView` | View input/output contract | Core 또는 communication policy 소유 |
| `IEquipmentObservationRuntime` | observation input/cycle/disposal capability | output command request/admission capability |
| `IEquipmentCommandRuntime` | named Start/Stop/Reset request와 admission stop capability | observation input/cycle, disposal, PLC protocol policy |
| `EquipmentPresenter` | async observation/Start/Stop/Reset 호출, command/cycle no-overlap ownership, close admission-stop/cancel/join/one-dispose/no-late-render | PLC protocol/ACK/deadline policy 판정 |
| `EquipmentCoordinator` | `IPlcObservationPort` connect/read, accepted source-identity mapping, active typed read failure → Core `CommunicationLost`, bounded reconnect epoch, P5-T3 source-fresh barrier, synchronized safe input → `ReportFreshSafeCommunicationEvidence` | WinForms Control 접근, output write/replay, command admission, Reset 판단 |
| `EquipmentCommandCoordinator` | opaque Core reservation, one pending command-ID, narrow output one-shot dispatch, transport receipt classification, internal command-family completion request | input observation, timeout recovery, retry/replay, reservation release |
| `EquipmentCommandRuntime` | Start/Stop/Reset baseline/admission/dispatch, shared P3/P4 serialization, exact fresh ACK same incarnation, monotonic deadlines, actual write typed failure → Core `CommunicationLost` + observation synchronization invalidation; P4 hold blocks Reset even if Core Recovery-ready | observation-port reconnect 추론/제어, retry/replay/release |
| `ThermalController` | phase, interlock, pending alarms including `CommunicationLost`, Recovery/Reset, recipe, event history | socket, reconnect, Modbus address, async I/O, system clock 직접 접근 |
| `IPlcObservationPort` | connect/disconnect/read observation contract | output write, UI/Core dependency, simulation fault control |
| `IPlcOutputPort` | typed one-shot output write only | input read, connection/disposal, UI/Core dependency, simulation controls |
| `IPlcClient` | empty compatibility composite of observation + output ports | UI/Core dependency, fault injection, reconnect policy |
| `VirtualPlcObservationInputControl` | P3 temperature/sensor/door simulation input setters | virtual time, ACK suppression, transport fault control |
| `VirtualPlcSimulationControl` | P4-oriented explicit `Advance`, delayed/suppressed ACK, transport fault simulation | P3 runtime injection |

## 6. 실제 PLC I/O contract

아래 fence는 current `IPlcOutputPort`와 empty compatibility `IPlcClient` declaration 전체다.

```csharp
public interface IPlcOutputPort
{
	Task<PlcWriteReceipt> WriteOutputsAsync(
		PlcOutputCommand command,
		CancellationToken cancellationToken);
}

public interface IPlcClient : IPlcObservationPort, IPlcOutputPort
{
}
```

### Input observation

`PlcInputSnapshot`은 immutable validated value다.

| field | 의미 |
| --- | --- |
| `DoorClosed` | physical/simulated door input |
| `SensorHealthy` | sensor feedback health input |
| `CurrentTemperature` | finite observed temperature |
| `MachineState` | `Idle`, `Running`, `Faulted` PLC machine observation |
| `AcknowledgedCommandId` | later semantic acknowledgement observation; `0`은 아직 ACK 없음 |
| `ObservationSequence` | producer-issued monotonic freshness identity |

`ObservationSequence`은 system wall clock이 아니라 producer가 발행한 freshness identity다. coordinator는 non-increasing value를 stale observation으로 취급한다.

### Output receipt와 semantic ACK의 분리

`PlcOutputCommand`는 positive `CommandId`와 `PlcCommandKind` (`Start`, `Stop`, `Reset`)를 가진 typed one-shot command다. `PlcWriteReceipt`는 same command ID와 `Written` 또는 `Failed` transport result만 표현한다.

```text
PlcWriteReceipt.TransportStatus == Written
≠ PLC program accepted the command
≠ equipment entered the target state
≠ semantic ACK
```

P4-T2 coordinator는 `AcknowledgedCommandId`를 읽거나 command를 complete하지 않는다. P4-T5 runtime도 exact matching `Written`을 semantic success로 취급하지 않으며 receipt/ACK 각각의 3초 monotonic epoch를 분리한다. strictly later accepted observation의 exact pending ACK와 Core reservation revalidation success만 Start/Stop/Reset completion 및 global-fence release authority다. P3 `EquipmentCoordinator` 자체는 계속 `IPlcObservationPort` only이고 `WriteOutputsAsync`를 호출하지 않는다.

## 7. P3 Coordinator cycle의 현재 범위

### P3-T1 — committed

```text
CycleAsync(elapsed)
  → disconnected transport이면 ConnectAsync
  → ReadInputsAsync
  → non-increasing ObservationSequence이면 StaleObservation
  → fresh snapshot을 Core에 반영
  → EquipmentCycleResult 반환
  → WriteOutputsAsync 호출 없음
```

P3-T1은 one-cycle read synchronization slice다. polling loop, retry/backoff, command queue, output write는 포함하지 않는다.

### P3-T2 — committed atomic observation mapping

P3-T2 (`3a7398d`)는 `DoorClosed`, `SensorHealthy`, `CurrentTemperature`, `elapsed`를 Core-owned `ThermalObservation`으로 묶어 적용한다.

```text
PlcInputSnapshot
  → EquipmentCoordinator
  → ThermalObservation
  → ThermalController.ApplyObservation(...)
```

P3-T2 tests cover DoorOpen interlock, OverTemperature, SensorTimeout, fresh input 이후 sensor recovery를 Core policy로 검증한다. `ThermalObservation`은 PLC type을 reference하지 않는다.

### P3-T4 — source committed WinForms observation cycle

P3-T4 (`2e502fa`)는 Form timer event를 `IEquipmentObservationRuntime.CycleAsync(...)`로 전환했다. P4-T5 (`8127888`)에서도 observation interface는 output authority 없이 유지되고, Presenter가 별도 narrow command interface로 awaitable Start/Stop/Reset을 one owner로 소유한다. in-flight cycle no-overlap과 elapsed carry는 유지되며 closing은 command/cycle을 모두 cancel/join한 뒤 owner를 한 번 dispose하고 late render를 막는다.

Observation cycle 자체는 output write나 `VirtualPlcSimulationControl.Advance(...)`를 호출하지 않는다. Program의 one concrete owner가 shared command runtime과 P3-only `VirtualPlcObservationInputControl`을 구성하고, reflection/actual-object regression이 observation/command/simulation capability 경계를 고정한다.

## 8. 시간과 plant ownership

P2 `VirtualPlcClient`는 wall clock이나 hidden timer 없이 `VirtualPlcSimulationControl.Advance(TimeSpan)`에서만 virtual time을 전진한다. door/sensor/temperature fault control도 simulation boundary에만 있다.

P3-T3 (`b949e6c`)에서 legacy `ThermalController.Tick`의 synthetic temperature change와 Tick-only normal phase progression을 제거했다. 따라서 현재 정확한 표현은 다음이다.

- P2 `VirtualPlcClient`는 deterministic illustrative plant/input simulation을 구현한다.
- P3-T2 (`3a7398d`)는 fresh PLC input을 Core-owned `ThermalObservation`으로 atomic mapping한다.
- P3-T3 (`b949e6c`)는 observed temperature와 elapsed를 받는 `ApplyObservation(...)`에서만 normal phase policy가 진행되게 한다.
- `Tick`은 SensorTimeout/Recovery를 위한 legacy feedback timing을 보존하지만 physical temperature, Holding elapsed, Heating/Holding/Cooling phase를 진행하지 않는다.
- P4-T4 `EquipmentCommandRuntime`의 injected `TimeProvider` monotonic timestamp만 receipt/ACK deadline을 판정한다. wall clock과 Core에는 deadline authority가 없다.
- P5-T2 `EquipmentCoordinator`는 같은 injected `TimeProvider`의 monotonic timestamp로 observation reconnect delay 250 ms → +500 ms → +1 s와 failure-time 재기준을 판정한다. wall clock, Core, output path에는 reconnect scheduling authority가 없다.

P3-T3은 Core source boundary다. P3-T4 `2e502fa`는 WinForms observation composition을 추가했고, current solution baseline `9c3ad95`에서 user-driven Session 1 manual smoke가 observed input `20 → 30 → Apply`, `30.00 °C` rendering, Idle 유지, UI 종료 후 process absence를 확인했다. 이는 narrow input-to-render/close composition evidence일 뿐이며 P0 screenshots는 historical baseline으로 남고, automated source/test/build evidence를 current UI screenshot evidence나 P4 evidence로 확장하지 않는다.

## 9. Alarm / Recovery policy

```text
Active phase
  → Alarm
  → cause cleared + Acknowledge + all pending alarms cleared
  → Recovery
  → Reset
  → Idle
```

| Alarm | 발생 조건 | Recovery 전 조건 |
| --- | --- | --- |
| `DoorOpen` | active phase에서 door open | door close + Acknowledge |
| `OverTemperature` | observed temperature가 safety limit 이상 | temperature가 safety limit 미만 + Acknowledge |
| `SensorTimeout` | unhealthy feedback gap이 timeout 이상 | healthy fresh input + Acknowledge |
| `CommunicationLost` | active safety-monitored read/write의 confirmed typed transport failure | P5-T4: synchronized safe evidence + new Acknowledge → Recovery-ready. Reset은 운영자 요청. P5-T5: 다른 pending alarm 또는 P4 hold가 있으면 Recovery/Reset 불가 |

여러 alarm cause가 동시에 남을 수 있다. 하나만 해소해도 pending alarm이 남으면 Recovery로 진행하지 않는다.

## 10. 검증 범위와 다음 evidence

### 현재 tracked baseline evidence

- `docs/verification/baseline-v0.1.md`
- `docs/verification/invariants.md`
- `docs/demo/images/` 및 `docs/demo/SCENARIOS.md`의 P0 UI baseline captures

### P3 source evidence

P1/P2/P3 source/test provenance는 각 local commit과 receipt에 남아 있다. P4-T1은 [`p4-t1-command-reservation-and-id-admission.md`](verification/p4-t1-command-reservation-and-id-admission.md)에 `8f32ce7`, P4-T2는 [`p4-t2-output-port-and-transport-receipt.md`](verification/p4-t2-output-port-and-transport-receipt.md)에 `254c546`, P4-T3는 [`p4-t3-exact-fresh-start-semantic-ack.md`](verification/p4-t3-exact-fresh-start-semantic-ack.md)에 `7a874e8`, P4-T4는 [`p4-t4-monotonic-lifecycle-holds.md`](verification/p4-t4-monotonic-lifecycle-holds.md)에 `0e2f6d2` + `cdbca25`, P4-T5는 [`p4-t5-command-family-completeness.md`](verification/p4-t5-command-family-completeness.md)에 `8127888`, exact 13-path scope, 39/39 + 49/49 + 20/20 + 26/26 + 19/19, full 153/153, repaired cleaner/architect/QA evidence를 기록한다. Final cumulative lineage, current Windows rerun, closure cohort, continuity, held P5+ scope는 [`p4-final-consistency-closure.md`](verification/p4-final-consistency-closure.md)에 별도로 고정한다.

### P5-T1 source evidence

[`p5-t1-communication-lost.md`](verification/p5-t1-communication-lost.md)는 source `8fabaeb`, exact 10-path scope, typed read/write classification과 negative boundaries, Debug 0 warnings/0 errors, Abstractions 19/19 + Core 40/40 + Presentation 26/26 + Application 56/56 + Simulation 20/20, full 161/161, final frozen-byte code/test-spec reviews PASS와 manifest `40c624dc133a00c3f88a93531b6a9b23a8215d7901f5cca2d80e91c52108f706`를 기록한다. 이는 S08 automated integration evidence이며 S09/S10/S11, reconnect/synchronization/recovery, current UI runtime, Modbus/device/safety/release evidence가 아니다.

### P5-T2 source evidence

[`p5-t2-bounded-reconnect.md`](verification/p5-t2-bounded-reconnect.md)는 source `ca68f66`, parent `96a2483`, exact five-path scope, observation-only reconnect epoch와 P5-T1/P4 negative boundaries, isolated Windows R1/R2 replay 및 R3 expected RED evidence, Debug 0 warnings/0 errors, Abstractions 19/19 + Core 40/40 + Application 69/69 + Presentation 26/26 + Simulation 20/20, full 174/174, final frozen-byte code/test-spec reviews PASS와 manifest `5ad1b6979107b838f34bc839c07b2a185f1763c46194f76e17debbf35810864e`를 기록한다. 이 tracked documentation checkpoint가 source-bound evidence를 완결하므로 P5-T2는 `Completed`다.

### 재현 명령

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

P3-T2 result는 `3a7398d`, P3-T3는 `b949e6c`, P3-T4는 `2e502fa`, P4-T1은 `8f32ce7`, P4-T2는 `254c546`, P4-T3는 `7a874e8`, P4-T4는 `0e2f6d2` + `cdbca25`, P4-T5는 `8127888`, P5-T1은 `8fabaeb`, P5-T2는 `ca68f66` source commit과 각각의 verification receipt를 기준으로 재현한다.
