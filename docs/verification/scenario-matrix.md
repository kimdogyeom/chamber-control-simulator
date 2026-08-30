# P7-T1 scenario matrix

Authority: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`, `main`.  
Bound at documentation commit of this file. Automated test names exist at Release SHA `2d29933`. **App captures are test+log pending operator PNGs** (`docs/verification/p7-t4-app-captures.md`). Do not treat P0 `docs/demo/images/00-idle.png` … as current P6 UI.

P0 historical images are the wrong composition root (`Form1 → Presenter → ThermalController` without PLC observation runtime).

## S01 정상 cycle

- Automated: `ThermalControllerTests.ApplyObservation_ValidObservedSequence_ProgressesThroughNormalPhasesToComplete`; `EquipmentCommandRuntimeTests.CycleAsync_LaterExactFreshAcknowledgement_CompletesStartExactlyOnce`; `EquipmentCommandRuntimeVirtualPlcTests.StartTracer_ExactFreshVirtualAcknowledgement_CompletesCoreAfterSemanticPoint`
- Operator: Idle, door closed, sensor healthy, Start, wait matching ACK, hold, cool, Complete
- Expected UI: command Start Acknowledged then None; connection Connected; sync Synchronized; Core Complete
- Event Log: Start written / awaiting ACK / acknowledged; phase events
- Capture: Planned `docs/demo/images/p7-s01-heating.png` (P7-T4)

## S02 Idle Door gate

- Automated: `ThermalControllerTests.Start_WhenDoorIsOpen_RemainsIdleAndIsIneligible`
- Operator: Open Door in Simulation / Fault Injection, click Start
- Expected UI: Idle, Start disabled or no Heating, Door Open
- Capture: Planned optional `docs/demo/images/p7-s02-idle-door.png`

## S03 active Door interlock

- Automated: `ThermalControllerTests.OpenDoor_WhileHeating_EntersDoorOpenAlarm`; `EquipmentCoordinatorTests.CycleAsync_WhenDoorIsOpenDuringHeating_MapsInputToDoorOpenAlarmWithoutWrite`; `ThermalObservationTests.ApplyObservation_WhenDoorIsOpenDuringHeating_RaisesDoorOpenAlarm`
- Operator: Heating 중 Open Door
- Expected UI: Alarm DoorOpen; CanReset false until close+Ack+Recovery
- Capture: Planned `docs/demo/images/p7-s03-door-open.png`

## S04 over-temperature

- Automated: `ThermalControllerTests.ReportTemperature_AtSafetyLimit_AlarmsUntilTemperatureIsBelowLimit`; `ThermalObservationTests.ApplyObservation_AtSafetyTemperature_RaisesOverTemperatureAlarm`
- Operator: Apply temperature at/above safety
- Expected UI: Alarm OverTemperature
- Capture: Planned optional

## S05 sensor timeout

- Automated: `ThermalControllerTests.FeedbackPaused_PastTimeout_RequiresResumeAndFreshTickBeforeReset`; `ThermalObservationTests.ApplyObservation_AfterSensorTimeout_RequiresFreshHealthyObservationBeforeRecovery`
- Operator: Pause feedback past timeout
- Expected UI: Alarm SensorTimeout; Reset blocked until fresh healthy + Ack
- Capture: Planned optional

## S06 ACK timeout

- Automated: `EquipmentCommandRuntimeTests.RequestStartAsync_WriteStillInFlightAtReceiptDeadline_HoldsLeaseAndCannotRevive`; `EquipmentCommandRuntimeVirtualPlcTests.StartTracer_SuppressedAck_AppliesVirtualEffectButKeepsCoreUncompleted`; `VirtualPlcFaultControlTests.SuppressNextAcknowledgement_AppliesSemanticEffectButKeepsAckUnobserved`
- Operator: Suppress ACK, Start, wait past timeout
- Expected UI: Heating 진입 금지; command TimedOut / hold; Simulation / Fault Injection used
- Capture: Planned `docs/demo/images/p7-s06-ack-timeout.png` or test+log if transient

## S07 late ACK

- Automated: `EquipmentCommandRuntimeTests.CycleAsync_StaleLowerAndHigherAcknowledgements_DoNotComplete`; `CycleAsync_ExactAcknowledgementWithUnsafeObservation_CannotReviveAfterSafeAba`
- Operator: 새 pending 중 이전 ACK — UI에서 재현 어려움
- Evidence: **test+log only** (04 §7). No fake screenshot.

## S08 disconnect CommunicationLost

- Automated: `EquipmentCommandRuntimeTests.RequestStopAsync_TransportFailure_RaisesCommunicationLostAndPreservesReconciliationHold`; `EquipmentCoordinatorTests.CycleAsync_WhenReadThrowsTransportException_RaisesCommunicationLostWithoutWrite`; P5-T1 receipt `p5-t1-communication-lost.md` @ `8fabaeb`
- Operator: Heating 중 Disconnect
- Expected UI: Alarm CommunicationLost; connection Faulted/Disconnected; progress stopped
- Capture: Planned `docs/demo/images/p7-s08-communication-lost.png`

## S09 reconnect only / WaitingForFreshInput

- Automated: `EquipmentCoordinatorTests.CycleAsync_AfterReadFault_RejectsCopiedOldIncarnationAndAcceptsCurrentReset`; P5-T3 receipt `p5-t3-source-synchronization.md` @ `fc37338`
- Operator: Disconnect 후 재연결, 소켓만 살아 있음
- Expected UI: connection Connected **and** Synchronization WaitingForFreshInput; no Recovery from reconnect alone
- Capture: Planned `docs/demo/images/p7-s09-waiting-for-fresh-input.png`

## S10 Recovery-ready without Reset

- Automated: `EquipmentCoordinatorTests.CycleAsync_AfterSynchronizedSafeInput_NewAcknowledgeReachesRecoveryReadyWithoutReset`; P5-T4 receipt `p5-t4-fresh-safe-recovery.md` @ `00a1df2`
- Operator: synchronized safe input, **new** Acknowledge
- Expected UI: Recovery Ready Yes; Event Log has no Reset
- Capture: Planned `docs/demo/images/p7-s10-recovery-ready.png`
- Nonclaim: Reset success

## S11 compound fault

- Automated: `ThermalControllerTests` CommunicationLost+DoorOpen / OverTemperature cases; `EquipmentCommandRuntimeTests.RequestResetAsync_WhileReceiptTimedOutHold_RemainsRejectedEvenIfCoreRecoveryReady`; P5-T5 receipt `p5-t5-composite-alarms.md` @ `ee89095`
- Operator: CL plus open door or over-temp; comms-only evidence
- Expected UI: still Alarm; Reset admission rejected if P4 hold
- Capture: Planned test+log or optional log screenshot

## S12 shutdown

- Automated: Application close/teardown tests on `EquipmentPresenter` (`DisposeAsync_DuringCycleTaskPublication_WaitsForPublishedCycleBeforeRuntimeDisposal` and related)
- Operator: pending I/O 중 앱 종료
- Expected: cancellation, no duplicate reconnect
- Evidence: **process exit confirmation / test+log**. No fake screenshot.
## S13 Software Abort (not E-Stop)
- Automated: `EquipmentCommandRuntimeTests.RequestAbortAsync_WhileStartAwaitingAck_WritesAbortAndKeepsHoldUntilAck`; `CommandReservationTests.TryReserveAbortPreempting_InvalidatesOutstandingStartReservation`
- Operator: Start 대기 중 Software Abort
- Expected UI: Command Abort awaiting ACK; heater off after semantic apply; Alarm/Reset unchanged; label is Software Abort not E-Stop
- Evidence: **test+log**. Live PNG later (`docs/image` excluded from this commit).
## S14 command rejection label
- Automated: `EquipmentCommandRuntimeTests.RequestStopAsync_WhileAlarm_RejectsWithCoreIneligibleReason`; `StopRequested_WhileAlarm_MapsAdmissionRejectedWithoutAutomaticFlag`
- Operator: Alarm 중 Stop
- Expected UI: command sector `Stop rejected (not eligible)`; no MessageBox
- Evidence: **test+log**. Live PNG later.

## Nonclaims

Reset success, Modbus, real equipment, safety-rated, hardware E-Stop/Safety PLC, Software Abort as E-Stop, P0 image reuse as current UI, push/tag. Live `docs/image` PNGs are later operator work.
