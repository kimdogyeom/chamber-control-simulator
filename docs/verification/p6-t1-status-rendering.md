# P6-T1 connection / synchronization / command status rendering verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `ad7e5fc3cddfc03c6bef30d003fba418fa4a406a`
- Parent: `1319c59`
- Subject: `feat: render PLC connection sync and command status`
- Evidence state: Completed — source commit, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `ad7e5fc`와 P6-T1을 `Completed` 상태로 bound한다. View는 Application cycle/command 결과를 표시할 뿐 복구 규칙을 계산하지 않는다. P6-T2 시뮬레이션 분리, P6-T3 Event Log 컬럼, P7 캡처는 포함하지 않는다.

## Exact source scope

1. `ChamberControlSimulator/Presentation/EquipmentStatusViewModel.cs` (create)
2. `ChamberControlSimulator/Presentation/IEquipmentView.cs`
3. `ChamberControlSimulator/Presentation/IEquipmentObservationRuntime.cs`
4. `ChamberControlSimulator/Presentation/IEquipmentCommandRuntime.cs`
5. `ChamberControlSimulator/Presentation/EquipmentObservationRuntime.cs`
6. `ChamberControlSimulator/Presentation/EquipmentPresenter.cs`
7. `ChamberControlSimulator/Form1.cs`
8. `ChamberControlSimulator/Form1.Designer.cs`
9. `ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`

## Implemented contract

`EquipmentStatusViewModel`은 `PlcConnectionState`, `ConnectionSynchronizationState`, command disposition/ID/kind를 담는다. Presenter는 `EquipmentCommandCycleResult`와 `EquipmentCommandRuntime.CurrentState`를 매핑하고 Form은 라벨만 그린다. 버튼 Enabled는 `ControllerSnapshot` 플래그 복사다.

Displayed connection values are the existing port enum: Disconnected, Connecting, Connected, Faulted. Reconnecting is not a `PlcConnectionState` value; WaitingForFreshInput remains the synchronization field.

## Validation

- Windows Debug at `ad7e5fc`: Presentation 27/27; full **188/188** (Abstractions 22, Core 44, Application 75, Presentation 27, Simulation 20)
- Mapping test: `TimerTicked_MapsConnectionSynchronizationAndCommandWithoutJudgingRecovery` (Connected + WaitingForFreshInput + AwaitingAck; CanReset from snapshot)
- Windows-byte allowlist manifest: `89f1b835e7f8cfc379ffbc544f0dd110e57a0ea4137e2b12cf7041964065c37c`
- Frozen-byte code review: PASS
- Frozen-byte test/spec review: PASS

188/188만으로는 review PASS가 아니다.

## Explicit nonclaims

- P6-T2 Simulation/Fault Injection grouping or ACK-suppress/disconnect buttons
- P6-T3 Event Log connection/command columns
- P7 screenshots / operator smoke
- Reset success, Modbus/TCP, real PLC, hardware safety
- Adding Reconnecting to `PlcConnectionState`
- push, tag, release

## Reproduction

```powershell
git checkout ad7e5fc3cddfc03c6bef30d003fba418fa4a406a
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
