# P5-T3 source-backed connection synchronization verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Primary source commit: `fc373383982be9de3c34ef7aa08bf795bda9a6a9`
- Comment-repair commit: `7a2ceec` (`fix: align P5-T3 test comments with waiting-for-fresh-input`)
- Baseline/parent of primary source: `ef16772aa977e83e4cf9c6ea5d3552ef31092661`
- Subject: `feat: add source-backed PLC observation identity`
- Evidence state: Completed — primary source, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `fc37338` (repair `7a2ceec`)와 P5-T3를 `Completed` 상태로 bound한다. 아래 evidence는 source incarnation / fresh-watermark synchronization만 설명하며 alarm clearing, Recovery-ready, Reset, UI/device/safety 또는 publication 완료를 뜻하지 않는다.

## Exact source scope

Create:

1. `ChamberControlSimulator.Plc.Abstractions/PlcSourceTransportIncarnation.cs`

Modify:

2. `ChamberControlSimulator.Plc.Abstractions/PlcInputSnapshot.cs`
3. `ChamberControlSimulator.Plc.Abstractions/IPlcObservationPort.cs`
4. `ChamberControlSimulator.Plc.Simulation/VirtualPlcClient.cs`
5. `ChamberControlSimulator.Application/EquipmentCoordinator.cs`
6. `ChamberControlSimulator.Application/EquipmentCommandRuntime.cs`
7. `ChamberControlSimulator.Plc.Abstractions.Tests/PlcInputSnapshotTests.cs`
8. `ChamberControlSimulator.Plc.Simulation.Tests/VirtualPlcClientLifecycleTests.cs`
9. `ChamberControlSimulator.Application.Tests/EquipmentCoordinatorTests.cs`
10. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`

Primary source commit `fc37338`에는 위 열 경로만 포함된다. Comment repair `7a2ceec`는 같은 allowlist 중 테스트 두 파일의 Korean 주석만 고친다. Core, Presentation, WinForms, project, documentation, roadmap, protocol, image 경로는 source commit에 포함되지 않는다.

## Implemented contract

### Source identity

`PlcSourceTransportIncarnation`은 비어 있지 않은 `Guid`만 허용하는 불변 값이다. `PlcInputSnapshot`은 이 identity를 필수 생성자 인자로 받고, `IPlcObservationPort.CurrentSourceTransportIncarnation`은 `Connected`일 때만 현재 소스 identity를 노출한다. identity는 `TimeProvider`, 수신 시각, coordinator 카운터로 합성하지 않는다.

`VirtualPlcClient`는 `Disconnected`/`Faulted`에서 `Connected`로 들어갈 때 새 incarnation을 발급하고 observation sequence를 0으로 되돌린다. disconnect, fault, dispose는 현재 identity를 지운다. `ConnectAsync`만으로는 snapshot을 만들지 않는다.

### Coordinator source-fresh barrier

`EquipmentCoordinator`는 마지막 수락 identity를 `(PlcSourceTransportIncarnation, ObservationSequence)`로 유지한다. 읽은 snapshot이 포트의 현재 connected identity와 다르면 `StaleObservation` / `WaitingForFreshInput`이고 Core observation을 적용하지 않는다.

확인된 typed read fault 뒤에는 마지막 수락과 **다른** incarnation이 오기 전에 synchronization을 완료하지 않는다. 복사된 이전 incarnation(A/101)은 Core를 바꾸지 않고, 현재 B/0만 `Synchronized` evidence가 된다. 이 evidence는 `CommunicationLost`를 지우거나 Recovery/Reset 권한을 만들지 않는다.

연결된 observation port에서 output-fault invalidation은 `ConnectAsync`를 추론하지 않는다. 같은 incarnation의 동일/이전 sequence는 `WaitingForFreshInput`으로 남고, 이후 sequence만 재동기화한다.

### P4 exact ACK incarnation fence

`EquipmentCommandRuntime`은 dispatch baseline의 source incarnation과 sequence를 저장한다. 다른 incarnation의 exact command ID는 명령을 완료하지 않고 기존 hold/timeout 경로에 남긴다. 교차 incarnation snapshot은 synchronization evidence가 될 수 있지만 admission release, replay, Core completion을 하지 않는다.

## Validation

- Windows Debug restore/build: 0 warnings / 0 errors
- Full suite at source `fc37338`: Abstractions 22, Core 40, Application 72, Presentation 26, Simulation 20; **180/180**
- Focused comment-repair tests at `7a2ceec`: Application 2/2
- Windows-byte allowlist manifest (git show of the ten paths at `7a2ceec`): `385059c126795da79972fb1564572bfa7193291d50c680a6e8fdbfb046f67442`
- Frozen-byte code review: PASS
- Frozen-byte test/spec review: PASS after comment repair `7a2ceec`

180/180만으로는 review PASS가 아니다.

## Explicit nonclaims

이 source-SHA-bound documentation checkpoint는 다음을 주장하지 않는다.

- P5-T4 qualified fresh-safe-input evidence, post-evidence acknowledgement, `CommunicationLost` clearing, Recovery-ready 또는 Reset 성공
- P5-T5 `CommunicationLost`와 다른 alarm cause의 composite precedence/clear/completion
- Core alarm clearing, command uncertainty clearing, admission release, output retry/replay 또는 automatic compensating output
- Presentation/WinForms UI runtime, connection badge, Alarm rendering, Event Log/UI expansion, current screenshot 또는 operator smoke
- Modbus/TCP adapter/protocol, real PLC/device/equipment/chamber/sensor/heater/actuator behavior
- E-Stop, Safety PLC, hardware safety circuit, device/human/physical safety validation
- push, tag, release 또는 publication

## Reproduction

Windows repository root에서 source commit `7a2ceec` (P5-T3 identity + comment repair)를 checkout한 뒤 실행한다.

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
