# Virtual Thermal Chamber Controller

WinForms 기반 가상 열처리 챔버 제어 시뮬레이터입니다. UI, Core 제어 규칙, PLC I/O 경계를 분리해 상태 전이·인터록·Alarm·Recovery를 자동 테스트로 확인합니다.

이 프로젝트는 실제 챔버, 생산 PLC, 산업 통신 프로토콜, 온도 센서, 히터를 제어하지 않습니다. 온도·Recipe·안전 한계·fault는 학습과 검증을 위한 시뮬레이션 값이며, PC 프로그램의 인터록은 E-Stop·Safety PLC·하드웨어 안전회로를 대체하지 않습니다.

## 구현 상태와 증거 상태

아래 표는 `2026-08-18` Windows authoritative worktree를 기준으로 한다. `Completed`는 source local commit, source-bound 자동 검증, frozen review, 그리고 별도 tracked documentation checkpoint가 있는 상태를 뜻한다.

| 범위 | evidence state | 근거 |
| --- | --- | --- |
| P0 WinForms/Core 기준선 | Completed | `1497a06`, `40716fa`; Debug build 0 warnings / 0 errors, 27 tests |
| P1 PLC abstraction contracts | Completed | `587b519`까지의 contract commits; full regression 48/48 |
| P2 deterministic Virtual PLC | Completed | `1935c5f`, `64eb20d`; full regression 59/59, independent reviews PASS |
| P3-T1 read-only `EquipmentCoordinator` | Completed | `54e8303`; full regression 61/61, independent review PASS |
| P3-T2 atomic `ThermalObservation` mapping | Completed | `3a7398d`; focused Core 3/3, Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte source review manifest `1f65b461e9a08e08f0559b9018f2af27a6e600decf53114c756221283f42a090` PASS |
| P3-T3 Core plant simulation 분리 | Completed | `b949e6c`; focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66, Windows-byte source review manifest `e7e94da9061b06428e9370c3fdbf1af28a28e2d5f451070d8de0e292039f540f` PASS |
| P3-T4 WinForms observation composition | Completed | implementation `2e502fa`; current solution baseline `9c3ad95`; P3-only concrete input facade, async non-overlapping observation cycle, close teardown, full regression 80/80, independent source/artifact reviews PASS; user-driven Session 1 smoke에서 observed input `20 → 30 → Apply` 후 `30.00 °C` rendering, Idle 유지, 오류 없음이 보고되었고 UI 종료 후 process absence를 확인 |
| P4 command lifecycle | P4-T1–P4-T5 Completed | reservation/admission `8f32ce7`; output/receipt `254c546`; Start exact fresh ACK `7a874e8`; monotonic hold/awaited ownership `0e2f6d2` + diagnostic repair `cdbca25`; complete Start/Stop/Reset family `8127888`; P4-T5 Debug 0 warnings/0 errors, full 153/153, repaired frozen cleaner/architect/QA reviews PASS |

## 현재 구현된 책임 경계

### P0 UI baseline — historical runtime evidence

아래 direct `Form1 → EquipmentPresenter → ThermalController` 경로는 P3-T4 이전에 캡처한 P0 baseline이다. 현재 source의 Form wiring이나 PLC observation runtime을 증명하지 않는다.

```text
Form1 (historical P0 View)
  → IEquipmentView event contract
  → EquipmentPresenter
  → ThermalController
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

이 경로의 screenshots와 `docs/demo/SCENARIOS.md`는 historical evidence로 보존한다.

### P3-T4 WinForms observation composition — completed source and supplementary UI evidence

```text
Form1 / IEquipmentView
  → EquipmentPresenter
  ├→ IEquipmentObservationRuntime → EquipmentCoordinator(IPlcObservationPort)
  └→ IEquipmentCommandRuntime → EquipmentCommandRuntime(IPlcOutputPort)
       → shared P3/P4 semaphore + monotonic lifecycle hold
  → ThermalObservation / exact Start·Stop·Reset ACK / ControllerSnapshot rendering
```

- P3 `EquipmentCoordinator`는 read-only `IPlcObservationPort`만 받는다. P4 `EquipmentCommandCoordinator`는 `ThermalController`와 narrow `IPlcOutputPort`만 받고, broad `IPlcClient`나 observation/input/connection capability를 받지 않는다. `IPlcClient`는 두 narrow port의 empty compatibility composite다.
- Presentation도 observation-only `IEquipmentObservationRuntime`과 named Start/Stop/Reset + admission-stop `IEquipmentCommandRuntime`을 분리한다. concrete wrapper 하나가 두 capability를 구현하고 `Program`은 같은 owner를 두 narrow reference로 Presenter에 주입한다.
- `VirtualPlcObservationInputControl`에는 temperature/sensor/door setter만 있고 `Advance`, ACK suppression, transport fault control이 없다. `VirtualPlcSimulationControl`은 P4-oriented test control로만 남는다.
- Presenter는 Start/Stop/Reset handler를 같은 owner로 await하고 active command/cycle을 따로 추적한다. close는 command admission stop → shared cancellation → both-task join → one-time owner disposal 순서이며 disposed 뒤 late render를 막는다.

P4-T5는 UI Start/Stop/Reset을 하나의 awaited command owner에 연결한다. 세 command 모두 exact fresh semantic ACK와 Core revalidation 뒤에만 complete하며, legacy direct Core Stop/Reset shortcut은 제거됐다. timeout/ambiguity 뒤 자동 retry, replay, release, reconnect recovery는 없다.

### P4-T1 command reservation and ID admission — completed

```text
ThermalController.TryReserveCommand(...)
  → opaque ControllerCommandReservation
  → EquipmentCommandCoordinator.TryAdmit(...)
  → positive process-local monotonic CommandId
  → one retained pending admission
```

- Core reservation은 PLC-neutral이며 admission 시 Core state/event를 변경하지 않는다.
- Application은 한 pending command만 유지한다. duplicate request와 invalidated reservation은 fence를 해제하거나 교체하지 않는다.
- P4-T1 source slice에는 output port write나 transport receipt 처리가 없었다. 해당 capability는 별도 P4-T2 source `254c546`에서만 추가됐다.
- source anchor와 exact validation/review 범위는 [`docs/verification/p4-t1-command-reservation-and-id-admission.md`](docs/verification/p4-t1-command-reservation-and-id-admission.md)에 기록한다.

P4-T1 reservation/admission, P4-T2 narrow output/transport receipt, P4-T3 exact fresh Start ACK, P4-T4 monotonic lifecycle holds, P4-T5 complete Start/Stop/Reset family and awaited Presentation ownership: implemented.

### P4-T2 narrow output port and transport receipt — completed

```text
EquipmentCommandCoordinator.DispatchPendingAsync(...)
  → retained EquipmentCommandAdmission only
  → exhaustive ControllerCommandKind → PlcCommandKind mapping
  → IPlcOutputPort.WriteOutputsAsync(...) exactly once
  → exact Written: AwaitingAcknowledgement
  → mismatch / Failed: DeliveryIndeterminate hold
```

- coordinator gate는 `await` 전에 dispatch-started를 claim하고 synchronous lock을 I/O 동안 보유하지 않는다.
- matching `Written` 뒤에도 Core state/event와 opaque reservation은 변경되지 않고 pending fence는 유지된다.
- mismatch, matching `Failed`, thrown/canceled write 뒤에도 재전송, ID 재할당, reservation release가 없다.
- source anchor와 frozen validation/review 범위는 [`docs/verification/p4-t2-output-port-and-transport-receipt.md`](docs/verification/p4-t2-output-port-and-transport-receipt.md)에 기록한다.

### P4-T3 exact fresh Start semantic ACK — completed

```text
EquipmentCommandRuntime.RequestStartAsync(...)
  → latest Completed P3 baseline + non-null PlcInputSnapshot
  → command ID > allocator and observed ACK high-water
  → retained Start dispatch through IPlcOutputPort
  → Written: AwaitingAcknowledgement, Core remains Idle

EquipmentCommandRuntime.CycleAsync(...)
  → shared semaphore serializes P3 read with P4 write
  → P3 maps a strictly later accepted observation first
  → exact pending ACK only
  → internal/friend Core revalidation + one-shot Start completion
```

- `ThermalController.TryCompleteAcknowledgedCommand(...)`는 non-public friend seam이다. exact owned active reservation, invalidation, current eligibility를 Core가 다시 확인하고 성공 시 한 번만 소비한다. public generic apply/release authority는 없다.
- stale/non-accepted ACK와 lower ACK는 complete하지 않는다. higher/wrong ACK, unsafe exact ACK, write exception/cancellation은 terminal reconciliation/Core-ineligible hold이며 retry, replay, release, retroactive completion이 없다.
- `VirtualPlcClient`는 due semantic timestamp에서 Start effect를 적용하고 남은 virtual interval만 heating한다. one-step overshoot와 equivalent split step은 같은 결과다. suppression은 observed ACK만 숨기고 semantic effect는 취소하지 않는다.
- T3 source에서는 UI/Presenter/Form/Program이 변경되지 않았다. 해당 source anchor와 exact evidence는 [`docs/verification/p4-t3-exact-fresh-start-semantic-ack.md`](docs/verification/p4-t3-exact-fresh-start-semantic-ack.md)에 기록한다.

### P4-T4 monotonic lifecycle holds and awaited Start ownership — completed

```text
RequestStartAsync(token)
  → Writing at output invocation timestamp
  → receipt elapsed >= 3s: ReceiptTimedOut (tie loses closed)
  → timely exact Written: ACK epoch begins
CycleAsync(...)
  → ACK elapsed >= 3s: AcknowledgementTimedOut
  → later observation remains evidence only; terminal state cannot revive
```

- injected `TimeProvider`의 monotonic timestamp만 사용한다. receipt deadline은 write invocation, ACK deadline은 timely exact matching `Written`에서 각각 시작한다. admission이나 wall clock에서 시작하지 않는다.
- noncooperative write timeout/cancellation은 physical write task가 settle할 때까지 shared semaphore lease를 continuation에 넘긴다. 그동안 P3 read와 later write는 막히며 eventual receipt는 terminal state를 complete/retry/release하지 않는다. late fault는 `TraceError` diagnostic으로 보존하고 `finally`에서만 lease를 해제한다.
- exact-boundary tie, delayed continuation, late ACK, duplicate Start, disconnect/reconnect observation, timeout 뒤 cancellation을 포함해 explicit terminal evidence와 one-ID/one-write fence를 보존한다.
- awaited UI Start와 close ownership은 자동 test evidence다. operator smoke, Stop/Reset P4 completion, reconnect recovery, real device/equipment behavior를 뜻하지 않는다. source anchor와 exact evidence는 [`docs/verification/p4-t4-monotonic-lifecycle-holds.md`](docs/verification/p4-t4-monotonic-lifecycle-holds.md)에 기록한다.

### P4-T5 command-family completeness — completed

```text
RequestStartAsync / RequestStopAsync / RequestResetAsync
  -> one private RequestCommandAsync(kind)
  -> one global pending reservation and output write
  -> timely exact Written starts ACK epoch only
  -> strictly later exact ACK + Core revalidation
  -> success alone releases the global fence
```

- Start, Stop, Reset은 one-command global fence를 공유한다. Stop priority/preemption과 Reset uncertainty-clearing 예외는 없다.
- Stop `Written`은 Core/virtual heat를 즉시 바꾸지 않는다. modeled semantic point의 virtual Stop effect는 simulated heater를 끄며, observed ACK suppression은 그 virtual effect를 취소하지 않는다. 이는 real-device effect evidence가 아니다.
- Reset은 Recovery-ready Core reservation 뒤에만 admit되고 virtual plant/alarm/safety/reconciliation shortcut을 갖지 않는다.
- stale/lower/same-sequence ACK는 completion authority가 아니어서 대기를 유지한다. higher/mismatched reconciliation, exact 3초 timeout, delayed ACK after timeout, post-dispatch Core ineligibility는 terminal hold이며 새 kind/ID/write를 허용하지 않는다.
- Form/Presenter는 세 command를 모두 awaited one-owner path로 route하고 close에서 admission stop, cancellation, join, single disposal, no-late-render 순서를 유지한다.
- source anchor와 exact evidence는 [`docs/verification/p4-t5-command-family-completeness.md`](docs/verification/p4-t5-command-family-completeness.md)에 기록한다. 이는 operator smoke, reconnect recovery, real device/equipment 또는 safety evidence가 아니다.

## 실제 PLC port 계약

```csharp
namespace ChamberControlSimulator.Plc.Abstractions;

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

`PlcInputSnapshot`은 `DoorClosed`, `SensorHealthy`, `CurrentTemperature`, `MachineState`, `AcknowledgedCommandId`, `ObservationSequence`을 가진 immutable observation이다.

`PlcOutputCommand`는 `CommandId`와 `PlcCommandKind` (`Start`, `Stop`, `Reset`)를 가진 typed one-shot command다. retained admission은 한 번만 dispatch된다. exact matching `Written`도 receipt 3초 안에 관찰된 경우에만 ACK epoch를 시작하며 PLC acceptance, equipment state transition, semantic ACK, completion을 뜻하지 않는다. timeout, mismatched/failed receipt, write exception/cancellation은 pending reservation과 duplicate fence를 유지하며 자동 retry/replay/release하지 않는다.

## Core safety policy

`ThermalController`는 상태 전이, DoorOpen/OverTemperature/SensorTimeout interlock, pending alarms, Acknowledge, Recovery, Reset, Recipe 선택, event history를 소유한다. Form, Presenter, Coordinator, Virtual PLC는 Alarm·Recovery·Reset을 독자적으로 판정하지 않는다.

현재 committed baseline의 normal path는 다음과 같다.

```text
Idle → Precheck → Heating → Holding → Cooling → Complete
```

P3-T2는 Core-owned `ThermalObservation`으로 external input을 적용했다 (`3a7398d`). P3-T3 (`b949e6c`)는 legacy `Tick`에서 synthetic temperature mutation과 Tick-only Heating/Holding/Cooling progression을 제거했다. `ApplyObservation(...)`만 observed temperature와 elapsed로 normal phase policy를 진행한다. `Tick`은 SensorTimeout/Recovery를 위한 legacy feedback timing을 보존하지만 plant temperature나 normal phase를 바꾸지 않는다.

P3-T4 source `2e502fa`는 Form/Presenter를 PLC observation runtime에 연결했다. current solution baseline `9c3ad95`에서 사용자가 수행한 Session 1 manual smoke는 observed input `20 → 30 → Apply` 후 `30.00 °C` rendering, Idle 유지, 오류 없음을 보고했고, UI 종료 후 application process absence를 확인했다. 이는 좁은 input-to-render/close composition evidence이며 P0 direct wiring과 screenshots는 계속 historical evidence다.

## 검증과 UI evidence의 범위

- `docs/verification/baseline-v0.1.md`와 `docs/verification/invariants.md`는 P0 baseline에 묶인 tracked evidence다.
- `docs/demo/images/`와 `docs/demo/SCENARIOS.md`의 캡처는 P0 direct Presenter/Core runtime evidence다.
- P1/P2/P3 provenance는 각 source receipt에 남아 있다. P4-T1은 [`p4-t1-command-reservation-and-id-admission.md`](docs/verification/p4-t1-command-reservation-and-id-admission.md)에 `8f32ce7`, P4-T2는 [`p4-t2-output-port-and-transport-receipt.md`](docs/verification/p4-t2-output-port-and-transport-receipt.md)에 `254c546`, P4-T3는 [`p4-t3-exact-fresh-start-semantic-ack.md`](docs/verification/p4-t3-exact-fresh-start-semantic-ack.md)에 `7a874e8`, P4-T4는 [`p4-t4-monotonic-lifecycle-holds.md`](docs/verification/p4-t4-monotonic-lifecycle-holds.md)에 `0e2f6d2`와 `cdbca25`, P4-T5는 [`p4-t5-command-family-completeness.md`](docs/verification/p4-t5-command-family-completeness.md)에 `8127888`를 기록한다.
- P4-T1 full 92/92는 `8f32ce7`, P4-T2 full 96/96는 `254c546`, P4-T3 full 114/114는 `7a874e8`, P4-T4 full 128/128는 `cdbca25`, P4-T5 Core 39/39 + Application 49/49 + Simulation 20/20 + Presentation 26/26 + Abstractions 19/19와 full 153/153는 `8127888`에 bound된다. 어느 결과도 reconnect recovery, real Modbus TCP/PLC/equipment, E-Stop, Safety PLC, hardware safety, 또는 human safety를 뜻하지 않는다.

## 재현 명령

Windows repository root에서 실행한다.

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

## 관련 문서

- [`docs/architecture-and-verification.md`](docs/architecture-and-verification.md): 구현됨 / 후보 / 계획됨을 분리한 책임 경계와 검증 기록
- [`docs/demo/SCENARIOS.md`](docs/demo/SCENARIOS.md): P0 historical UI scenario와 screenshot 범위
- [`docs/verification/p3-t4-winforms-observation-composition.md`](docs/verification/p3-t4-winforms-observation-composition.md): P3-T4 source SHA, automated evidence, and explicit nonclaims
- [`docs/verification/p4-t1-command-reservation-and-id-admission.md`](docs/verification/p4-t1-command-reservation-and-id-admission.md): P4-T1 source SHA, exact reservation/admission evidence, and later-slice nonclaims
- [`docs/verification/p4-t2-output-port-and-transport-receipt.md`](docs/verification/p4-t2-output-port-and-transport-receipt.md): P4-T2 source SHA, narrow capability/receipt evidence, and later-slice nonclaims
- [`docs/verification/p4-t3-exact-fresh-start-semantic-ack.md`](docs/verification/p4-t3-exact-fresh-start-semantic-ack.md): P4-T3 source SHA, exact fresh Start ACK evidence, review repair, and P4-T4/T5/UI nonclaims
- [`docs/verification/p4-t4-monotonic-lifecycle-holds.md`](docs/verification/p4-t4-monotonic-lifecycle-holds.md): P4-T4 source SHA, monotonic deadline/terminal-hold and awaited Start/close evidence, review repair, and later-slice/device/safety nonclaims
- [`docs/verification/p4-t5-command-family-completeness.md`](docs/verification/p4-t5-command-family-completeness.md): P4-T5 source SHA, complete command-family/global-fence/simulation/Presentation evidence, repaired reviews, and recovery/device/safety nonclaims
- local ignored `docs/roadmap/STATUS.md`: 다음 작업 세션용 current progress tracker. tracked verification receipt를 대체하지 않는다.
