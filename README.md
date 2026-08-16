# Virtual Thermal Chamber Controller

WinForms 기반 가상 열처리 챔버 제어 시뮬레이터입니다. UI, Core 제어 규칙, PLC I/O 경계를 분리해 상태 전이·인터록·Alarm·Recovery를 자동 테스트로 확인합니다.

이 프로젝트는 실제 챔버, 생산 PLC, 산업 통신 프로토콜, 온도 센서, 히터를 제어하지 않습니다. 온도·Recipe·안전 한계·fault는 학습과 검증을 위한 시뮬레이션 값이며, PC 프로그램의 인터록은 E-Stop·Safety PLC·하드웨어 안전회로를 대체하지 않습니다.

## 구현 상태와 증거 상태

아래 표는 `2026-08-16` Windows authoritative worktree를 기준으로 한다. `Completed`는 local commit과 검증 근거가 있는 상태이고, `In progress`는 working tree에만 있는 후보를 뜻한다.

| 범위 | evidence state | 근거 |
| --- | --- | --- |
| P0 WinForms/Core 기준선 | Completed | `1497a06`, `40716fa`; Debug build 0 warnings / 0 errors, 27 tests |
| P1 PLC abstraction contracts | Completed | `587b519`까지의 contract commits; full regression 48/48 |
| P2 deterministic Virtual PLC | Completed | `1935c5f`, `64eb20d`; full regression 59/59, independent reviews PASS |
| P3-T1 read-only `EquipmentCoordinator` | Completed | `54e8303`; full regression 61/61, independent review PASS |
| P3-T2 atomic `ThermalObservation` mapping | Completed | `3a7398d`; focused Core 3/3, Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte source review manifest `1f65b461e9a08e08f0559b9018f2af27a6e600decf53114c756221283f42a090` PASS |
| P3-T3 Core plant simulation 분리 | Completed | `b949e6c`; focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66, Windows-byte source review manifest `e7e94da9061b06428e9370c3fdbf1af28a28e2d5f451070d8de0e292039f540f` PASS |
| P3-T4 WinForms composition root | Planned | Form/Presenter를 Coordinator와 Virtual PLC에 연결하고 cancellation·dispose 경계를 추가 예정 |
| P4 command ID / semantic ACK lifecycle | Planned | Application output write, matching ACK, timeout, duplicate prevention은 아직 구현하지 않음 |

## 현재 구현된 책임 경계

### P0 UI baseline — historical runtime evidence

현재 `Form1` UI는 아직 P3-T4 이전의 baseline wiring을 유지한다.

```text
Form1 (WinForms View)
  → IEquipmentView event contract
  → EquipmentPresenter
  → ThermalController
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

이 경로의 화면 캡처와 시나리오는 P0 baseline evidence다. Coordinator가 Form에 연결되었다는 증거로 해석하면 안 된다.

### P1/P2/P3 Application path — implemented boundary

```text
EquipmentCoordinator (Application)
  ├→ ThermalController (Core safety/process authority)
  └→ IPlcClient (PLC I/O port)
       ├→ VirtualPlcClient (implemented)
       └→ ModbusTcpPlcClient (P8 선택 단계, 미구현)
```

- `IPlcClient`는 connection lifecycle, input read, typed output write만 노출한다.
- `VirtualPlcClient`는 명시적 virtual time, delayed/suppressed acknowledgement, transport fault, door/sensor/temperature simulation control을 가진 상태형 simulator다.
- `EquipmentCoordinator`는 P3-T1에서 disconnected transport를 connect하고 한 snapshot을 읽으며, non-increasing `ObservationSequence`를 stale로 거부한다. 이 slice는 `WriteOutputsAsync`를 호출하지 않고 ACK를 semantic completion으로 해석하지 않는다.
- P3-T2는 `PlcInputSnapshot`의 Door/Sensor/Temperature를 PLC-independent `ThermalObservation`으로 묶어 Core에 적용한다. 이 source boundary는 `3a7398d`에 committed됐다.
- P3-T3는 `ThermalController.Tick`의 synthetic heat/cool과 Tick-only normal phase progression을 제거했다 (`b949e6c`). normal phase policy는 external `ThermalObservation`과 elapsed를 받는 `ApplyObservation(...)`에서만 진행한다.

P3-T4 전에는 위 Application path가 WinForms runtime composition root에 연결되지 않는다. P3-T3은 Core가 plant state를 합성하지 않게 한 source boundary이며, legacy Form/Presenter가 PLC observation runtime으로 전환됐다는 증거는 아니다. P4 전에는 UI Start/Stop/Reset을 PLC command write와 semantic ACK로 연결했다고 주장하지 않는다.

## 실제 PLC port 계약

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

`PlcInputSnapshot`은 `DoorClosed`, `SensorHealthy`, `CurrentTemperature`, `MachineState`, `AcknowledgedCommandId`, `ObservationSequence`을 가진 immutable observation이다.

`PlcOutputCommand`는 `CommandId`와 `PlcCommandKind` (`Start`, `Stop`, `Reset`)를 가진 typed one-shot command다. `PlcWriteReceipt.TransportStatus == Written`은 transport write 결과일 뿐 PLC acceptance, equipment state transition, semantic ACK를 뜻하지 않는다. semantic ACK는 이후 input snapshot의 `AcknowledgedCommandId`로 판단할 P4 범위다.

## Core safety policy

`ThermalController`는 상태 전이, DoorOpen/OverTemperature/SensorTimeout interlock, pending alarms, Acknowledge, Recovery, Reset, Recipe 선택, event history를 소유한다. Form, Presenter, Coordinator, Virtual PLC는 Alarm·Recovery·Reset을 독자적으로 판정하지 않는다.

현재 committed baseline의 normal path는 다음과 같다.

```text
Idle → Precheck → Heating → Holding → Cooling → Complete
```

P3-T2는 Core-owned `ThermalObservation`으로 external input을 적용했다 (`3a7398d`). P3-T3 (`b949e6c`)는 legacy `Tick`에서 synthetic temperature mutation과 Tick-only Heating/Holding/Cooling progression을 제거했다. `ApplyObservation(...)`만 observed temperature와 elapsed로 normal phase policy를 진행한다. `Tick`은 SensorTimeout/Recovery를 위한 legacy feedback timing을 보존하지만 plant temperature나 normal phase를 바꾸지 않는다.

이 boundary가 P3-T4 runtime composition을 대신하지는 않는다. 기존 Form/Presenter는 아직 direct baseline wiring과 manual simulation input을 유지하며, PLC/Virtual PLC observation을 UI runtime에 연결하는 작업은 P3-T4 범위다.

## 검증과 UI evidence의 범위

- `docs/verification/baseline-v0.1.md`와 `docs/verification/invariants.md`는 P0 baseline에 묶인 tracked evidence다.
- `docs/demo/images/`와 `docs/demo/SCENARIOS.md`의 캡처는 P0 direct Presenter/Core runtime evidence다.
- P1/P2/P3-T1의 source/test/review provenance는 각 local commit과 independent review bundle에 남아 있다. P3-T2 source verification은 [`docs/verification/p3-t2-atomic-observation.md`](docs/verification/p3-t2-atomic-observation.md)에 `3a7398d` SHA와 함께 기록한다. P3-T3 source verification은 [`docs/verification/p3-t3-core-plant-separation.md`](docs/verification/p3-t3-core-plant-separation.md)에 `b949e6c` SHA와 함께 기록한다.
- P3-T2의 65/65는 `3a7398d` source commit에, P3-T3의 66/66은 `b949e6c` source commit에 각각 bound된 verification result다. P3-T4/P4의 completion evidence나 release evidence 전체를 뜻하지 않는다.

## 재현 명령

Windows repository root에서 실행한다.

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

## 관련 문서

- [`docs/architecture-and-verification.md`](docs/architecture-and-verification.md): 구현됨 / 후보 / 계획됨을 분리한 책임 경계와 검증 기록
- [`docs/demo/SCENARIOS.md`](docs/demo/SCENARIOS.md): P0 baseline UI scenario와 screenshot 범위
- local ignored `docs/roadmap/STATUS.md`: 다음 작업 세션용 current progress tracker. tracked verification receipt를 대체하지 않는다.
