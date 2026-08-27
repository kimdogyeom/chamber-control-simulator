# P5-T4 fresh-safe CommunicationLost recovery verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `00a1df2f79afb3cfc53130eac39f9d3677eb635f`
- Baseline/parent: `d2e9f0cf2a2096121f6970907c72db9598e955d5`
- Subject: `feat: qualify CommunicationLost recovery after fresh-safe evidence`
- Evidence state: Completed — source commit, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `00a1df2`와 P5-T4를 `Completed` 상태로 bound한다. 아래 evidence는 동기화된 안전 입력 뒤 **새 Acknowledge로 Recovery-ready**가 되는 경로만 설명한다. Reset 성공, UI/device/safety, publication은 포함하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Core/ThermalController.cs`
2. `ChamberControlSimulator.Core.Tests/ThermalControllerTests.cs`
3. `ChamberControlSimulator.Application/EquipmentCoordinator.cs`
4. `ChamberControlSimulator.Application.Tests/EquipmentCoordinatorTests.cs`

Source commit `00a1df2`에는 위 네 경로만 포함된다.

## Implemented contract

`ThermalController.ReportFreshSafeCommunicationEvidence()`는 pending `CommunicationLost`가 있고 문이 닫혀 있으며 온도가 safety 미만이고 feedback이 pause가 아닐 때만 증거를 기록한다. 증거는 기존 Acknowledge를 무효화하므로 **증거 이후의 새 Acknowledge**가 필요하다.

`IsAlarmConditionCleared(CommunicationLost)`는 이 증거가 있을 때만 true다. 증거 없는 Acknowledge, Stop, Reset은 알람을 우회하지 못한다.

`EquipmentCoordinator`는 source-fresh `Completed` cycle에서 `DoorClosed && SensorHealthy`일 때만 Core에 증거를 보고한다. 문 열린 동기화 입력은 증거를 올리지 않는다.

이 slice는 `Reset()`을 호출하지 않는다. Recovery-ready에서 `CanReset`이 true여도 운영자/별도 요청이 오기 전까지 Reset 이벤트는 없다.

## Validation

- Windows Debug full suite at `00a1df2`: **184/184** (Core 42, Application 74, Abstractions 22, Presentation 26, Simulation 20)
- Focused re-run: Core `ReportFreshSafeCommunicationEvidence_ThenNewAcknowledge_ReachesRecoveryReadyWithoutReset`, `AcknowledgeAlarm_AfterCommunicationLostWithOpenDoor_DoesNotBecomeRecoveryReady`, `ReportCommunicationLost_OnlySafetyMonitoredControllerRaisesNonBypassableAlarm`; Application `CycleAsync_AfterSynchronizedSafeInput_NewAcknowledgeReachesRecoveryReadyWithoutReset`, `CycleAsync_AfterSynchronizedOpenDoor_AcknowledgeDoesNotReachRecoveryReady`
- Windows-byte allowlist manifest (`git show` of the four paths at `00a1df2`): `5900dd7f62240513a7e39160ec307cbecb9260ccad6102997105343c5f95b0f4`
- Frozen-byte code review: PASS
- Frozen-byte test/spec review: PASS

184/184만으로는 review PASS가 아니다.

## Explicit nonclaims

- Reset 성공, Reset 자동 호출, command admission release, output retry/replay
- P5-T5 composite precedence를 이 receipt만으로 완료 주장 (T5는 별도 source `ee89095` / receipt)
- Presentation/WinForms UI, Event Log 캡처, connection badge
- Modbus/TCP, real PLC/equipment, E-Stop/Safety PLC/hardware/human safety
- push, tag, release

## Reproduction

```powershell
git checkout 00a1df2f79afb3cfc53130eac39f9d3677eb635f
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
