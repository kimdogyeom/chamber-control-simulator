# P5-T5 composite CommunicationLost alarm precedence verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `ee89095b46bc19dadf1fc65e367bc1a8ae7fe098`
- Baseline/parent: `00a1df2f79afb3cfc53130eac39f9d3677eb635f`
- Subject: `test: prove composite CommunicationLost alarms block Recovery-ready`
- Evidence state: Completed — source commit, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `ee89095`와 P5-T5를 `Completed` 상태로 bound한다. 통신 증거만으로 DoorOpen/OverTemperature pending 또는 미해결 P4 hold가 Recovery/Reset을 막는지 증명한다. UI/device/safety, publication은 포함하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Core.Tests/ThermalControllerTests.cs`
2. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`

Source commit `ee89095`에는 위 두 경로만 포함된다. 제품 동작은 P5-T4 Core pending-alarm 규칙과 기존 P4 admission hold에 의존하며, 이 커밋은 그 규칙을 자동 시퀀스로 고정한다.

## Implemented contract

`CommunicationLost`와 `DoorOpen`이 함께 있으면 `ReportFreshSafeCommunicationEvidence` + Acknowledge만으로 Recovery-ready가 되지 않는다. `CommunicationLost`와 `OverTemperature`도 같다.

Core가 Recovery-ready여도 `EquipmentCommandRuntime`의 `ReceiptTimedOut` hold는 `RequestResetAsync`를 `AdmissionRejected`로 남기고 추가 write/Reset 이벤트를 만들지 않는다.

SensorTimeout 동반 케이스는 이 커밋의 신규 테스트가 아니다. 기존 Core SensorTimeout+DoorOpen pending 규칙과 같은 `IsAlarmConditionCleared` 집합을 공유한다.

## Validation

- Windows Debug full suite at `ee89095`: **187/187** (Core 44, Application 75, Abstractions 22, Presentation 26, Simulation 20)
- Focused re-run: `CommunicationLostPlusDoorOpen_CommsOnlyEvidence_DoesNotReachRecoveryReady`, `CommunicationLostPlusOverTemperature_CommsOnlyEvidence_DoesNotReachRecoveryReady`, `RequestResetAsync_WhileReceiptTimedOutHold_RemainsRejectedEvenIfCoreRecoveryReady`
- Windows-byte allowlist manifest (`git show` of the two paths at `ee89095`): `9a48bd4af9b3000eed4e9dee5abc6333b42b05cdd696923d2a6749e27ca3bf76`
- Frozen-byte code review: PASS (no production path change; Core/Application contracts already present)
- Frozen-byte test/spec review: PASS

187/187만으로는 review PASS가 아니다.

## Explicit nonclaims

- Reset 성공을 이 slice가 수행하거나 증명함
- SensorTimeout 전용 신규 테스트 파일 (공유 pending 규칙은 기존 Core 테스트에 있음)
- Presentation/WinForms UI, Event Log 캡처
- Modbus/TCP, real PLC/equipment, E-Stop/Safety PLC/hardware/human safety
- push, tag, release

## Reproduction

```powershell
git checkout ee89095b46bc19dadf1fc65e367bc1a8ae7fe098
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
