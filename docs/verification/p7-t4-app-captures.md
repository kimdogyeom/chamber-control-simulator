# P7-T4 app-only capture status

## Authority

- Frozen Release SHA for automated evidence: `2d2993338eae7bf193c78e3f5ccc8b05722e80bb`
- This documentation commit does not add PNG files.

## Decision

No new `docs/demo/images/p7-*.png` files are committed in this slice. The agent session has no WinForms/desktop capture surface. 04 §7 forbids fabricating or compositing frames. P0 `docs/demo/images/00-idle.png` … remain historical Presenter/Core screenshots and are **not** current P6 UI evidence.

Each required state is therefore **test+log** until an operator captures the live app window on SHA `2d29933` (or a later SHA that re-runs Release).

## Required states

| State | Automated evidence | Capture |
| --- | --- | --- |
| Idle baseline | Core Idle snapshots; Presentation constructor render | test+log; operator PNG pending |
| Normal ACK Start/Heating | `CycleAsync_LaterExactFreshAcknowledgement_CompletesStartExactlyOnce`; Virtual PLC Start tracer | test+log; operator PNG pending |
| DoorOpen alarm/recovery | `OpenDoor_WhileHeating_EntersDoorOpenAlarm`; coordinator door mapping | test+log; operator PNG pending |
| ACK timeout | `StartTracer_SuppressedAck_AppliesVirtualEffectButKeepsCoreUncompleted`; Suppress ACK path | test+log (transient allowed) |
| CommunicationLost | P5-T1 tests @ `8fabaeb`; runtime transport failure tests | test+log; operator PNG pending |
| WaitingForFreshInput | `CycleAsync_AfterReadFault_RejectsCopiedOldIncarnationAndAcceptsCurrentReset`; P6-T1 mapping test | test+log; operator PNG pending |
| Recovery-ready (no Reset) | `CycleAsync_AfterSynchronizedSafeInput_NewAcknowledgeReachesRecoveryReadyWithoutReset` | test+log; operator PNG pending |

## Operator capture procedure (not executed here)

1. Checkout `2d29933` or the SHA after a fresh Release re-run.
2. Run `ChamberControlSimulator` (WinForms).
3. Capture **app window only**; Event Log readable; Simulation / Fault Injection distinct from operator commands.
4. Save new files under `docs/demo/images/p7-s01-heating.png` etc. Do not overwrite P0 images as current evidence.
5. Close the app, re-run Release test, bind PNGs to that SHA.

## Nonclaims

These rows are not live GUI evidence. Reset success is not claimed. Modbus/device/safety not claimed. No fake screenshots.
