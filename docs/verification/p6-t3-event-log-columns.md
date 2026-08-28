# P6-T3 event-log connection and command columns verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `ae99e20b70d45781d441182aeff886fadb41e53b`
- Parent: `e8f6a28`
- Subject: `feat: stamp connection and command onto event log rows`
- Evidence state: Completed — source commit, frozen-byte reviews, and this tracked documentation checkpoint are bound together.

이 tracked documentation checkpoint는 source `ae99e20`와 P6-T3를 `Completed` 상태로 bound한다. Event Log 새 행은 마지막 `EquipmentStatusViewModel`에서 connection과 command를 stamp한다. Core `EventLogEntry` 스키마는 바꾸지 않는다. 이벤트 수가 같으면 ListView를 재구성하지 않는다. P7 캡처는 포함하지 않는다.

## Exact source scope

1. `ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`
2. `ChamberControlSimulator/Form1.Designer.cs`
3. `ChamberControlSimulator/Form1.cs`

## Implemented contract

ListView columns: Time, State, Event, Alarm, Connection, Command. `ShowEquipmentStatus`가 `_lastStatus`를 저장하고 `ShowEventLog`가 새 행에만 stamp한다. `entries.Count == _renderedEventCount`이면 return. count가 줄면 clear 후 replay.

## Validation

- Windows Debug at `ae99e20`: Presentation 29/29; full **190/190**
- Focused: `TimerTicked_ForwardsEventLogWithLatestEquipmentStatus`
- Windows-byte allowlist manifest: `dbc1e85e97bbc3d3b64a57ab020492116d1fabe1bfe3b150b643d62c97eeb3c8`
- Frozen-byte code review: PASS
- Frozen-byte test/spec review: PASS

190/190만으로는 review PASS가 아니다.

## Explicit nonclaims

- Core `EventLogEntry` schema change
- P7 screenshots / operator smoke
- Reset success, Modbus/TCP, real PLC, hardware safety
- push, tag, release

## Reproduction

```powershell
git checkout ae99e20b70d45781d441182aeff886fadb41e53b
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --verbosity minimal
```
