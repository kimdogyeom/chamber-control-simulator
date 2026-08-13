# Virtual Thermal Chamber Controller

WinForms 기반 가상 열처리 챔버 제어 시뮬레이터입니다. 상태 전이·안전 인터락·Alarm·Recovery를 Core 규칙으로 분리하고 자동 테스트로 확인합니다.

이 프로젝트는 실제 챔버, PLC, 산업 통신 프로토콜, 온도 센서, 히터를 제어하지 않습니다. 온도, Recipe, 안전 한계, 시간 변화는 학습과 시연을 위한 시뮬레이션 예시값입니다.

## 확인할 수 있는 것

| 확인 대상 | 구현 근거 | 검증 근거 |
| --- | --- | --- |
| 안전 판단의 단일 책임 | 상태 전이, Door 인터락, 과온, SensorTimeout, Recovery, Reset을 `ThermalController`가 소유 | `ChamberControlSimulator.Core.Tests` 22개 |
| UI와 제어 로직 분리 | `Form1 → IEquipmentView → EquipmentPresenter → ThermalController` | `ChamberControlSimulator.Presentation.Tests` 5개 |
| 시간 기반 진행 | WinForms Timer가 Tick을 예약하고, `Stopwatch.Elapsed`가 `Tick(TimeSpan)`으로 전달 | `TimerTicked_ForwardsElapsedTimeToControllerAndRefreshesView` |
| Holding 단계 유지 | 목표 온도 도달 뒤 Recipe의 기본 `HoldDuration` 3초 동안 elapsed를 누적 | `Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete` |
| 운전 중 설정 변경 차단 | Recipe 변경은 `Idle`에서만 Core가 허용 | `SelectRecipe_WhenHeating_KeepsActiveRecipe` |

현재 소스 기준 검증 결과:

```text
Debug build: 0 warnings / 0 errors
MSTest:      27 passed
- Core:      22
- Presenter:  5
```

## UI 증거

아래 화면은 공개 프로젝트 이름으로 실행한 앱 창 전용 캡처다. 실제 챔버·PLC·온도 센서·히터를 제어하는 화면이 아니며, 온도와 안전 입력은 모두 시뮬레이션 값이다.

![IDLE 기준선 — 앱 창만 포함](docs/demo/images/00-idle.png)

`IDLE`, `Standard 250C`, 현재 온도 `20.00 °C`, Door `Closed`, Sensor Feedback `Active`, Active Alarm `None`을 확인할 수 있다. Start는 활성화되어 있고 Acknowledge와 Reset은 비활성화되어 있다.

정상 전이, DoorOpen, OverTemperature, SensorTimeout의 화면 증거와 Event / Alarm Log는 [데모 시나리오](docs/demo/SCENARIOS.md)에 연결했다. 복합 인터락 상태를 포함한 OverTemperature 캡처는 문서의 주석 범위에서만 해석한다.

## 구조 한눈에 보기

```text
Form1 (WinForms View)
  → IEquipmentView event contract
  → EquipmentPresenter
  → ThermalController (Core safety authority)
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

- **Form1**은 버튼·ComboBox·Timer 이벤트를 만들고 Snapshot·Event Log를 화면에 표시합니다.
- **EquipmentPresenter**는 View 요청을 Core에 전달하고 최신 결과를 View에 반영합니다.
- **ThermalController**만 상태 전이와 안전 조건을 판단합니다. Form이나 Presenter가 Alarm·Recovery·Reset을 독자적으로 결정하지 않습니다.

책임 분리와 테스트-불변식 매핑은 [아키텍처와 검증 기록](docs/architecture-and-verification.md)에 정리했습니다.

## 공정과 복구 흐름

```text
Idle → Precheck → Heating → Holding → Cooling → Complete
```

Heating에서 목표 온도에 도달하면 Holding으로 전이합니다. Recipe의 기본 Holding 시간은 3초이며, Controller가 매 Tick의 실제 elapsed를 누적한 뒤 Cooling으로 전이합니다. 이 과정에서 UI thread를 멈추지 않습니다.

안전 조건이 깨지면 정상 전이를 중단합니다.

```text
Active phase
  → Alarm
  → 원인 해소 + Acknowledge + 모든 pending alarm clear
  → Recovery
  → Reset
  → Idle
```

| Alarm | 발생 예시 | Reset 전 필요한 조건 |
| --- | --- | --- |
| `DoorOpen` | 운전 중 Door Open | Door를 닫고 Acknowledge |
| `OverTemperature` | 현재 온도가 safety temperature 이상 | 온도를 safety temperature 미만으로 낮추고 Acknowledge |
| `SensorTimeout` | feedback pause가 timeout 이상 지속 | feedback 재개 후 fresh positive Tick을 확인하고 Acknowledge |

`Reset`은 Alarm을 임의로 지우는 버튼이 아닙니다. Controller가 Recovery-ready라고 판단한 경우에만 Idle로 돌아갈 수 있습니다.

## 검증

### Core safety rules

`ChamberControlSimulator.Core.Tests/ThermalControllerTests.cs`는 정상 전이, Door/온도/SensorTimeout Alarm, 여러 pending alarm, Recovery 재발, Event History, Recipe 정책을 검사합니다.

대표 사례:

```text
Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete
ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit
FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset
FeedbackPaused_AfterTimeout_DoesNotRepeatedlyReassertSensorTimeout
SelectRecipe_WhenHeating_KeepsActiveRecipe
```

### Passive View Presenter wiring

`ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`는 WinForms 창을 띄우지 않고 `FakeEquipmentView`를 사용해 이벤트 전달과 화면 갱신 계약을 검사합니다.

대표 사례:

```text
StartRequested_RendersHeatingSnapshotAndNewEventHistory
TimerTicked_ForwardsElapsedTimeToControllerAndRefreshesView
RecipeSelectionRequested_WhenIdle_RendersSelectedRecipe
RecipeSelectionRequested_WhenHeating_RendersOriginalRecipe
```
