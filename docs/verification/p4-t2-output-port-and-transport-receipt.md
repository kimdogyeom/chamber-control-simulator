# P4-T2 narrow output port and transport receipt verification

## Evidence identity

- Branch: `main`
- Source commit: `254c5460ab6f600d058d25e8a4e8207810bb45a8` (`feat: dispatch pending command through output port`)
- Source parent: `c14fee35db62684307da0ca78640aa23dc6a97b5`
- The source commit contains exactly the six source/test paths listed below; final Windows working-byte hashes matched the frozen review manifest before staging and the staged blob IDs matched the committed blob IDs.

## Exact source scope

| Path | Windows working-byte SHA-256 |
| --- | --- |
| `ChamberControlSimulator.Application.Tests/EquipmentCommandCoordinatorTests.cs` | `b68f26d7031cd473ad62da0a10573f8eda69e5e5209a7c2752bec33767bb08c3` |
| `ChamberControlSimulator.Application/EquipmentCommandCoordinator.cs` | `8ebd250eb0c8be2101a53a844da90c857dba6627900c9ddd2a067a56d9b430d9` |
| `ChamberControlSimulator.Plc.Abstractions.Tests/IPlcClientTests.cs` | `f13321f826693d6127f8fa59ba77981b8ab02b9c7b30decdd3d6cf0b244aa1e0` |
| `ChamberControlSimulator.Plc.Abstractions.Tests/IPlcOutputPortTests.cs` | `7b6adec38dfdb52fb7f437e2b9cf7ada507e0f964c9265410e54541629ebe0c5` |
| `ChamberControlSimulator.Plc.Abstractions/IPlcClient.cs` | `40c96042f92e4fb0db3c505fab9c30fca246bdccbe1ec38cf8f8e03737ab1095` |
| `ChamberControlSimulator.Plc.Abstractions/IPlcOutputPort.cs` | `e9b9561ea24d72cff6abc0723554ab58b6a6901e1014f3c851efebb6cb9b7579` |

## Approved caller and fault-model choices

- T2 retains `TryAdmit(kind)` and adds `DispatchPendingAsync(CancellationToken)`, which dispatches only the coordinator's retained pending admission.
- `EquipmentCommandCoordinator` receives only `IPlcOutputPort`; it does not receive `IPlcClient`, `IPlcObservationPort`, input, connection, disposal, or Virtual PLC controls.
- exact matching `Written` means transport receipt received and semantic ACK still pending. It never means completed or successful equipment behavior.
- mismatched receipt, matching current `Failed`, exception, and cancellation retain the pending reservation and one-command fence. There is no retry, replay, release, or ID replacement.
- The approved future suppressed-ACK model may apply the virtual semantic effect at semantic time while suppressing observed ACK. T2 does not change `VirtualPlcClient` timing or semantics.

## Observable behavior proved

1. `IPlcOutputPort` exposes exactly `Task<PlcWriteReceipt> WriteOutputsAsync(PlcOutputCommand, CancellationToken)` and no observation, connection, disposal, or virtual-control surface.
2. `IPlcClient` is an empty compatibility composite of `IPlcObservationPort` and `IPlcOutputPort`; P3 `EquipmentCoordinator` remains exactly `ThermalController, IPlcObservationPort`.
3. accepted Start maps explicitly to `PlcOutputCommand(commandId, PlcCommandKind.Start)` and reaches the narrow port exactly once.
4. dispatch-started is claimed under the coordinator gate before awaiting I/O; no synchronous lock is held across the await.
5. exact matching `Written` returns `AwaitingAcknowledgement` while Core remains Idle/event-empty and the pending reservation remains held.
6. mismatched `Written` and matching `Failed` return `DeliveryIndeterminate`; thrown/canceled writes propagate under the T2 interim taxonomy. Every such path preserves the fence and blocks another dispatch/admission.

## Source-bound RED to GREEN and validation

- Expected RED — Abstractions: test compile failed because `IPlcOutputPort` did not exist.
- Expected RED — Application: controlled output-only fake could not implement the absent `IPlcOutputPort`.
- Focused `ChamberControlSimulator.Plc.Abstractions.Tests`: **19/19 passed**.
- Focused `ChamberControlSimulator.Application.Tests`: **14/14 passed**.
- Debug solution build: **0 warnings / 0 errors**.
- Full solution regression: **96/96 passed** — Abstractions 19, Application 14, Core 33, Simulation 11, Presentation 19.
- `git diff --check`: passed. Korean purpose / expected result / completion condition comments are adjacent to all changed behavior tests.

These results are bound to source commit `254c5460ab6f600d058d25e8a4e8207810bb45a8`, not to this later documentation commit.

## Frozen Windows-byte review

- Review ZIP SHA-256: `caf7e681d1c5718876826ea983132c7820bfdf868b76fa93b1e0e99ba0c75121`
- Snapshot SHA-256: `c5bf3af08404d721da39b731d1ec0e9ec85806a7820b439a6dfe56c2d4181b29`
- Root manifest SHA-256: `ac74d1f6218d686792834d59d9936e78e8f430f3c9b2b2c824c4f8fb915eb9df`
- Payload count: 28
- Independent integrity receipt SHA-256: `47ed7c16568905bff0c71a1afe56ab14a8c5fe8609b7df4eb9282725ade61e08`
- Independent architecture/product/code review: **PASS / CLEAR / CLEAR / CLEAR** with no blockers.

## Explicit nonclaims

P4-T2 does not implement or prove:

- semantic ACK consumption, freshness/watermark rules, or Core completion;
- write/ACK deadline, timeout, late-ACK, disconnect/reconnect, or reconciliation recovery;
- Presenter/Form/Program command routing or automatic heating;
- Virtual PLC Start/Stop/Reset semantic side effects or suppressed-ACK timing changes;
- real Modbus TCP, a physical PLC, equipment behavior, E-Stop, Safety PLC, hardware safety, or human safety.

Status at this checkpoint:

```text
P4-T1 reservation/admission: implemented
P4-T2 narrow output/transport receipt: implemented
P4-T3 semantic ACK: not implemented
P4-T4 timeout/reconciliation: not implemented
P4-T5 Stop/Reset lifecycle: not implemented
```

## Documentation checkpoint boundary

The tracked documentation allowlist is exactly `README.md`, `docs/architecture-and-verification.md`, and this receipt. No C# source, project, UI, simulation, runtime, generated output, credential, service, remote Git, or physical-device action belongs to this checkpoint.

Ignored `docs/roadmap/STATUS.md` and `docs/roadmap/03-implementation-roadmap.md` are continuity artifacts only and remain excluded from staging. Ignored `docs/structure-guide.md` remains stale and outside this checkpoint; it is not evidence.
