# P6-T2 simulation / fault-injection chrome verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `e8f6a28`
- Parent: `b498319`
- Subject: `feat: separate simulation fault-injection chrome`
- Evidence state: Completed — source commit, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `e8f6a28`와 P6-T2를 `Completed` 상태로 bound한다. 화면은 operator Start/Stop/Reset/Acknowledge와 Simulation / Fault Injection을 시각적으로 구분한다. ACK 억제와 Disconnect는 기존 `VirtualPlcSimulationControl`에만 연결된다. Core 복구 규칙, P6-T3 Event Log 컬럼, P7 캡처는 포함하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`
2. `ChamberControlSimulator/Form1.Designer.cs`
3. `ChamberControlSimulator/Form1.cs`
4. `ChamberControlSimulator/Presentation/EquipmentObservationRuntime.cs`
5. `ChamberControlSimulator/Presentation/EquipmentPresenter.cs`
6. `ChamberControlSimulator/Presentation/IEquipmentObservationRuntime.cs`
7. `ChamberControlSimulator/Presentation/IEquipmentView.cs`
8. `ChamberControlSimulator/Program.cs`

## Implemented contract

`grpSimulationInput` 제목은 `Simulation / Fault Injection`이다. Door, temperature, sensor pause/resume, Suppress ACK, Disconnect가 이 그룹에 있다. operator command 그룹은 Start/Stop/Reset/Acknowledge를 유지한다.

`IPlcObservationInputControl`에는 transport fault API가 없다. `EquipmentObservationRuntime`은 선택적 `VirtualPlcSimulationControl`에 `SuppressNextAcknowledgement`와 `ForceTransportDisconnect`를 위임한다. Presenter는 해당 View 이벤트를 runtime으로만 전달하고 Recovery를 계산하지 않는다.

## Validation

- Windows Debug at `e8f6a28`: Presentation 28/28; full **189/189**
- Focused: `FaultInjectionRequests_AreForwardedThroughObservationRuntimeWithoutOperatorCommands`
- Windows-byte allowlist manifest: `d52a202fc7aafcb211f316900420c69d3a519c0d034988daa31650ecfed7697f`
- Frozen-byte code review: PASS
- Frozen-byte test/spec review: PASS

189/189만으로는 review PASS가 아니다.

## Explicit nonclaims

- P6-T3 Event Log connection/command columns
- P7 screenshots / operator smoke
- Reset success, Modbus/TCP, real PLC, hardware safety
- Expanding `IPlcObservationInputControl` with P4 fault members
- push, tag, release

## Reproduction

```powershell
git checkout e8f6a28
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
