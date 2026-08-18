# P4-T3 exact fresh Start semantic ACK verification receipt

## Authority and evidence state

- Authoritative repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `7a874e8f407f75a56f6dc043314590f0fd0bcf6a`
- Parent: `e60b707290d3fd183272111c420df8ac10ab562e`
- Subject: `feat: complete Start on exact fresh PLC acknowledgement`
- Scope: source/test behavior only. This receipt is a separate tracked documentation checkpoint.

`Completed` here means that the exact source commit, Windows validation, frozen-byte integrity verification, and independent repaired-generation review exist. It does not mean UI command routing, timeout/recovery completion, Stop/Reset lifecycle completion, real PLC/equipment behavior, or safety certification.

## Exact source scope

1. `ChamberControlSimulator.Application.Tests/EquipmentCommandRuntimeTests.cs`
2. `ChamberControlSimulator.Application/EquipmentCommandCoordinator.cs`
3. `ChamberControlSimulator.Application/EquipmentCommandRuntime.cs`
4. `ChamberControlSimulator.Core.Tests/CommandReservationTests.cs`
5. `ChamberControlSimulator.Core/Properties/AssemblyInfo.cs`
6. `ChamberControlSimulator.Core/ThermalController.cs`
7. `ChamberControlSimulator.Plc.Simulation.Tests/ChamberControlSimulator.Plc.Simulation.Tests.csproj`
8. `ChamberControlSimulator.Plc.Simulation.Tests/EquipmentCommandRuntimeVirtualPlcTests.cs`
9. `ChamberControlSimulator.Plc.Simulation.Tests/VirtualPlcClientTests.cs`
10. `ChamberControlSimulator.Plc.Simulation.Tests/VirtualPlcFaultControlTests.cs`
11. `ChamberControlSimulator.Plc.Simulation/VirtualPlcClient.cs`

No Presenter, Form, `Program`, View contract, timeout policy, Stop/Reset completion, Modbus adapter, service, or device file changed.

## Implemented contract

### Fresh baseline and ID high-water

`EquipmentCommandRuntime.RequestStartAsync` requires the latest `EquipmentCycleResult` to be `Completed` with a non-null immutable `PlcInputSnapshot`. Stale or failed latest cycles close the baseline gate. `TryAdmitAfter(Start, observedAck)` retains the public T1 `TryAdmit(kind)` behavior while allocating strictly above both the process-local allocator and the observed ACK high-water.

### Transport/semantic separation

A retained Start admission is written exactly once through `IPlcOutputPort`. Exact `Written` means `AwaitingAcknowledgement`; Core remains Idle with no Start event. Failed/mismatched receipt, write exception, and in-flight cancellation retain the command/reservation in terminal reconciliation with no retry, replay, release, or later exact-ACK revival.

### Exact fresh ACK and Core authority

The runtime shares one `SemaphoreSlim` across P3 reads and P4 writes, so operations do not overlap on the same transport. A completion candidate must be a later accepted observation (`ObservationSequence` strictly greater than the captured baseline) with ACK exactly equal to the pending ID. P3 maps the observation before completion. Lower ACK waits; higher/wrong ACK is terminal reconciliation; exact ACK mapped with unsafe Core state is terminal Core-ineligible.

`ThermalController.TryCompleteAcknowledgedCommand` is internal and friend-visible only to Application and Core tests. Core verifies reservation identity, invalidation, and current eligibility, consumes on success exactly once, and exposes no public generic apply/release authority.

### Deterministic Virtual semantic time

Virtual Start effect is queued at write and applied at its delayed semantic timestamp. `Advance` integrates to each due event, applies every command due there, then integrates the remaining interval. One overshooting step and equivalent split steps therefore produce the same temperature and ACK. Suppressing the observed ACK does not cancel the semantic effect; Application stays uncompleted because no exact ACK is observed.

## RED-to-GREEN and Windows validation

Expected RED was captured before implementation:

- Core tests did not compile because the non-public acknowledged-completion seam was absent.
- Application tests did not compile because the Start runtime/dispositions were absent.
- Simulation integration tests did not compile because the runtime was absent.

Final authoritative Windows results after review repair:

- Core: **36/36 passed**
- Application: **24/24 passed**
- Simulation: **16/16 passed**
- Debug solution build: **0 warnings / 0 errors**
- Full solution: **114/114 passed** (Abstractions 19 + Core 36 + Application 24 + Simulation 16 + Presentation 19)
- `git diff --check`: passed

These figures are bound to source `7a874e8f407f75a56f6dc043314590f0fd0bcf6a`, not to future P4-T4/T5 behavior.

## Frozen review and repair provenance

Initial frozen generation review returned `BLOCK`: `VirtualPlcClient.Advance` applied a due Start only at the end of an overshooting interval, making plant evolution step-partition dependent. It also advised direct write-exception/cancellation terminal-hold tests.

The defect was repaired at its source with chronological due-event slicing. Normal and suppressed-ACK partition-invariance tests plus direct ambiguity tests were added, then all focused/full Windows gates were rerun.

Final repaired generation:

- ZIP SHA-256: `1c20f8ba15494bca305210b130c28bdc6668ed57fb80f03ac496d3e9f12e6749`
- Manifest SHA-256: `a9e7de3daa3d725f105a71f909fb5f3d3783584d43edcc92a7f2e54dc4163160`
- Snapshot SHA-256: `184404e0502a88e67908a84939725aa4c2234bec8d539570d307396664ade7ee`
- Payloads: `34`
- Integrity receipt SHA-256: `d174c30cbaa84d4d3ad6728f22196198acb3eb62830ae41806d8c4362e675cc9`
- Integrity checks: exact payload hashes/counts, exact 11-path canonical patch, patch apply-check, Git-normalized candidate equivalence — passed
- Independent architecture/product/code review: **PASS**, no findings; prior blocker and advisory resolved

Stage/commit verification proved that the committed 11 paths and blobs were exactly the reviewed allowlist. Fresh Windows bundle SHA-256 `93815bcd0a333b8483d62182d644dc385abccf17447dceb37b1b2fbb7c185f82` realigned the zero-remote control mirror to the source commit.

## Reproduction

Run at the Windows repository root:

```powershell
dotnet test ChamberControlSimulator.Core.Tests\ChamberControlSimulator.Core.Tests.csproj --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.Application.Tests\ChamberControlSimulator.Application.Tests.csproj --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.Plc.Simulation.Tests\ChamberControlSimulator.Plc.Simulation.Tests.csproj --configuration Debug --no-restore --nologo
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

## Explicit nonclaims

This checkpoint does **not** claim:

- Presenter/Form/Program or operator command routing,
- timeout, deadline, close/teardown, reconnect, or automatic recovery policy,
- Stop/Reset semantic completion or preemption,
- retry, replay, queue replacement, reservation release, or retroactive completion,
- production Modbus/TCP, real PLC, chamber, sensor, heater, or equipment behavior,
- E-Stop, Safety PLC, hardware safety circuit, human safety, or physical safety validation.
