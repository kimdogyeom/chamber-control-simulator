# 아키텍처와 검증 기록

## 1. 목표와 경계

이 프로젝트는 C# WinForms로 만든 **가상 열처리 챔버 제어 시뮬레이터**다. 상태 전이와 안전 조건을 UI에서 분리해 테스트 가능한 규칙으로 다룬다.

이 저장소는 실제 챔버·PLC·산업 통신·센서·히터와 연결되지 않는다. 온도, 시간, Recipe, 안전 한계는 동작을 설명하기 위한 예시값이며, 실제 공정 조건이나 장비 제어 값을 의미하지 않는다.

## 2. 책임 분리

```text
Form1
  → IEquipmentView
  → EquipmentPresenter
  → ThermalController
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

| 구성 요소 | 책임 | 하지 않는 일 |
| --- | --- | --- |
| `Form1` | 버튼·ComboBox·Timer 이벤트 발생, Stopwatch elapsed 전달, Snapshot과 Event Log 표시 | Alarm·Recovery·Reset 안전 판단 |
| `IEquipmentView` | View 입력 이벤트와 View 갱신 메서드 계약 | Core 규칙 소유 |
| `EquipmentPresenter` | View 이벤트를 Controller 호출로 연결하고 결과를 View에 반영 | 상태 전이·인터락 직접 결정 |
| `ThermalController` | 상태 전이, Door 인터락, 과온, SensorTimeout, Alarm, Recovery, Reset, Recipe 선택 정책, Event History | WinForms Control 접근 |

`ThermalController`가 안전 상태를 최종 판단한다. Form이나 Presenter는 조건을 해석해 Alarm을 임의로 해제하지 않는다.

## 3. 시간 흐름

WinForms `Timer`는 Tick을 발생시키는 스케줄러 역할만 한다. 실제 Tick 간격은 UI 부하에 따라 달라질 수 있으므로 `Form1`은 `Stopwatch.Elapsed`를 측정해 `TimerTickedEventArgs`로 Presenter에 전달한다. Presenter는 그 값을 `ThermalController.Tick(TimeSpan elapsed)`으로 전달한다.

```text
WinForms Timer Tick
  → Stopwatch.Elapsed
  → TimerTickedEventArgs
  → EquipmentPresenter
  → ThermalController.Tick(TimeSpan)
  → ControllerSnapshot / EventHistory
  → Form1 rendering
```

Core는 온도 변화·Holding 누적·SensorTimeout을 `TimeSpan` 기준으로 계산한다. `Thread.Sleep`, UI thread block, `while` 기반 UI 제어 루프는 사용하지 않는다.

## 4. 상태 전이

정상 경로는 아래와 같다.

```text
Idle → Precheck → Heating → Holding → Cooling → Complete
```

- `Idle`: Recipe 선택이 가능한 준비 상태
- `Precheck`: 시작 전 조건을 점검하는 전이 단계
- `Heating`: 목표 온도까지 가상 온도를 올리는 단계
- `Holding`: 목표 도달 후 Recipe의 `HoldDuration`만큼 유지하는 단계
- `Cooling`: ambient temperature까지 가상 온도를 내리는 단계
- `Complete`: 정상 공정이 끝난 상태

`Holding`은 기본 3초다. Controller는 Holding 진입 때 누적 시간을 0으로 초기화하고, 이후 `Tick(elapsed)`마다 elapsed를 더한다. 누적값이 `HoldDuration` 이상일 때만 Cooling으로 전이한다. 화면·Timer·사용자 입력은 Holding 중에도 계속 반응한다.

## 5. Alarm과 Recovery

운전 단계에서 안전 조건이 깨지면 정상 전이를 중단하고 Alarm으로 전이한다.

```text
Active phase
  → Alarm
  → 원인 해소 + Acknowledge + 모든 pending alarm clear
  → Recovery
  → Reset
  → Idle
```

| Alarm | 발생 조건 | Recovery 전 조건 |
| --- | --- | --- |
| `DoorOpen` | 활성 공정 중 Door가 열림 | Door를 닫고 Acknowledge |
| `OverTemperature` | 현재 온도가 Recipe safety temperature 이상 | 온도를 safety temperature 미만으로 낮추고 Acknowledge |
| `SensorTimeout` | feedback pause가 timeout 이상 지속 | feedback을 재개하고 fresh positive Tick 뒤 Acknowledge |

Alarm 중 Stop 요청은 안전 상태를 우회하지 못한다. Reset은 Alarm을 즉시 지우는 동작이 아니다. Controller가 Recovery-ready라고 판단한 경우에만 Idle로 복귀한다.

여러 Alarm 원인이 동시에 존재할 수 있다. Controller는 pending alarm을 집합으로 유지하며, 하나를 해소해도 다른 원인이 남아 있으면 Recovery로 진행하지 않는다. Recovery 중 원인이 재발하면 다시 Alarm으로 돌아간다.

## 6. Recipe 선택 정책

Recipe는 `Idle`에서만 선택할 수 있다. Heating·Holding·Cooling·Alarm·Recovery 중 선택 변경 요청이 들어와도, Core는 현재 Recipe를 유지한다. UI의 ComboBox 활성화 여부는 이 Core 결과를 반영할 뿐, 정책을 소유하지 않는다.

## 7. 테스트-불변식 매핑

### Core: 22 MSTest

`ChamberControlSimulator.Core.Tests/ThermalControllerTests.cs`는 UI를 띄우지 않고 Controller의 규칙을 검증한다.

| 대표 테스트 | 확인하는 불변식 |
| --- | --- |
| `Start_ValidRecipe_ProgressesThroughNormalPhasesToComplete` | 정상 상태 전이와 Holding duration 경계 |
| `ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit` | safety limit 이상에서 Alarm, 미만이 될 때까지 해소 불가 |
| `FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset` | 재개 신호만으로는 부족하고 fresh Tick이 필요 |
| `FeedbackPaused_AfterTimeout_DoesNotRepeatedlyReassertSensorTimeout` | 지속 fault가 Event History를 중복 오염하지 않음 |
| `SelectRecipe_WhenHeating_KeepsActiveRecipe` | 운전 중 Recipe 변경은 Core가 거부 |

### Presenter: 5 MSTest

`ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`는 `FakeEquipmentView`를 이용해 화면을 띄우지 않고 이벤트 전달과 갱신 계약을 검증한다.

| 대표 테스트 | 확인하는 계약 |
| --- | --- |
| `StartRequested_RendersHeatingSnapshotAndNewEventHistory` | Start 요청이 Controller 호출과 View 갱신으로 연결됨 |
| `TimerTicked_ForwardsElapsedTimeToControllerAndRefreshesView` | Stopwatch elapsed가 Controller까지 전달되고 View가 갱신됨 |
| `RecipeSelectionRequested_WhenIdle_RendersSelectedRecipe` | Idle 선택 요청 반영 |
| `RecipeSelectionRequested_WhenHeating_RendersOriginalRecipe` | Heating 선택 요청 거부 결과 반영 |

`EquipmentPresenter`를 public API로 넓히지 않았다. `InternalsVisibleTo("ChamberControlSimulator.Presentation.Tests")`로 테스트 assembly에만 internal 접근을 허용한다.

## 8. 재현 명령

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```
