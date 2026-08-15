# P0 현재 동작 불변식

## 목적과 적용 범위

이 문서는 PLC 추상화와 비동기 Coordinator를 도입하기 전에 보존해야 할 현재 동작 의미를 고정한다. 이후 구조 변경은 아래 불변식을 유지하거나, 변경 이유와 새 검증 증거를 별도로 기록해야 한다.

이 문서의 범위는 현재 단일 프로세스 가상 챔버 모델과 Presenter다. 실제 PLC, Modbus/TCP, semantic ACK, 통신 단절·재연결, Safety PLC 또는 하드웨어 안전회로를 검증했다는 뜻이 아니다.

## 검증 기준

| 항목 | 값 |
| --- | --- |
| 브랜치 | `main` |
| 테스트 열거 대상 SHA | `1497a0600e997e0c1d38f7ef971115e09c579563` |
| Core tests | 22 |
| Presentation tests | 5 |
| 전체 tests | 27 |
| 기준선 영수증 | [`baseline-v0.1.md`](baseline-v0.1.md) |

테스트 이름은 다음 명령으로 실제 test runner가 인식한 목록과 대조했다.

```powershell
dotnet test ChamberControlSimulator.slnx `
  --configuration Debug `
  --no-build `
  --no-restore `
  --nologo `
  --list-tests
```

## 필수 불변식과 직접 증거

### INV-P0-01 — 열린 Door는 Idle Start를 차단한다

- 의미: Idle에서 Door가 열려 있으면 `Start()`는 무시되고 상태는 `Idle`에 남으며 `CanStart`는 `false`다.
- 경계: Idle의 열린 Door는 시작 자격 조건 위반이지 즉시 Alarm을 발생시키는 active-phase 인터록은 아니다.
- 구현 지점: `ThermalController.Start()`, `ThermalController.PublishSnapshot()`
- 직접 테스트:
  - `Start_WhenDoorIsOpen_RemainsIdleAndIsIneligible`

### INV-P0-02 — active phase의 DoorOpen은 Alarm을 발생시킨다

- 의미: 안전 감시 중 Door가 열리면 상태는 `Alarm`, active alarm은 `DoorOpen`이 되고 Reset은 차단된다.
- 구현 지점: `ThermalController.SetDoorOpen()`, `ThermalController.IsSafetyMonitored()`
- 직접 테스트:
  - `OpenDoor_WhileHeating_EntersDoorOpenAlarm`
- 증거 한계: 구현은 `Precheck`, `Heating`, `Holding`, `Cooling`을 모두 active phase로 분류하지만 현재 직접 테스트는 `Heating`만 실행한다.

### INV-P0-03 — safety temperature 이상은 OverTemperature Alarm이다

- 의미: 안전 감시 중 현재 온도가 recipe의 safety temperature와 같거나 높으면 `OverTemperature` Alarm이 발생한다. 온도가 기준 미만으로 내려가고 Alarm이 acknowledge되어야 Recovery로 이동할 수 있다.
- 구현 지점: `ThermalController.ReportTemperature()`, `ThermalController.IsAlarmConditionCleared()`
- 직접 테스트:
  - `ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit`
- 관련 Start gate 테스트:
  - `Start_WhenCurrentTemperatureIsAtSafety_RemainsIdleUntilSafeTemperatureReported`

### INV-P0-04 — SensorTimeout 후 Resume만으로 Reset할 수 없다

- 의미: feedback timeout 발생 뒤 `ResumeFeedback()` 호출만으로는 fresh sensor input을 증명하지 않는다. Resume 이후 양의 elapsed time을 가진 fresh tick이 있어야 Recovery 조건을 만족한다.
- 구현 지점: `ThermalController.ResumeFeedback()`, `ThermalController.Tick()`, `ThermalController.IsAlarmConditionCleared()`
- 직접 테스트:
  - `FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset`
  - `FeedbackTimeout_AtExactBoundaryRequiresPositiveFreshTickAfterResume`

### INV-P0-05 — 모든 pending Alarm 원인이 해소되어야 Recovery 가능하다

- 의미: DoorOpen, OverTemperature, SensorTimeout이 복합 발생하면 acknowledge만으로 Recovery할 수 없다. `_pendingAlarms`의 모든 원인이 해소되어야 한다.
- 구현 지점: `ThermalController.TryMarkRecoveryReady()`
- 직접 테스트:
  - `DoorOpenThenAtSafetyTemperature_TracksBothInterlocksBeforeRecovery`
  - `SensorTimeoutThenDoorOpen_TracksBothInterlocksBeforeRecovery`

### INV-P0-06 — Alarm 중 Stop으로 안전 상태를 우회할 수 없다

- 의미: `Alarm` 또는 `Recovery` 상태에서 `Stop()`은 상태와 Alarm을 지우지 않는다. 이어지는 `Start()`도 차단된다.
- 구현 지점: `ThermalController.Stop()`, `ThermalController.Start()`
- 직접 테스트:
  - `Stop_WhenAlarmed_PreservesAlarmAndBlocksRestart`

### INV-P0-07 — active phase에서는 Recipe를 변경할 수 없다

- 의미: Idle이 아닌 상태에서 recipe 선택 요청은 `false`를 반환하고 기존 recipe와 target temperature를 유지한다. Presenter도 그 원래 상태를 다시 렌더한다.
- 구현 지점: `ThermalController.SelectRecipe()`, `EquipmentPresenter.OnRecipeSelectionRequested()`
- 직접 테스트:
  - `SelectRecipe_WhenHeating_KeepsActiveRecipe`
  - `RecipeSelectionRequested_WhenHeating_RendersOriginalRecipe`
- 증거 한계: 현재 직접 테스트는 `Heating`만 실행하며 다른 non-Idle 상태를 각각 parameterize하지 않는다.

### INV-P0-08 — Event History는 외부에서 변경할 수 없다

- 의미: 외부가 `EventHistory`를 `IList<EventLogEntry>`로 보더라도 항목 추가는 `NotSupportedException`을 발생시키고 controller 내부 history는 변하지 않는다.
- 구현 지점: constructor의 `_events.AsReadOnly()`, `ThermalController.EventHistory`
- 직접 테스트:
  - `EventHistory_CannotBeMutatedOutsideTheController`

## 추가로 보존할 현재 의미

### INV-P0-09 — 정상 cycle의 phase와 event 순서는 결정적이다

- 의미: 정상 cycle은 `Precheck → Heating → Holding → Cooling → Complete`로 진행하고 대응 event history 순서를 유지한다.
- 직접 테스트:
  - `Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete`

### INV-P0-10 — Stop과 Reset은 기존 session history를 지우지 않는다

- 의미: 정상 active phase의 Stop과 Alarm recovery 이후 Reset은 기존 event history를 보존하고 마지막에 각각 `Stop`, `Reset` event를 추가한다.
- 직접 테스트:
  - `Stop_WhenHeating_ReturnsIdleAndPreservesSessionHistory`
  - `Reset_PreservesEntirePreResetSessionHistory`

### INV-P0-11 — Recovery 중 Alarm 재발생은 새 clear cycle을 요구한다

- 의미: Recovery 상태에서 Alarm 조건이 다시 발생하면 `Alarm`으로 돌아가 Reset을 차단한다. OverTemperature와 SensorTimeout은 새 acknowledgement와 조건 해소를 다시 요구한다.
- 직접 테스트:
  - `DoorOpen_ReassertedFromRecovery_ReturnsToAlarmAndBlocksReset`
  - `OverTemperature_ReassertedFromRecovery_RequiresNewAcknowledgementAndClearCycleBeforeReset`
  - `SensorTimeout_ReassertedFromRecovery_RequiresNewAcknowledgementAndClearCycleBeforeReset`

### INV-P0-12 — 동일한 지속 SensorTimeout은 event를 반복 추가하지 않는다

- 의미: feedback이 계속 pause된 상태에서 timeout 이후 tick이 반복되어도 동일 SensorTimeout event는 최초 한 건만 기록한다.
- 직접 테스트:
  - `FeedbackPaused_AfterTimeout_DoesNotRepeatedlyReassertSensorTimeout`

### INV-P0-13 — Idle Recipe 선택은 Core와 Presenter에 반영된다

- 의미: Idle에서 유효한 다른 recipe를 선택하면 Core snapshot과 Presenter view에 새 recipe 이름과 target temperature가 반영된다.
- 직접 테스트:
  - `SelectRecipe_WhenIdle_ActivatesSelectedRecipe`
  - `RecipeSelectionRequested_WhenIdle_RendersSelectedRecipe`

## 현재 coverage gap

다음 항목은 현재 구현 의도나 소스 분기로는 존재하지만, P0의 직접 자동 테스트 증거가 충분하지 않다.

1. DoorOpen과 recipe 변경 차단은 모든 active/non-Idle 상태가 아니라 `Heating`에서만 직접 테스트된다.
2. Presenter의 Stop, Acknowledge, Reset, Door, temperature, feedback pause/resume event handler에는 전용 테스트가 없다.
3. GUI는 초기 Idle 화면만 수동 확인했다. 전체 cycle과 Alarm/Recovery GUI 흐름은 아직 자동화된 end-to-end 테스트가 아니다.
4. Release configuration은 P0에서 실행하지 않았다.
5. PLC I/O, transport write, semantic ACK, timeout, disconnect, reconnect, stale snapshot은 아직 구현·검증 범위 밖이다.
6. 이 controller 인터록은 포트폴리오용 가상 모델이며 실제 장비의 Safety PLC 또는 하드웨어 안전 기능을 대체하지 않는다.

## 변경 규칙

- 위 불변식을 깨는 변경은 조용히 기존 테스트를 수정해서 통과시키지 않는다.
- 의미 변경이 필요하면 변경 이유, 영향받는 invariant ID, 새 테스트와 검증 결과를 기록한다.
- P1 이후 구조 변경은 mapped test의 이름과 행위가 유지되는지 전체 회귀 테스트로 확인한다.
- coverage gap을 해소한 테스트는 해당 invariant 아래에 직접 증거로 추가한다.
