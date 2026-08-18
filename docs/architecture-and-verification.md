# 아키텍처와 검증 기록

## 1. 문서 상태와 증거 경계

이 문서는 `2026-08-18` Windows authoritative repository를 기준으로 작성한 architecture/progress record다. 아래 네 상태를 섞지 않는다.

| evidence state | 의미 |
| --- | --- |
| Completed | local commit과 해당 source의 검증 근거가 있다. |
| In progress — reviewed but uncommitted | source·test·review 후보는 있으나 아직 local commit이 없다. |
| Planned | roadmap만 존재하며 source가 없다. |
| Historical baseline | 과거 source SHA와 연결된 화면 또는 test evidence다. 현재 wiring의 증거가 아니다. |

P3-T2 atomic observation mapping의 source anchor는 `3a7398d` (`feat: map PLC observations atomically`)다. focused Core 3/3, focused Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte independent source review를 통과했다. 뒤따르는 documentation checkpoint는 의도적으로 별도 commit으로 남긴다.

P3-T3 Core plant simulation separation의 source anchor는 `b949e6c` (`feat: separate Core plant temperature policy`)다. focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66, Windows-byte independent source review를 통과했다. 이 문서와 P3-T3 verification receipt도 source commit 뒤의 별도 documentation checkpoint로 남긴다.

P3-T4 WinForms observation composition의 implementation source anchor는 `2e502fa` (`feat: compose WinForms observation runtime`)다. concrete P3 input facade, non-overlapping observation cycle, close teardown, P3/P4 capability boundary를 포함하며 Debug build 0 warnings/0 errors, full regression 80/80, Windows-byte independent architecture/lifecycle review PASS와 artifact-integrity review PASS를 통과했다. current solution baseline `9c3ad95`에서 user-driven Session 1 manual smoke는 observed input `20 → 30 → Apply` 후 `30.00 °C` rendering, Idle 유지, 오류 없음을 보고했고 UI 종료 후 application process absence를 확인했다.

P4-T1 command reservation/admission의 source anchor는 `8f32ce7` (`feat: reserve command admission before dispatch`)다. opaque Core reservation, no-transition admission, one retained Application pending command, positive monotonic process-local IDs, invalidation-retained duplicate fence를 포함하며 focused Core 7/7, focused Application 5/5, Debug build 0 warnings/0 errors, full regression 92/92, Windows-byte final source review PASS를 통과했다.

P4-T2 narrow output/transport receipt의 source anchor는 `254c546` (`feat: dispatch pending command through output port`)다. `IPlcOutputPort` 분리, empty `IPlcClient` composite, retained pending command one-shot dispatch, exact matching `Written`의 ACK-wait classification, mismatch/`Failed`/throw/cancel delivery-indeterminate fence를 포함하며 focused Abstractions 19/19, Application 14/14, Debug 0 warnings/0 errors, full 96/96, frozen Windows-byte review PASS를 통과했다. 이 evidence는 semantic ACK, Core completion, timeout/reconciliation, UI/Program/Presenter routing, Virtual PLC semantic effect를 증명하지 않는다.

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
| P4 command lifecycle | P4-T1/P4-T2 Completed | reservation/admission `8f32ce7`; narrow output/receipt `254c546`; P4-T2 focused 19/19 + 14/14, Debug 0/0, full 96/96, frozen source review PASS; P4-T3~T5 not implemented |

P3-T2의 65/65는 `3a7398d` source commit에, P3-T3의 66/66은 `b949e6c` source commit에 각각 bound된 verification result다. P3-T4/P4의 completion evidence나 전체 release claim으로 사용하지 않는다.

## 4. 현재 실행 경계

### 4.1 P0 UI baseline — historical runtime path

직접 `Form1 → EquipmentPresenter → ThermalController` wiring과 screenshots는 P3-T4 이전 P0 baseline이다. `docs/demo/SCENARIOS.md`는 그 historical procedure를 보존하며 current observation composition 또는 current UI screenshot evidence가 아니다.

### 4.2 P3-T4 current WinForms observation composition

```text
Form1 / IEquipmentView
  → EquipmentPresenter
  → IEquipmentObservationRuntime
  → EquipmentCoordinator(IPlcObservationPort)
  → VirtualPlcClient
  → ThermalObservation + elapsed
  → ThermalController.ApplyObservation(...)
  → ControllerSnapshot / Form1 rendering
```

`Program.CreateObservationRuntime(...)`은 `VirtualPlcObservationInputControl`을 주입한다. 이 concrete facade는 temperature/sensor/door setter만 갖고 P4 `Advance`, ACK suppression, transport fault API를 갖지 않는다. `VirtualPlcSimulationControl`은 P4-oriented test control로 분리되어 있다.

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

P4-T1 reservation/admission and P4-T2 narrow output/transport receipt: implemented. P4-T3 semantic ACK, P4-T4 timeout/reconciliation, P4-T5 Stop/Reset lifecycle: not implemented.

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

Written은 completed/successful semantic result가 아니다. matching `Written`, mismatch, `Failed`, exception, cancellation 어느 branch도 Core reservation을 적용·해제·교체하지 않고 새 ID/write를 허용하지 않는다. deadline과 public timeout/cancellation taxonomy는 P4-T4 범위다.

## 5. 구성 요소별 책임

| 구성 요소 | 현재 책임 | 하지 않는 일 |
| --- | --- | --- |
| `Form1` | UI event 발생과 snapshot/event log rendering | Alarm, Recovery, Reset 판단 / PLC I/O |
| `IEquipmentView` | View input/output contract | Core 또는 communication policy 소유 |
| `EquipmentPresenter` | async View event와 observation runtime 호출, no-overlap, close teardown | PLC protocol/ACK policy 판정 |
| `EquipmentCoordinator` | `IPlcObservationPort` connect/read/freshness policy와 PLC input → Core mapping | WinForms Control 접근, output write, semantic ACK completion 판단 |
| `EquipmentCommandCoordinator` | opaque Core reservation, one pending command-ID, narrow output one-shot dispatch, transport-only receipt classification | input/ACK consumption, Core completion, timeout recovery, retry/replay, reservation release |
| `ThermalController` | phase, interlock, pending alarm, Recovery/Reset, recipe, event history | socket, Modbus address, async I/O, system clock 직접 접근 |
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

P4-T2는 `AcknowledgedCommandId`를 읽거나 command를 complete하지 않는다. exact matching `Written`도 transport receipt일 뿐이며 pending reservation과 Core state/event를 변경하지 않는다. P4-T3에서만 accepted fresh observation의 exact matching ACK를 completion evidence로 도입한다. P3 coordinator/runtime path는 계속 `IPlcObservationPort` only이고 `WriteOutputsAsync`를 호출하지 않는다.

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

P3-T4 (`2e502fa`)는 Form timer event를 `IEquipmentObservationRuntime.CycleAsync(...)`로 전환했다. cycle은 `EquipmentCoordinator.CycleAsync(...)`의 connect/read/map path를 호출하고 observed input과 elapsed를 Core policy에 반영한다. in-flight cycle 중 추가 timer tick은 observation I/O를 겹치지 않게 하며 elapsed는 다음 admitted cycle로 보존된다. Form closing은 cancellation, active-cycle join, one-time runtime disposal을 기다리고 late View render를 막는다.

이 path는 P4 command/receipt/ACK lifecycle 또는 `VirtualPlcSimulationControl.Advance(...)`를 진행하지 않는다. `Program`은 P3-only concrete `VirtualPlcObservationInputControl`을 주입하며 actual-object regression test가 broad P4 facade injection을 막는다.

## 8. 시간과 plant ownership

P2 `VirtualPlcClient`는 wall clock이나 hidden timer 없이 `VirtualPlcSimulationControl.Advance(TimeSpan)`에서만 virtual time을 전진한다. door/sensor/temperature fault control도 simulation boundary에만 있다.

P3-T3 (`b949e6c`)에서 legacy `ThermalController.Tick`의 synthetic temperature change와 Tick-only normal phase progression을 제거했다. 따라서 현재 정확한 표현은 다음이다.

- P2 `VirtualPlcClient`는 deterministic illustrative plant/input simulation을 구현한다.
- P3-T2 (`3a7398d`)는 fresh PLC input을 Core-owned `ThermalObservation`으로 atomic mapping한다.
- P3-T3 (`b949e6c`)는 observed temperature와 elapsed를 받는 `ApplyObservation(...)`에서만 normal phase policy가 진행되게 한다.
- `Tick`은 SensorTimeout/Recovery를 위한 legacy feedback timing을 보존하지만 physical temperature, Holding elapsed, Heating/Holding/Cooling phase를 진행하지 않는다.

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

여러 alarm cause가 동시에 남을 수 있다. 하나만 해소해도 pending alarm이 남으면 Recovery로 진행하지 않는다.

## 10. 검증 범위와 다음 evidence

### 현재 tracked baseline evidence

- `docs/verification/baseline-v0.1.md`
- `docs/verification/invariants.md`
- `docs/demo/images/` 및 `docs/demo/SCENARIOS.md`의 P0 UI baseline captures

### P3 source evidence

P1/P2/P3-T1의 local commit과 Windows build/test output, independent review bundles가 source/test provenance다. P3-T2는 [`p3-t2-atomic-observation.md`](verification/p3-t2-atomic-observation.md)에 `3a7398d`, exact commands, 65/65 result, source-review manifest를 기록한다. P3-T3는 [`p3-t3-core-plant-separation.md`](verification/p3-t3-core-plant-separation.md)에 `b949e6c`, focused Tick contract, 66/66 result, source-review manifest를 기록한다. P3-T4는 [`p3-t4-winforms-observation-composition.md`](verification/p3-t4-winforms-observation-composition.md)에 `2e502fa`, 80/80 result, concrete-facade and artifact-integrity review evidence를 기록한다. P4-T1은 [`p4-t1-command-reservation-and-id-admission.md`](verification/p4-t1-command-reservation-and-id-admission.md)에 `8f32ce7`, exact five-path source scope, focused 7/7 + 5/5, full 92/92, frozen-review evidence를 기록한다. P4-T2는 [`p4-t2-output-port-and-transport-receipt.md`](verification/p4-t2-output-port-and-transport-receipt.md)에 `254c546`, exact six-path source scope, focused 19/19 + 14/14, full 96/96, frozen-review evidence를 기록한다.

### 재현 명령

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

P3-T2 result는 `3a7398d`, P3-T3 result는 `b949e6c`, P3-T4 result는 `2e502fa`, P4-T1 result는 `8f32ce7`, P4-T2 result는 `254c546` source commit과 각 tracked verification receipt를 기준으로 재현한다.
