# P7-T4 app-only capture status

## Authority

- Operator captures taken on current MainForm UI after Abort / disconnect-heater work.
- Release automated evidence at source HEAD `f600f2a846193216c6ac5d2bf89bc11fb99a0f37`: 209/209.
- Documentation commit that adds these PNGs: `59c4ee7`.

## Decision

Six live app-window PNGs are tracked under `docs/demo/images/p7-*.png`. P0 `docs/demo/images/00-idle.png` … remain historical Presenter/Core screenshots and are **not** current UI evidence. Staging copies under `docs/image/` are local-only.

ACK timeout and WaitingForFreshInput are **test+log**. They are not required portfolio frames.

## Captured states

| State | File | Notes |
| --- | --- | --- |
| Idle baseline | [`p7-idle.png`](../demo/images/p7-idle.png) | IDLE, 20 °C, Command None, Start enabled |
| Normal ACK Start/Heating | [`p7-s01-heating.png`](../demo/images/p7-s01-heating.png) | Heating, Start #1 Completed |
| DoorOpen alarm | [`p7-s03-door-open.png`](../demo/images/p7-s03-door-open.png) | Alarm DoorOpen, Reset disabled |
| CommunicationLost | [`p7-s08-communication-lost.png`](../demo/images/p7-s08-communication-lost.png) | Alarm CommunicationLost after Disconnect; Connection may already be Connected |
| Recovery-ready (no Reset) | [`p7-s10-recovery-ready.png`](../demo/images/p7-s10-recovery-ready.png) | Recovery Ready Yes, Reset enabled, Event Log has no Reset |
| Software Abort | [`p7-s13-abort.png`](../demo/images/p7-s13-abort.png) | Abort #2 Completed, red Abort button, not E-Stop; post-complete Idle resembles idle baseline |

## Not captured (explicit)

| State | Reason |
| --- | --- |
| ACK timeout | Transient; test+log |
| WaitingForFreshInput | Sub-cycle; test+log |
| Reset success | Not claimed |

## Nonclaims

Reset success, Modbus, real equipment, hardware E-Stop/Safety PLC. Abort is a PC heater-off preemption.
