# P3-T3 Core Plant Simulation Separation — Verification Receipt

## Source anchor

| 항목 | 값 |
| --- | --- |
| Repository | `chamber-control-simulator` |
| Source commit | `b949e6c1a22526de2c6b39462325ef0e660ffd2b` |
| Subject | `feat: separate Core plant temperature policy` |
| Parent | `10541c11921ae09982fd048320ca07ecf49dcb0c` |
| Source scope | `ThermalController`, Core tests, Presentation tests |

이 receipt는 위 source commit에만 적용된다. source commit과 documentation checkpoint를 분리해 code evidence와 documentation evidence를 섞지 않는다.

## Implemented boundary

P3-T2는 fresh `PlcInputSnapshot`을 Core-owned `ThermalObservation`으로 atomic mapping했다. P3-T3는 그 observation boundary를 기준으로 Core의 legacy plant simulation 책임을 제거했다.

```text
fresh PLC or Virtual PLC observation + elapsed
  → ThermalObservation
  → ThermalController.ApplyObservation(...)
  → normal phase policy
```

P3-T3가 보장하는 범위:

- `ThermalController.Tick(TimeSpan)`은 physical temperature를 합성하거나 Heating/Holding/Cooling normal phase를 진행하지 않는다.
- `ThermalController.ApplyObservation(ThermalObservation, TimeSpan)`은 observed temperature와 elapsed를 받아 normal phase policy를 진행한다.
- `Tick`은 SensorTimeout/Recovery를 위한 legacy feedback timing을 유지하지만 `Holding` elapsed를 누적하지 않는다.
- Core는 PLC, Modbus, Virtual PLC, WinForms type을 reference하지 않는다.
- P3 coordinator는 계속 read-only다. output write와 semantic ACK completion은 P4 범위다.

## RED to GREEN contract

초기 RED는 observed 20°C에서 `Start()` 후 `Tick(2초)`을 호출했을 때 `Heating`을 기대했지만 실제로 `Holding`이 되는 것을 확인했다. 이는 기존 `Tick`이 synthetic temperature를 올려 target에 도달시켰기 때문이다.

최종 contract test는 Heating, Holding, Cooling을 모두 고정한다.

```text
observed 20°C → Heating → Tick(2s) → Heating, 20°C
observed 30°C → Holding → Tick(configured 3s hold duration) → Holding, 30°C
observed 30°C + elapsed 3s → Cooling → Tick(2s) → Cooling, 30°C
```

Holding assertion은 recipe의 `holdDuration`과 같은 3초를 명시적으로 사용한다. legacy Tick holding accumulator를 되살리면 Tick 직후 Cooling으로 전이하므로 assertion에서 즉시 실패한다.

## Automated verification

Windows authoritative repository에서 source commit `b949e6c` bytes를 기준으로 실행했다.

```powershell
dotnet test ChamberControlSimulator.Core.Tests --configuration Debug --no-restore --filter FullyQualifiedName~Tick_WithoutExternalObservation_DoesNotSynthesizeTemperatureOrAdvancePhase --nologo
dotnet build ChamberControlSimulator.slnx --configuration Debug --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

| Gate | Result |
| --- | --- |
| Focused Tick contract | 1/1 passed |
| Debug build | 0 warnings / 0 errors |
| Application tests | 3/3 passed |
| Core tests | 26/26 passed |
| PLC Abstractions tests | 21/21 passed |
| Virtual PLC tests | 11/11 passed |
| Presentation tests | 5/5 passed |
| Full regression | 66/66 passed |

## Independent source review

| 항목 | 값 |
| --- | --- |
| Review type | Windows authoritative byte-copy, changed-scope independent review |
| Manifest SHA-256 | `e7e94da9061b06428e9370c3fdbf1af28a28e2d5f451070d8de0e292039f540f` |
| Result | PASS |
| Checked | Tick temperature/phase boundary, observation-driven phase policy, Core dependency boundary, P3 read-only rule, changed-test comments, CRLF/no BOM/trailing whitespace |

최종 review는 explicit configured-duration Holding assertion이 observation-driven transition 전에 복구된 Tick-only Holding → Cooling transition을 감지한다는 것을 확인했다.

## Explicit nonclaims and remaining work

- 이 P3-T3 source anchor 당시에는 WinForms composition root가 Coordinator/Virtual PLC observation runtime에 연결되지 않았다. 이후 P3-T4 source `2e502fa`가 composition을 추가했으며, 그 별도 evidence는 [`p3-t4-winforms-observation-composition.md`](p3-t4-winforms-observation-composition.md)에 기록한다. 기존 P0 screenshots는 계속 historical baseline evidence다.
- P4 전까지 output write, matching semantic ACK, timeout, late ACK, duplicate prevention은 구현되지 않았다.
- Virtual PLC는 illustrative simulation이며 production PLC, Modbus TCP communication, semiconductor process behavior를 검증하지 않는다.
- PC software policy는 E-Stop, Safety PLC, hardware safety circuit, human safety를 보장하거나 대체하지 않는다.
