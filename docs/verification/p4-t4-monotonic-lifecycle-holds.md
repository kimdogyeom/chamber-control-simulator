# P4-T4 monotonic lifecycle holds verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `0e2f6d2505e340846509b42987bd88eac6c812d1`
- Parent: `9f762ef53f9c6d711bf2623c45b6ec5fe1b90ca7`
- Subject: `feat: enforce monotonic command lifecycle holds`
- Scope: source/test behavior only. This receipt is a separate tracked documentation checkpoint.

`Completed` here means that the exact source commit, source-bound Windows validation, frozen-byte integrity verification, and repaired-generation independent review exist. It does not mean P4-T5 Stop/Reset completion, automatic reconnect recovery, operator UI smoke, real PLC/equipment behavior, or safety certification.

## Exact source scope

1. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`
2. `ChamberControlSimulator.Application.Tests/ManualTimeProvider.cs`
3. `ChamberControlSimulator.Application/EquipmentCommandLifecycleState.cs`
4. `ChamberControlSimulator.Application/EquipmentCommandRuntime.cs`
5. `ChamberControlSimulator.Plc.Simulation.Tests/EquipmentCommandRuntimeVirtualPlcTests.cs`
6. `ChamberControlSimulator.Presentation.Tests/EquipmentPresenterTests.cs`
7. `ChamberControlSimulator/Form1.cs`
8. `ChamberControlSimulator/Presentation/EquipmentObservationRuntime.cs`
9. `ChamberControlSimulator/Presentation/EquipmentPresenter.cs`
10. `ChamberControlSimulator/Presentation/IEquipmentCommandRuntime.cs`
11. `ChamberControlSimulator/Presentation/IEquipmentView.cs`
12. `ChamberControlSimulator/Program.cs`

No Core, PLC abstraction/implementation, Modbus adapter, service, device, Stop/Reset command-completion, or reconnect-recovery source changed.

## Implemented contract

### Two monotonic deadline epochs

`EquipmentCommandRuntime` requires a fourth `TimeProvider` constructor argument owned by Application composition. It records monotonic timestamps only; wall clock and Core have no deadline authority.

- Receipt epoch starts immediately before `DispatchPendingAsync` invokes the output path.
- `Writing` is observable while the transport write has not settled.
- Elapsed time **greater than or equal to exactly 3 seconds** is `ReceiptTimedOut`; timeout wins an exact receipt/deadline tie and delayed continuation observation.
- Only a timely exact matching `Written` starts the acknowledgement epoch.
- ACK elapsed time **greater than or equal to exactly 3 seconds** is `AcknowledgementTimedOut`.

Admission time does not start either epoch. A transport receipt still is not semantic acceptance or command completion.

### Shared lease and terminal evidence

P3 observation reads and P4 writes retain one `SemaphoreSlim` boundary. If timeout or cancellation occurs while a write ignores cancellation, the request returns or throws but transfers gate release to a continuation that observes actual physical task settlement. P3 reads and later writes remain blocked until then. The eventual result cannot start an ACK epoch, complete Core, retry, replay, release the reservation, or allocate another ID.

`ReceiptTimedOut`, `AcknowledgementTimedOut`, `ReconciliationRequired`, `AcknowledgedButCoreIneligible`, and `Completed` are stable terminal evidence. A later exact ACK, wrong/higher ACK, reconnect observation, transport completion, or cycle cancellation cannot revive or generically overwrite them. Duplicate Start requests produce no new ID or write. `StopAdmission` synchronously closes admission before shutdown cancellation.

### Separate Presentation capabilities and owned close

`IEquipmentObservationRuntime` remains observation/input/cycle/disposal only. New non-disposable `IEquipmentCommandRuntime` exposes only `RequestStartAsync` and `StopAdmission`. The concrete `EquipmentObservationRuntime` implements both narrow references around one `EquipmentCommandRuntime`; `Program` passes that one owner to Presenter through both interfaces and supplies `TimeProvider.System`.

`IEquipmentView.StartRequested` is awaitable. Presenter tracks command and observation tasks separately, suppresses duplicate active Start handling, and renders only while alive. Close order is admission stop, shared cancellation, join of both active tasks, then exactly one observation-owner disposal. Noncooperative command work delays disposal until actual settlement; late rendering is blocked.

Only Start uses this P4 route. Existing Stop/Reset UI handlers remain legacy direct Core calls and are not P4 command completion.

## RED-to-GREEN and Windows validation

Expected RED was captured before implementation:

- Application tests did not compile because the four-argument `TimeProvider` constructor, timeout states, `CurrentState`, and `StopAdmission` were absent.
- Presentation tests did not compile because `StartRequested` was still `EventHandler` instead of `Func<Task>`.

Final authoritative Windows results after review repair:

- Application: **35/35 passed**
- Simulation: **16/16 passed**
- Presentation: **21/21 passed**
- Debug solution build: **0 warnings / 0 errors**
- Full solution: **127/127 passed** (Abstractions 19 + Core 36 + Application 35 + Simulation 16 + Presentation 21)
- `git diff --check`: passed

These figures are bound to source `0e2f6d2505e340846509b42987bd88eac6c812d1`, not to future P4-T5 behavior.

## Frozen review and repair provenance

Initial frozen source review returned `BLOCK` on three defects:

1. `Task.WhenAny` could accept a receipt at or after the exact 3-second boundary because the recorded write timestamp did not arbitrate ties.
2. observation-cycle cancellation could overwrite timeout/completed/Core-ineligible terminal evidence with generic reconciliation.
3. command authority had widened `IEquipmentObservationRuntime` instead of using a separate narrow Presentation command capability.

The defects were repaired at their sources. Monotonic elapsed `>= 3 seconds` now wins receipt ties and delayed observations; direct exact-tie and late-receipt tests were added. Cycle cancellation changes only active `AwaitingAcknowledgement`; direct timeout/completed/Core-ineligible preservation tests were added. Observation and command interfaces were split, one concrete owner is injected through both narrow references, and reflection tests prove the capability boundary. All focused/full Windows gates were rerun.

Final repaired generation:

- ZIP SHA-256: `17d5dd10256ac65493d9c3568f777c959bbaa15d9917ab54c1d09b5649e62fde`
- Manifest SHA-256: `e6e951beb62c6422bbbe3adba3d4daf285070bed75f87a45bcd3d3f19269b6ea`
- Snapshot SHA-256: `ec692a84d5b736dfcf6e600b18f7dfe8889b5f9cea95741444ebb0f397ac9b8c`
- Payloads: `30`
- Integrity receipt SHA-256: `a6a7bb0c5f71fea25cc9f232e0452dade5cf73a0017da2ec52f4cdefc2dcb130`
- Integrity checks: exact payload hashes/counts, exact 12-path canonical patch, patch apply-check, Git-normalized candidate equivalence — passed
- Independent architecture/product/code review: **PASS**, no remaining findings; all prior blockers resolved

Stage/commit verification proved that the committed 12 paths and blobs exactly matched the repaired reviewed allowlist. Fresh Windows bundle SHA-256 `1072edfb347a961a03a25ceccbc8d2fd2a7782496822e540039f0bf6d307d158` realigned the zero-remote control mirror to the source commit.

## Reproduction

Run at the Windows repository root:

```powershell
dotnet test ChamberControlSimulator.Application.Tests\ChamberControlSimulator.Application.Tests.csproj --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.Plc.Simulation.Tests\ChamberControlSimulator.Plc.Simulation.Tests.csproj --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.Presentation.Tests\ChamberControlSimulator.Presentation.Tests.csproj --configuration Debug --no-restore --nologo
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

## Explicit nonclaims

This checkpoint does **not** claim:

- P4-T5 Stop/Reset semantic completion, preemption, or global duplicate-fence release,
- automatic reconnect recovery, retry, replay, queue replacement, reservation release, or retroactive completion,
- operator-driven UI smoke or visual rendering evidence,
- production Modbus/TCP, real PLC, chamber, sensor, heater, or equipment behavior,
- E-Stop, Safety PLC, hardware safety circuit, human safety, or physical safety validation.
