# P3-T4 WinForms Observation Composition — Verification Receipt

## Source anchor

- source commit: `2e502faf81a81088e6f2ec27c2010cb9c1bf0118` — `feat: compose WinForms observation runtime`
- base source commit: `870ab0f34665f60b5a7aa3b60760271365cfc23e` — `docs: record P3-T3 verification evidence`
- source review bundle manifest: `9b09fd4e86708a88ae3552ff31d62bdeaebeb6fdcae0ede3df69ce9603148688`
- post-smoke current solution baseline: `9c3ad9591a52e8a4bbaab6d028cdc3a9dae2655b` — `chore: group solution projects`; solution-folder organization only, with all 10 existing project paths retained
- scope: P3-T4 observation composition, concrete P3/P4 facade split, timer elapsed preservation, async Form close/teardown, publication-reservation safety, canonical review-patch evidence, and supplementary user-driven Session 1 manual smoke

## Implemented composition

```text
Form1 / IEquipmentView
  → EquipmentPresenter
  → IEquipmentObservationRuntime
  → EquipmentCoordinator(IPlcObservationPort)
  → VirtualPlcClient
  → ThermalObservation + elapsed
  → ThermalController.ApplyObservation(...)
  → ControllerSnapshot / Form1 rendering
```

Simulation input follows a separate P3-only path:

```text
Form1 simulation input
  → EquipmentPresenter
  → IEquipmentObservationRuntime
  → IPlcObservationInputControl
  → VirtualPlcObservationInputControl
```

`EquipmentCoordinator` receives `IPlcObservationPort`, not write-capable `IPlcClient`. `Program.CreateObservationRuntime(...)` injects a distinct concrete `VirtualPlcObservationInputControl` into `EquipmentObservationRuntime`; that facade implements `IPlcObservationInputControl` and contains only temperature, sensor-health, and door setters. `VirtualPlcSimulationControl` remains a separate P4-oriented virtual-time/ACK/fault facade. Neither P3 path has `WriteOutputsAsync`, `Advance`, ACK suppression, or transport-fault control capability.

## TDD evidence

| Contract | Expected RED | GREEN proof |
| --- | --- | --- |
| P3 must not progress P4 virtual state | pre-seeded pending Start command changed observed temperature from `20°C` to `25°C` through `Advance` | `ObservationRuntime_Cycle_DoesNotAdvancePendingP4CommandState` passed |
| View lifecycle must be awaitable | async timer/closing event contract was absent | `ViewLifecycleEvents_ExposeAwaitableTimerAndClosingHandlers` passed |
| busy elapsed must be preserved | expected `9s`, actual `4s` | `TimerTicked_WhileBusy_AccumulatesElapsedForNextAdmittedCycle` passed |
| close must await teardown | closing completion returned before runtime disposal | `ClosingRequestedAsync_AwaitsRuntimeTeardownBeforeCloseCanContinue` passed |
| concurrent callers must share teardown | second disposer completed early | `DisposeAsync_ConcurrentCallersShareActiveTeardownCompletion` passed |
| faults must not skip cleanup | non-cancellation cycle fault escaped and skipped runtime disposal | `FaultedCycle_DoesNotSkipRuntimeDisposalOrEscapeTimerPath` passed |
| P3 must not receive broad P4 capabilities | constructor expected `IPlcObservationPort`, actual `IPlcClient` | `P3Runtime_UsesObservationOnlyPortsWithoutP4ControlCapability` passed |
| actual Program composition must not inject a broad facade | `CreateObservationRuntime` was absent, so the composition test could not prove a distinct concrete P3 object | `ProgramComposition_InjectsDistinctP3ObservationInputFacadeWithoutP4Controls` passed |
| active task must be published before reentrant teardown | expected disposal count `0`, actual `1` during raw task publication | `DisposeAsync_DuringCycleTaskPublication_WaitsForPublishedCycleBeforeRuntimeDisposal` passed |

All added or behaviorally modified tests retain adjacent `목적`, `예상 결과`, `완료 조건` comments.

## Final verification

Windows authoritative worktree (`C:\Users\rlaeh\source\repos\chamber-control-simulator`) results for the P3-T4 implementation and current solution baseline `9c3ad9591a52e8a4bbaab6d028cdc3a9dae2655b`:

```text
Debug build: 0 warnings / 0 errors
Application:       3/3 passed
Core:             26/26 passed
PLC Abstractions: 21/21 passed
PLC Simulation:   11/11 passed
Presentation:     19/19 passed
Total:            80/80 passed
Presentation stability: 10 consecutive final-binary runs passed
```

- final source bundle: every tracked `*.cs`, `*.csproj`, and `ChamberControlSimulator.slnx`, plus five untracked P3-T4 source files; 19 changed paths have canonical repository-relative patch headers
- source bundle static gate: changed 19 source/project files are UTF-8 without BOM, CRLF, and without trailing whitespace; changed XML uses 2-space indentation; the separate raw patch artifact records its own 71 trailing-whitespace context-marker lines
- `git diff --check`: passed
- source review verdict: `PASS — Windows-byte independent architecture/lifecycle/capability review PASS; repaired artifact-integrity review PASS`
- solution grouping validation on `9c3ad95`: 10 project paths retained exactly once; UTF-8 no BOM, CRLF-only, no trailing whitespace; Debug build 0 warnings/0 errors; full regression 80/80; independent one-file review PASS

## Post-fix Session 1 manual smoke

The user operated the Session 1 WinForms UI after the P3-T4 repairs and current solution baseline `9c3ad95`. The user reported the prescribed observed-input flow completed without issue:

```text
Simulation Input temperature: 20 → 30
Apply
Observed rendering: 30.00 °C
Controller phase: Idle retained
No error/freeze/cross-thread failure reported
```

The user then reported the UI closed. A subsequent read-only process check returned `CHAMBER_PROCESS_ABSENT`. This is supplementary user-driven live composition evidence: it covers the narrow observed-input → observation cycle → Form rendering path and normal shutdown outcome. No P3-T4 application screenshot was retained or published as current evidence.

## Evidence boundary and nonclaims

Automated source/test/build evidence remains primary. The Session 1 result is a user-operated manual smoke, not an automated regression and not proof of every Form state. It does not claim output write, `WriteOutputsAsync`, transport receipt, semantic ACK, automatic heater/temperature progression, real Modbus TCP, physical PLC communication, semiconductor process behavior, E-Stop, safety PLC, hardware safety circuits, or human safety.

P4 remains out of scope: Application output dispatch, `WriteOutputsAsync` use, transport receipt interpretation, matching semantic ACK, timeout, and duplicate prevention are not implemented. This project also does not verify real Modbus TCP or physical PLC communication, semiconductor process behavior, E-Stop, safety PLC, hardware safety circuits, or human safety.
