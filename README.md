# Virtual Thermal Chamber Controller

WinForms 기반 가상 열처리 챔버 제어 시뮬레이터입니다. UI, Core 제어 규칙, PLC I/O 경계를 분리해 상태 전이·인터록·Alarm·Recovery를 자동 테스트로 확인합니다.

이 프로젝트는 실제 챔버, 생산 PLC, 산업 통신 프로토콜, 온도 센서, 히터를 제어하지 않습니다. 온도·Recipe·안전 한계·fault는 학습과 검증을 위한 시뮬레이션 값이며, PC 프로그램의 인터록은 E-Stop·Safety PLC·하드웨어 안전회로를 대체하지 않습니다.

## 구현 상태와 증거 상태

아래 표는 `2026-08-17` Windows authoritative worktree를 기준으로 한다. `Completed`는 local commit과 검증 근거가 있는 상태이고, `Source committed`는 implementation SHA와 automated evidence가 local commit에 고정됐지만 post-fix UI smoke 같은 별도 evidence가 남아 있을 수 있음을 뜻한다.

| 범위 | evidence state | 근거 |
| --- | --- | --- |
| P0 WinForms/Core 기준선 | Completed | `1497a06`, `40716fa`; Debug build 0 warnings / 0 errors, 27 tests |
| P1 PLC abstraction contracts | Completed | `587b519`까지의 contract commits; full regression 48/48 |
| P2 deterministic Virtual PLC | Completed | `1935c5f`, `64eb20d`; full regression 59/59, independent reviews PASS |
| P3-T1 read-only `EquipmentCoordinator` | Completed | `54e8303`; full regression 61/61, independent review PASS |
| P3-T2 atomic `ThermalObservation` mapping | Completed | `3a7398d`; focused Core 3/3, Application 3/3, Debug build 0 warnings/0 errors, full regression 65/65, Windows-byte source review manifest `1f65b461e9a08e08f0559b9018f2af27a6e600decf53114c756221283f42a090` PASS |
| P3-T3 Core plant simulation 분리 | Completed | `b949e6c`; focused Tick contract 1/1, Debug build 0 warnings/0 errors, full regression 66/66, Windows-byte source review manifest `e7e94da9061b06428e9370c3fdbf1af28a28e2d5f451070d8de0e292039f540f` PASS |
| P3-T4 WinForms observation composition | Source committed | `2e502fa`; P3-only concrete input facade, async non-overlapping observation cycle, close teardown, full regression 80/80, independent source/artifact reviews PASS; post-fix Session 1 UI smoke는 deferred |
| P4 command ID / semantic ACK lifecycle | Planned | Application output write, matching ACK, timeout, duplicate prevention은 아직 구현하지 않음 |

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

### P3-T4 WinForms observation composition — source committed

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

- `EquipmentCoordinator`는 read-only `IPlcObservationPort`만 선언적으로 받는다. `IPlcClient`는 그 observation contract를 확장해 P4용 `WriteOutputsAsync`를 가진 compatibility port다.
- `Program`은 P3 setter path에 별도 concrete `VirtualPlcObservationInputControl`을 주입한다. 이 facade에는 temperature/sensor/door setter만 있고 `Advance`, ACK suppression, transport fault control이 없다.
- `VirtualPlcSimulationControl`은 explicit virtual time, delayed/suppressed acknowledgement, transport fault를 보유하는 P4-oriented simulation facade로 남는다. P3 nominal observation cycle은 이를 받거나 `Advance(...)`를 호출하지 않는다.
- P3-T1/T2의 connect/read/freshness와 atomic `ThermalObservation` mapping, P3-T3의 observed-input-only Core policy는 이 composition에서 재사용된다.
- timer cycle은 async/non-overlapping이며, close는 cancellation → active-cycle join → one-time runtime disposal을 기다린다.

P4 전에는 UI Start/Stop/Reset을 PLC command write, transport receipt, matching semantic ACK, timeout, duplicate prevention으로 연결했다고 주장하지 않는다.

## 실제 PLC port 계약

```csharp
public interface IPlcClient : IPlcObservationPort
{
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

P3-T4 source `2e502fa`는 Form/Presenter를 PLC observation runtime에 연결했다. P0 direct wiring과 screenshots는 historical evidence로 남고, post-fix Session 1 UI smoke는 사용자가 deferred했으므로 current composition screenshot evidence로 주장하지 않는다.

## 검증과 UI evidence의 범위

- `docs/verification/baseline-v0.1.md`와 `docs/verification/invariants.md`는 P0 baseline에 묶인 tracked evidence다.
- `docs/demo/images/`와 `docs/demo/SCENARIOS.md`의 캡처는 P0 direct Presenter/Core runtime evidence다.
- P1/P2/P3-T1의 source/test/review provenance는 각 local commit과 independent review bundle에 남아 있다. P3-T2 source verification은 [`docs/verification/p3-t2-atomic-observation.md`](docs/verification/p3-t2-atomic-observation.md)에 `3a7398d` SHA와 함께 기록한다. P3-T3 source verification은 [`docs/verification/p3-t3-core-plant-separation.md`](docs/verification/p3-t3-core-plant-separation.md)에 `b949e6c` SHA와 함께 기록한다. P3-T4 source verification은 [`docs/verification/p3-t4-winforms-observation-composition.md`](docs/verification/p3-t4-winforms-observation-composition.md)에 `2e502fa` SHA와 함께 기록한다.
- P3-T2의 65/65는 `3a7398d` source commit에, P3-T3의 66/66은 `b949e6c` source commit에, P3-T4의 80/80은 `2e502fa` source commit에 각각 bound된 verification result다. 어느 결과도 P4 completion, real Modbus TCP, physical PLC, hardware safety, 또는 deferred post-fix UI smoke evidence를 뜻하지 않는다.

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
- local ignored `docs/roadmap/STATUS.md`: 다음 작업 세션용 current progress tracker. tracked verification receipt를 대체하지 않는다.
