# P4-T1 command reservation and ID admission verification

## Evidence identity

- Authoritative repository: `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source commit: `8f32ce7698e8f38ef7b321800e5326f259d4b623` (`feat: reserve command admission before dispatch`)
- Source parent: `1016f6584b96f8900b98a269dc731f39a1c07e8b`
- Source commit path set and final file hashes were reverified against the frozen review manifest before this documentation checkpoint.

## Exact source and test scope

| path | SHA-256 |
| --- | --- |
| `ChamberControlSimulator.Core/Models.cs` | `fcedec1408d482f12a0137ca6ca718294331450695e370be2897580e1875aab2` |
| `ChamberControlSimulator.Core/ThermalController.cs` | `765522101ecf05ec4365949f191399e5f179b11f67c4c0e13900275fba19f3e0` |
| `ChamberControlSimulator.Application/EquipmentCommandCoordinator.cs` | `f79b7d03f55341553ea61533bf37fa1e63f9eb9984ecfbb44e161253ac6e182e` |
| `ChamberControlSimulator.Core.Tests/CommandReservationTests.cs` | `3c2bac17bbc24e47c1dc5e45125628b65748eb010089ef3d378ba0b56b421dac` |
| `ChamberControlSimulator.Application.Tests/EquipmentCommandCoordinatorTests.cs` | `e01b61e9f6e3d8993e648c8d01f29420a82ff7b2925e992200c6a5e1e98b100f` |

## Verified behavior

- `ThermalController.TryReserveCommand(...)` returns an opaque, PLC-neutral reservation only when the requested Start/Stop/Reset is currently eligible.
- Reservation and Application admission do not change Core state or append a Core event.
- `EquipmentCommandCoordinator.TryAdmit(...)` retains exactly one pending admission and allocates positive process-local IDs monotonically, beginning at `1`.
- Duplicate admission is rejected while the pending fence exists; there is no queue.
- If Core eligibility changes after reservation, the reservation is invalidated but retained. It is not released, replaced, retried, or replayed.
- Production capability tests prove the P4-T1 Application source does not reference PLC abstractions or invoke `WriteOutputsAsync`.

## Source-bound validation

The following results apply to source commit `8f32ce7698e8f38ef7b321800e5326f259d4b623`, not to this later documentation commit:

- focused `CommandReservationTests`: 7/7 passed;
- focused `EquipmentCommandCoordinatorTests`: 5/5 passed;
- Debug solution build: 0 warnings, 0 errors;
- full solution regression: 92/92 passed, 0 failed, 0 skipped;
- frozen Windows-byte independent source review: PASS.

Full-suite project totals were PLC Abstractions 21/21, Core 33/33, PLC Simulation 11/11, Presentation 19/19, and Application 8/8.

## Frozen source-review provenance

- snapshot identity: `370ec97d4bb36e0c357b22da6d3e4a8738878deaabb35a7e0888e8b405483520`;
- `MANIFEST.json` SHA-256: `e4d489e2ce45106c6fac9165bb136ca97f63df55d33929e1ec91e5baeed74fba`;
- `MANIFEST.sha256` SHA-256: `7116b57944e5356d0e480e6f5b79502228c195f4576fd2d2b694d59c739653d4`;
- `VALIDATION_RECEIPT.md` SHA-256: `c9cd0fe08e0caf22a6ecabacf382aa09bf5bf9481598abb8ea31a2fbe53f0122`;
- `REVIEW_SCOPE.md` SHA-256: `4e0ed571b680c6216971e00a6662669e46b86667eb53df502860ca9b6d66abb6`.

The committed five-path source set matches the candidate hashes recorded by that manifest.

## Explicit nonclaims

P4-T1 does not implement or prove:

- PLC output-port dispatch or runtime transport-receipt classification;
- semantic ACK matching or Core transition after ACK;
- write/ACK deadlines, timeout, cancellation, reconnect, or reconciliation recovery;
- Presenter/Form command routing or automatic heating;
- real Modbus TCP, a physical PLC, equipment behavior, E-Stop, Safety PLC, hardware safety, or human safety.

Status at this checkpoint:

```text
P4-T1 reservation/admission: implemented
P4-T2 output receipt: not implemented
P4-T3 semantic ACK: not implemented
P4-T4 timeout/reconciliation: not implemented
P4-T5 Reset/Recovery lifecycle: not implemented
```

## Documentation boundary audit

This checkpoint changes only `README.md`, `docs/architecture-and-verification.md`, and this receipt. Existing P3 verification receipts and `docs/demo/SCENARIOS.md` remain bound to their own source/evidence baselines.

Ignored `docs/roadmap/STATUS.md` and `docs/roadmap/03-implementation-roadmap.md` are continuity artifacts only and are excluded from staging. Ignored `docs/structure-guide.md` was audited and is stale against P3-T4/P4-T1 composition; because it is untracked and outside this allowlist, it remains separately scoped documentation debt and is not evidence for this checkpoint.
