# 아키텍처와 검증 기록

## 1. 문서 상태와 증거 경계

이 문서는 `2026-08-16` Windows authoritative repository를 기준으로 작성한 architecture/progress record다. 아래 네 상태를 섞지 않는다.

| evidence state | 의미 |
| --- | --- |
| Completed | local commit과 해당 source의 검증 근거가 있다. |
| In progress — reviewed but uncommitted | source·test·review 후보는 있으나 아직 local commit이 없다. |
| Planned | roadmap만 존재하며 source가 없다. |
| Historical baseline | 과거 source SHA와 연결된 화면 또는 test evidence다. 현재 wiring의 증거가 아니다. |

P3-T2 atomic observation mapping의 source anchor는 `3a7398d` (`feat: map PLC observations atomically`)다. focused Core 3/3, focused Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte independent source review를 통과했다. 뒤따르는 documentation checkpoint는 의도적으로 별도 commit으로 남긴다.

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
| P3-T3 plant simulation 분리 | Planned | `ThermalController.Tick`의 synthetic temperature 변경 제거 |
| P3-T4 UI composition | Planned | Form/Presenter와 Coordinator/Virtual PLC 연결, cancellation/dispose 경계 |
| P4 command/ACK lifecycle | Planned | output write, matching ACK, timeout, duplicate prevention |

P3-T2의 65/65는 `3a7398d` source commit에 bound된 verification result다. P3-T3/T4/P4의 completion evidence나 전체 release claim으로 사용하지 않는다.

## 4. 현재 존재하는 두 실행 경계

### 4.1 P0 UI baseline — historical runtime path

WinForms runtime은 아직 P3-T4 전의 direct Presenter/Core wiring을 사용한다.

```text
Form1
  → IEquipmentView
  → EquipmentPresenter
  → ThermalController
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

`Form1`은 버튼·ComboBox·Timer event를 발생시키고 snapshot/event log를 렌더링한다. `EquipmentPresenter`는 view request를 Core 호출로 연결한다. `ThermalController`만 safety policy를 판단한다.

이 path의 screenshots와 `docs/demo/SCENARIOS.md`는 P0 historical baseline evidence다. Application coordinator 또는 Virtual PLC가 WinForms runtime에 이미 연결됐다는 증거가 아니다.

### 4.2 P1/P2/P3 Application boundary — implemented source path

```text
EquipmentCoordinator (Application)
  ├→ ThermalController (Core safety/process authority)
  └→ IPlcClient (PLC I/O port)
       ├→ VirtualPlcClient (implemented)
       └→ ModbusTcpPlcClient (P8 optional, not implemented)
```

`ChamberControlSimulator.Application`은 `Core`와 `Plc.Abstractions`만 reference한다. `Plc.Simulation`, WinForms, Modbus protocol package를 reference하지 않는다. 구체 adapter 선택은 future composition root의 책임이다.

## 5. 구성 요소별 책임

| 구성 요소 | 현재 책임 | 하지 않는 일 |
| --- | --- | --- |
| `Form1` | UI event 발생과 snapshot/event log rendering | Alarm, Recovery, Reset 판단 / PLC I/O |
| `IEquipmentView` | View input/output contract | Core 또는 communication policy 소유 |
| `EquipmentPresenter` | baseline View event와 Core result 연결 | retry, reconnect, PLC protocol 판단 |
| `EquipmentCoordinator` | connect/read/freshness policy와 PLC input → Core mapping | WinForms Control 접근, simulation concrete type 판별, semantic ACK completion 판단 |
| `ThermalController` | phase, interlock, pending alarm, Recovery/Reset, recipe, event history | socket, Modbus address, async I/O, system clock 직접 접근 |
| `IPlcClient` | connect/disconnect/read/write contract | UI/Core dependency, fault injection, reconnect policy |
| `VirtualPlcClient` | deterministic virtual transport/plant state, explicit `Advance`, delayed/suppressed ACK, fault simulation | production equipment 정확도 주장 |
| `VirtualPlcSimulationControl` | test/demo용 door/sensor/temp/fault virtual control | `IPlcClient` public contract 노출 |

## 6. 실제 PLC I/O contract

```csharp
public interface IPlcClient : IAsyncDisposable
{
    PlcConnectionState ConnectionState { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken);
    Task<PlcWriteReceipt> WriteOutputsAsync(
        PlcOutputCommand command,
        CancellationToken cancellationToken);
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

P4에서 only matching later `AcknowledgedCommandId`가 command completion을 뜻하도록 구현할 예정이다. P3-T1/T2 coordinator는 `WriteOutputsAsync`를 호출하지 않으며 ACK를 semantic result로 해석하지 않는다.

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

## 8. 시간과 plant ownership

P2 `VirtualPlcClient`는 wall clock이나 hidden timer 없이 `VirtualPlcSimulationControl.Advance(TimeSpan)`에서만 virtual time을 전진한다. door/sensor/temperature fault control도 simulation boundary에만 있다.

아직 P3-T3 전이므로 legacy `ThermalController.Tick`은 synthetic temperature change를 가진다. 따라서 현재 정확한 표현은 다음이다.

- Virtual PLC는 deterministic plant/input simulation을 구현했다.
- P3-T2 (`3a7398d`)는 observed input을 Core에 atomic mapping한다.
- **Core에서 plant simulation을 완전히 제거한 상태는 아직 아니다.**

P3-T3에서는 Core가 observed temperature와 elapsed로 phase policy만 판단하도록 legacy synthetic heat/cool mutation을 제거한다.

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

P1/P2/P3-T1의 local commit과 Windows build/test output, independent review bundles가 source/test provenance다. P3-T2는 [`p3-t2-atomic-observation.md`](verification/p3-t2-atomic-observation.md)에 `3a7398d`, exact commands, 65/65 result, source-review manifest를 기록한다.

### 재현 명령

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

P3-T2 result는 `3a7398d` source commit과 `docs/verification/p3-t2-atomic-observation.md` receipt를 기준으로 재현한다.
