# P5-T1 confirmed typed communication-loss verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `8fabaebf79b01a23129067a079ac5443c64c99ca`
- Baseline/parent: `ef01e0994e93912cfcf362e7c9a01c7d781ab4d6`
- Source tree: `2436c3a277161b68b653a47de040bc53478949c9`
- Subject: `feat: record PLC communication-loss alarms`
- Evidence state: completed source and source-SHA-bound tracked documentation checkpoint.

`Completed`는 이 source SHA에서 P5-T1의 confirmed typed communication-loss alarm boundary와 P4 hold 보존이 구현되고 Windows validation 및 frozen-byte reviews를 통과했다는 뜻이다. reconnect, synchronization, recovery, current UI runtime, Modbus/device/safety, push/release 완료를 뜻하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`
2. `ChamberControlSimulator.Application.Tests/EquipmentCoordinatorTests.cs`
3. `ChamberControlSimulator.Application/EquipmentCommandRuntime.cs`
4. `ChamberControlSimulator.Application/EquipmentCoordinator.cs`
5. `ChamberControlSimulator.Core.Tests/ThermalControllerTests.cs`
6. `ChamberControlSimulator.Core/Models.cs`
7. `ChamberControlSimulator.Core/ThermalController.cs`
8. `ChamberControlSimulator.Plc.Abstractions/PlcTransportException.cs`
9. `ChamberControlSimulator.Plc.Simulation.Tests/VirtualPlcClientLifecycleTests.cs`
10. `ChamberControlSimulator.Plc.Simulation/VirtualPlcClient.cs`

Source commit에는 위 열 경로만 포함되고 documentation, Presentation, WinForms, project, Modbus adapter 경로는 포함되지 않는다.

## Implemented contract

### Core alarm ownership and hold

`AlarmKind.CommunicationLost`와 `ThermalController.ReportCommunicationLost()`가 Core에 있다. Core는 safety-monitored active state에서만 alarm을 올리고 pending cause를 소유한다. P5-T1에는 이 cause의 automatic clearing predicate가 없으므로 Acknowledge 뒤에도 Stop/Reset으로 우회하거나 정상 progression을 재개할 수 없다. Idle의 직접 보고는 state, active alarm, event history를 변경하지 않는다.

### Confirmed read classification

`EquipmentCoordinator`는 connection attempt와 active read를 분리한다.

- `ConnectAsync`가 typed `PlcTransportException`으로 실패하면 cycle은 non-alarm `TransportFailed`를 반환한다. read/write는 호출하지 않고 active Core state를 그대로 둔다.
- connected active `ReadInputsAsync`의 typed failure는 Core `CommunicationLost`를 보고하고 `TransportFailed`를 반환한다. output write는 없다.
- `PlcConnectionState.Faulted`에서도 reconnect를 시도하지 않고 read 1회가 typed failure에 도달한다. 결과는 `Faulted`, `CommunicationLost`, connect 0회, write 0회다.

### Confirmed write classification and P4 fences

`EquipmentCommandRuntime`은 실제 `WriteOutputsAsync`가 시작된 뒤 settle한 typed `PlcTransportException`을 Core `CommunicationLost`로 보고한다.

- timely typed Stop write failure는 exception을 보존하고 `ReconciliationRequired`, 원래 command ID/kind, closed admission, one write, no Stop event를 유지한다.
- write invocation 전 injected `TimeProvider`가 같은 exception type을 던지면 communication alarm을 올리지 않는다. command uncertainty fence는 남지만 physical write count는 0이다.
- receipt timeout 뒤 늦은 typed write failure는 alarm을 보고해도 `ReceiptTimedOut`, 원래 command ID/kind와 shared-gate settlement ordering을 바꾸지 않는다.
- exact receipt deadline의 typed write failure에서도 timeout이 tie를 이긴다. 결과는 `ReceiptTimedOut`, command ID `1`, Stop kind, closed admission, write 1회, no retry/replay, no Stop event다.

`CommunicationLost` 보고는 P4 timeout/reconciliation evidence를 대체하거나 reservation을 release하는 recovery shortcut이 아니다.

## Windows validation and frozen reviews

Source commit 및 이 documentation write 직전 authoritative Windows rerun 결과:

- PLC abstractions: **19/19 passed**
- Core: **40/40 passed**
- Presentation: **26/26 passed**
- Application: **56/56 passed**
- PLC simulation: **20/20 passed**
- Full solution: **161/161 passed**
- Debug solution build: **0 warnings / 0 errors**
- Source `git diff --check` before commit: **passed**
- Final independent frozen-byte code review: **PASS**
- Final independent frozen-byte test/spec review: **PASS**
- Review manifest SHA-256: `40c624dc133a00c3f88a93531b6a9b23a8215d7901f5cca2d80e91c52108f706`

Automated S08 evidence는 다음 named regressions를 포함한다.

- `ReportCommunicationLost_OnlySafetyMonitoredControllerRaisesNonBypassableAlarm`
- `CycleAsync_WhenConnectThrowsTransportException_ReturnsNonAlarmTransportFailure`
- `CycleAsync_WhenReadThrowsTransportException_RaisesCommunicationLostWithoutWrite`
- `CycleAsync_WhenFaultedReadThrowsTransportException_RaisesCommunicationLostWithoutReconnectOrWrite`
- `RequestStopAsync_TransportFailure_RaisesCommunicationLostAndPreservesReconciliationHold`
- `RequestStopAsync_TimeProviderTransportFailureBeforeWrite_DoesNotRaiseCommunicationLost`
- `RequestStopAsync_LateTransportFailureAfterReceiptTimeout_RaisesCommunicationLostAndPreservesHold`
- `RequestStopAsync_TransportFailureAtExactReceiptDeadline_TimeoutWinsAndRaisesCommunicationLost`
- `ForceTransportDisconnect_RejectsReadAndWriteWithTransportExceptionUntilReconnect`

## Reproduction

Windows repository root에서 실행한다.

```powershell
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

## Explicit nonclaims

이 checkpoint는 다음을 주장하지 않는다.

- P5-T2 bounded reconnect/backoff, socket restoration, automatic retry 또는 reconnect 횟수 evidence
- P5-T3 connected-but-unsynchronized 상태, session/epoch freshness 또는 stale pre-fault input rejection
- P5-T4 fresh-safe-input/acknowledgement recovery, Recovery-ready 또는 Reset 성공
- P5-T5 `CommunicationLost` + 다른 cause의 compound-fault recovery completion
- Presentation/WinForms UI runtime, connection badge, Alarm rendering, Event Log/UI expansion, current screenshot 또는 operator smoke
- production Modbus/TCP adapter, real PLC, chamber, sensor, heater, actuator 또는 실제 equipment behavior
- E-Stop, Safety PLC, hardware safety circuit, device/human/physical safety validation
- push, tag, release 또는 publication
