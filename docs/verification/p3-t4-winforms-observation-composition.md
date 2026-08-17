# P3-T4 WinForms Observation Composition — Verification Receipt

## Source anchor

- source commit: `2e502faf81a81088e6f2ec27c2010cb9c1bf0118` — `feat: compose WinForms observation runtime`
- base source commit: `870ab0f34665f60b5a7aa3b60760271365cfc23e` — `docs: record P3-T3 verification evidence`
- source review bundle manifest: `9b09fd4e86708a88ae3552ff31d62bdeaebeb6fdcae0ede3df69ce9603148688`
- scope: P3-T4 observation composition, concrete P3/P4 facade split, timer elapsed preservation, async Form close/teardown, publication-reservation safety, and canonical review-patch evidence

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

Windows authoritative worktree (`C:\Users\rlaeh\source\repos\chamber-control-simulator`) results:

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

## Evidence boundary and nonclaims

The earlier manual P3-T4 observation smoke predates the final teardown, capability, and publication-reservation repairs. The user explicitly deferred another Session 1 UI smoke to avoid disrupting a foreground application. This receipt therefore claims automated source/test/build evidence only; it does not claim a post-fix live Form-close smoke.

P4 remains out of scope: Application output dispatch, `WriteOutputsAsync` use, transport receipt interpretation, matching semantic ACK, timeout, and duplicate prevention are not implemented. This project also does not verify real Modbus TCP or physical PLC communication, semiconductor process behavior, E-Stop, safety PLC, hardware safety circuits, or human safety.
