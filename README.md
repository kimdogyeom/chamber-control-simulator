# Virtual Thermal Chamber Controller

WinForms 기반 가상 열처리 챔버 제어 시뮬레이터입니다. 목표는 화면을 많이 만드는 것이 아니라, 상태 전이·안전 인터락·Alarm·Recovery를 Core 규칙으로 분리하고 자동 테스트로 확인하는 것입니다.

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

이 공개 스냅샷에는 이전 프로젝트명과 화면 문구가 표시된 캡처를 포함하지 않습니다. 새 이름으로 실행한 앱 창 전용 캡처는 검증 후 `docs/demo/images/`에 추가합니다.

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

상세한 책임 분리와 테스트-불변식 매핑은 [아키텍처와 검증 기록](docs/architecture-and-verification.md)에서 확인할 수 있습니다.

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

## 로컬에서 확인하기

### 준비물

- Windows
- .NET SDK 10
- WinForms 개발 워크로드가 설치된 Visual Studio 환경

### Build와 테스트

```powershell
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore
```

### WinForms 실행

Visual Studio에서 `ChamberControlSimulator.slnx`를 연 뒤 `ChamberControlSimulator` 프로젝트를 시작 프로젝트로 선택하고 Debug 실행합니다. Recipe 선택, Start/Stop, Door, simulated temperature, feedback pause/resume, Acknowledge, Reset을 확인할 수 있습니다.

정상·DoorOpen·OverTemperature·SensorTimeout 재현 절차와 자동 테스트 연결은 [데모 시나리오](docs/demo/SCENARIOS.md)에 정리했습니다.

## 현재 범위와 다음 단계

### 현재 포함하지 않는 것

- 실제 장비·PLC·Serial·TCP/IP·Modbus·SECS/GEM 연동
- 실제 온도 센서, 히터, 모터, 릴레이, 솔레노이드 제어
- PID 제어, 보정된 열 모델, 실제 공정 Recipe
- 데이터베이스·영구 로그·클라우드·다중 챔버

### Phase 2 후보: Arduino 입력 HIL

Phase 2에서는 Arduino UNO의 저전압 입력을 USB Serial로 받아 가상 센서 입력으로 사용하는 HIL(Hardware-in-the-Loop) 확장을 계획합니다.

- 가변저항: **virtual temperature input**
- 버튼: **virtual Door input**
- Core: 기존 `ThermalController`가 동일한 Alarm·Recovery 규칙으로 판단

이 단계도 실제 챔버 온도를 측정하거나 히터·도어를 구동하는 실장비 제어가 아닙니다. 고전력 출력과 실제 공정 제어는 범위 밖으로 유지합니다.

## 저장소 구성

```text
ChamberControlSimulator/                       WinForms View와 Presenter
ChamberControlSimulator.Core/                  상태 전이·안전·Recovery 규칙
ChamberControlSimulator.Core.Tests/            Core 안전 규칙 MSTest
ChamberControlSimulator.Presentation.Tests/    FakeEquipmentView 기반 Presenter MSTest
docs/                                          아키텍처·검증·데모 기록
```
