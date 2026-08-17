# 데모 시나리오

> **Evidence status — P0 historical baseline:** 이 문서의 UI 흐름, Normal Cycle 절차, screenshots는 direct `Form1 → EquipmentPresenter → ThermalController` wiring에서 만든 P0 baseline evidence다. P3-T4 source `2e502fa`는 `EquipmentCoordinator`와 Virtual PLC observation runtime을 WinForms composition root에 연결했지만, 아래 images와 procedures는 그 source SHA에 bound된 current UI evidence가 아니다. 특히 P3-T3 이후 legacy `Tick`은 synthetic temperature나 normal phase를 진행하지 않으므로 아래 Normal Cycle은 current UI reproduction procedure가 아니다. current solution baseline `9c3ad95`에서 user-driven Session 1 manual smoke는 observed input `20 → 30 → Apply` 후 `30.00 °C` rendering과 Idle 유지를 확인했으며, UI 종료 후 process absence도 확인했다. 이 좁은 input-to-render/close evidence는 [`docs/verification/p3-t4-winforms-observation-composition.md`](../verification/p3-t4-winforms-observation-composition.md)에 기록하며, 아래 images와 procedures를 current normal-cycle, PLC command write, semantic ACK, 또는 current screenshot evidence로 바꾸지 않는다.

이 문서는 P0 baseline에서 재현한 UI 흐름과 당시 automated test identifier를 보존한다. 현재 source policy의 증거는 source-SHA-bound verification receipt를 사용하며, 이 문서의 historical procedure가 이를 대체하지 않는다. 이 프로젝트는 가상 시뮬레이터이며 실제 장비·PLC·온도 센서·히터를 제어하지 않는다.

P0 capture 환경에서는 Visual Studio에서 `ChamberControlSimulator.slnx`를 열고 `ChamberControlSimulator`를 시작 프로젝트로 설정했다. 이 설정만으로는 P3-T3 이후 기존 automatic thermal progression을 현재 UI에서 재현할 수 없다.

## 공통 확인 항목

모든 시나리오에서 다음 영역을 함께 확인한다.

- 상단 Controller state badge
- Equipment Status: 상태, 현재/목표 온도, Door, Sensor Feedback, Active Alarm, Recovery Ready
- Event History의 최신 행
- Safety / Simulation Input 버튼의 enabled 상태

캡처를 추가할 때는 앱 창만 포함하고, 다른 프로그램·바탕화면·개인 정보는 프레임에 넣지 않는다.

## 1. P0 historical normal cycle and Holding

아래 sequence, expected result, screenshot은 P3-T3 이전 direct UI baseline에서 기록한 historical evidence다. 현재 `b949e6c` source에서는 Presenter timer가 `Tick`만 호출하고, `Tick`은 plant temperature나 normal phase를 전진시키지 않는다. 따라서 아래 steps는 current UI reproduction procedure가 아니다.

P0 capture sequence:

1. Recipe가 `Standard 250C`인지 확인했다.
2. **Start**를 눌렀다.
3. Timer-driven synthetic progression으로 `Heating`과 `Holding` badge, Event History를 확인했다.
4. 기본 3초 Holding 뒤 `Cooling` 전이를 확인했다.
5. ambient temperature에서 `Complete`를 확인했다.

P0 historical expected result:

```text
Idle → Precheck → Heating → Holding → Cooling → Complete
```

P0 historical automated identifier (current source에는 존재하지 않음):

```text
Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete
```

Current source policy proof (UI runtime evidence 아님):

```text
ApplyObservation_ValidObservedSequence_ProgressesThroughNormalPhasesToComplete
```

이 current Core test는 observed target, hold elapsed, ambient observation을 `ApplyObservation(...)`으로 제공해 phase sequence를 검증한다. P3-T4 source `2e502fa`는 Coordinator/Virtual PLC observation을 WinForms composition root에 연결했다. user-driven P3-T4 Session 1 smoke는 observed input-to-render/close만 보조로 확인하며, 이 Core test와 P0 screenshot을 post-fix current normal-cycle UI flow evidence로 바꾸지 않는다. P3-T4 automated runtime proof와 manual-smoke boundary는 verification receipt를 따른다.

### P0 화면 증거

![IDLE 기준선 — 앱 창만 포함](images/00-idle.png)

- `IDLE`, `Standard 250C`, 현재 온도 `20.00 °C`, Door `Closed`, Sensor Feedback `Active`, Active Alarm `None`
- Start 활성화, Acknowledge·Reset 비활성화

![HOLDING 상태 — 앱 창만 포함](images/01-holding.png)

- `HOLDING`, 현재·목표 온도 `250.00 °C`, Door `Closed`, Active Alarm `None`
- Event / Alarm Log: `Start → Phase: Precheck → Phase: Heating → Phase: Holding`

![COOLING 상태 — 앱 창만 포함](images/02-cooling.png)

- `COOLING`, 현재 온도 `213.25 °C`, Door `Closed`, Active Alarm `None`
- Event / Alarm Log에 `Phase: Cooling`이 추가된 중간 전이

![COMPLETE Event Log — 앱 창만 포함](images/03-complete.png)

- `COMPLETE`, Active Alarm `None`
- Event / Alarm Log에 `Precheck → Heating → Holding → Cooling → Complete`가 순서대로 기록됨

## 2. DoorOpen Alarm과 Recovery

1. Idle에서 **Start**를 눌러 active phase로 진입한다.
2. **Open Door**를 누른다.
3. `Alarm` badge와 `Active Alarm : DoorOpen`을 확인한다.
4. **Close Door**를 누른다.
5. **Acknowledge**를 누른다.
6. `Recovery`와 `Recovery Ready : Yes`를 확인한다.
7. **Reset**을 눌러 `Idle`로 돌아가는지 확인한다.

기대 결과:

```text
active phase → Alarm(DoorOpen) → Recovery → Idle
```

자동 근거:

```text
OpenDoor_WhileHeating_EntersDoorOpenAlarm
Reset_AfterDoorAlarmIsClosedAndAcknowledged_ReturnsToIdle
```

### 화면 증거

![DoorOpen Alarm — 앱 창만 포함](images/04-door-open-alarm.png)

- `ALARM`, Door `Open`, Active Alarm `DoorOpen`, Recovery Ready `No`, Acknowledge 활성화
- Event / Alarm Log 마지막 행: `Alarm: DoorOpen`

![DoorOpen Recovery — 앱 창만 포함](images/05-door-recovery.png)

- `RECOVERY`, Door `Closed`, Active Alarm `DoorOpen`, Recovery Ready `Yes`, Reset 활성화
- Event / Alarm Log: `Alarm: DoorOpen → Acknowledgement → Recovery ready`

## 3. OverTemperature Alarm과 Recovery

1. `Standard 250C` Recipe를 선택한다. safety temperature는 300C다.
2. **Start**를 누른다.
3. Simulation Input에 `300`을 넣고 **Apply Temperature**를 누른다.
4. `Alarm` badge와 `Active Alarm : OverTemperature`을 확인한다.
5. Simulation Input을 `299` 이하로 바꾸고 **Apply Temperature**를 누른다.
6. **Acknowledge**를 누른다.
7. `Recovery`가 보이는지 확인한 뒤 **Reset**을 눌러 Idle로 돌아간다.

자동 근거:

```text
ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit
```

### 화면 증거

![OverTemperature Event Log — 앱 창만 포함](images/06-over-temperature-alarm.png)

- 현재 온도와 Simulation Input이 `300.0 °C`이며 Event / Alarm Log에 `Alarm: OverTemperature`가 기록됨
- 이 캡처는 앞선 DoorOpen이 아직 pending인 복합 인터락 세션이다. 따라서 Safety / Interlock의 Active Alarm 표시는 `DoorOpen`이며, 단독 과온 Alarm 화면으로 해석하지 않는다.

![OverTemperature 이후 복합 Recovery — 앱 창만 포함](images/07-over-temperature-recovery.png)

- Event / Alarm Log에는 `Alarm: OverTemperature → Acknowledgement → Recovery ready`가 남음
- Active Alarm이 `DoorOpen`으로 남은 이유도 같은 pending interlock 때문이다. Reset은 모든 pending alarm이 clear된 뒤에만 가능하다.

이 화면은 여러 pending interlock을 함께 추적하는 흐름의 Event Log 증거다. fresh run의 단독 OverTemperature 상태·Recovery 조건은 위 UI 절차와 `ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit` 자동 테스트를 함께 근거로 사용한다.

## 4. SensorTimeout Alarm과 Recovery

1. Idle에서 **Start**를 누른다.
2. **Pause Feedback**을 누른다.
3. SensorTimeout(3초)을 넘길 때까지 기다린다.
4. `Alarm` badge와 `Active Alarm : SensorTimeout`을 확인한다.
5. **Resume Feedback**을 누른다.
6. 다음 positive Timer Tick이 처리된 뒤 **Acknowledge**를 누른다.
7. `Recovery`를 확인하고 **Reset**으로 Idle에 돌아간다.

중요: Resume 직후 즉시 Reset할 수 없어야 한다. Controller는 fresh positive Tick까지 기다린다.

자동 근거:

```text
FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset
FeedbackPaused_AfterTimeout_DoesNotRepeatedlyReassertSensorTimeout
```

### 화면 증거

![SensorTimeout Alarm — 앱 창만 포함](images/08-sensor-timeout-alarm.png)

- `ALARM`, Sensor Feedback `Paused`, Active Alarm `SensorTimeout`, Recovery Ready `No`
- Event / Alarm Log 마지막 행: `Alarm: SensorTimeout`

![SensorTimeout Recovery — 앱 창만 포함](images/09-sensor-timeout-recovery.png)

- `RECOVERY`, Sensor Feedback `Active`, Active Alarm `SensorTimeout`, Recovery Ready `Yes`, Reset 활성화
- Event / Alarm Log: `Alarm: SensorTimeout → Acknowledgement → Recovery ready`
