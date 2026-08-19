using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Application.Tests;

[TestClass]
public sealed class EquipmentCoordinatorTests
{
	// 목적: open-door PLC input이 coordinator의 Core observation mapping을 거쳐 active interlock alarm이 되는지 검증한다.
	// 예상 결과: Heating controller의 cycle result와 controller snapshot은 DoorOpen alarm을 나타내며 output write는 없다.
	// 완료 조건: coordinator가 PLC type을 Core에 전달하지 않고 one-cycle safety mapping을 test로 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenDoorIsOpenDuringHeating_MapsInputToDoorOpenAlarmWithoutWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(false, true, 20d, PlcMachineState.Idle, 0, 0));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, result.Disposition);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.DoorOpen, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: producer observation sequence가 증가하지 않으면 stale input이 Core 상태를 재적용하지 않는지 검증한다.
	// 예상 결과: 두 번째 cycle은 StaleObservation이고 첫 accepted door observation으로 만든 controller snapshot이 유지된다.
	// 완료 조건: initial sequence 0/이후 sequence의 strict-increase policy가 output write 없이 test로 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenObservationSequenceIsNotIncreasing_ReturnsStaleWithoutMutatingController()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var plc = new RecordingPlcClient(
			new PlcInputSnapshot(true, true, 20d, PlcMachineState.Idle, 0, 1),
			new PlcInputSnapshot(false, true, 20d, PlcMachineState.Idle, 0, 1));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var first = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);
		var second = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, first.Disposition);
		Assert.AreEqual(EquipmentCycleDisposition.StaleObservation, second.Disposition);
		Assert.IsFalse(second.ControllerSnapshot.IsDoorOpen);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: disconnected PLC port를 coordinator가 연결한 뒤 한 번의 input read만 처리하는지 검증한다.
	// 예상 결과: cycle은 Completed이고 controller snapshot과 accepted input을 반환하며 output write는 발생하지 않는다.
	// 완료 조건: Application이 PLC transport를 직접 구현하지 않고 read-only P3 cycle을 완결하는 상태로 test가 통과한다.
	[TestMethod]
	public async Task CycleAsync_WhenDisconnected_ConnectsReadsAndDoesNotWriteOutputs()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		var plc = new RecordingPlcClient(new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20d,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0));
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.Completed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Connected, result.ConnectionState);
		Assert.IsNotNull(result.InputSnapshot);
		Assert.AreEqual(0L, result.InputSnapshot.ObservationSequence);
		Assert.AreEqual(ControllerState.Idle, result.ControllerSnapshot.State);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 연결이 끊긴 활성 cycle의 ConnectAsync 전송 실패가 Core 통신 상실 알람으로 분류되지 않는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 controller는 Heating 상태와 알람 없음 상태를 유지하며 read/write가 없다.
	// 완료 조건: 연결 시도 1회, read 0회, write 0회이고 CommunicationLost가 발생하지 않는다.
	[TestMethod]
	public async Task CycleAsync_WhenConnectThrowsTransportException_ReturnsNonAlarmTransportFailure()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new RecordingPlcClient(new PlcInputSnapshot(
			doorClosed: true,
			sensorHealthy: true,
			currentTemperature: 20d,
			machineState: PlcMachineState.Idle,
			acknowledgedCommandId: 0,
			observationSequence: 0))
		{
			ThrowTransportExceptionOnConnect = true
		};
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Disconnected, result.ConnectionState);
		Assert.IsNull(result.InputSnapshot);
		Assert.AreEqual(ControllerState.Heating, result.ControllerSnapshot.State);
		Assert.IsNull(result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(1, plc.ConnectCallCount);
		Assert.AreEqual(0, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	// 목적: 활성 cycle의 typed PLC read fault가 Core 통신 알람으로 매핑되는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 controller는 CommunicationLost 알람이며 output write는 없다.
	// 완료 조건: cancellation으로 변환하지 않고 read boundary 결과와 fail-closed Core 상태가 함께 유지된다.
	[TestMethod]
	public async Task CycleAsync_WhenReadThrowsTransportException_RaisesCommunicationLostWithoutWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new TransportFailingObservationPort();
		using var cancellationSource = new CancellationTokenSource();
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, cancellationSource.Token);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.WriteCallCount);
		Assert.IsFalse(cancellationSource.IsCancellationRequested);
	}

	// 목적: Faulted 상태의 활성 관찰 포트가 재연결 없이 typed read 경계를 통과하는지 검증한다.
	// 예상 결과: cycle은 TransportFailed이고 Faulted를 유지하며 controller는 CommunicationLost 알람에 진입한다.
	// 완료 조건: connect 0회, read 1회, write 0회이고 Core 통신 상실 상태가 반환된다.
	[TestMethod]
	public async Task CycleAsync_WhenFaultedReadThrowsTransportException_RaisesCommunicationLostWithoutReconnectOrWrite()
	{
		var controller = new ThermalController(
			new Recipe("Test", targetTemperature: 30d, safetyTemperature: 35d),
			SimulationSettings.Illustrative);
		controller.Start();
		var plc = new TransportFailingObservationPort(PlcConnectionState.Faulted);
		await using var coordinator = new EquipmentCoordinator(controller, plc);

		var result = await coordinator.CycleAsync(TimeSpan.Zero, CancellationToken.None);

		Assert.AreEqual(EquipmentCycleDisposition.TransportFailed, result.Disposition);
		Assert.AreEqual(PlcConnectionState.Faulted, result.ConnectionState);
		Assert.IsNull(result.InputSnapshot);
		Assert.AreEqual(ControllerState.Alarm, result.ControllerSnapshot.State);
		Assert.AreEqual(AlarmKind.CommunicationLost, result.ControllerSnapshot.ActiveAlarm);
		Assert.AreEqual(0, plc.ConnectCallCount);
		Assert.AreEqual(1, plc.ReadCallCount);
		Assert.AreEqual(0, plc.WriteCallCount);
	}

	private sealed class TransportFailingObservationPort : IPlcClient
	{
		public TransportFailingObservationPort(
			PlcConnectionState connectionState = PlcConnectionState.Connected)
		{
			ConnectionState = connectionState;
		}

		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public PlcConnectionState ConnectionState { get; private set; }

		public Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			ConnectionState = PlcConnectionState.Connected;
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			throw new PlcTransportException("Confirmed read transport failure.");
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(
				command.CommandId,
				PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingPlcClient : IPlcClient
	{
		private readonly Queue<PlcInputSnapshot> _inputSnapshots;

		public RecordingPlcClient(params PlcInputSnapshot[] inputSnapshots)
		{
			if (inputSnapshots.Length == 0)
			{
				throw new ArgumentException("At least one input snapshot is required.", nameof(inputSnapshots));
			}

			_inputSnapshots = new Queue<PlcInputSnapshot>(inputSnapshots);
		}

		public int ConnectCallCount { get; private set; }
		public int ReadCallCount { get; private set; }
		public int WriteCallCount { get; private set; }
		public bool ThrowTransportExceptionOnConnect { get; init; }
		public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Disconnected;

		public Task ConnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectCallCount++;
			if (ThrowTransportExceptionOnConnect)
			{
				throw new PlcTransportException("Confirmed connect transport failure.");
			}
			ConnectionState = PlcConnectionState.Connected;
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConnectionState = PlcConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<PlcInputSnapshot> ReadInputsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCallCount++;
			if (_inputSnapshots.Count == 0)
			{
				throw new InvalidOperationException("No input snapshot remains.");
			}

			return Task.FromResult(_inputSnapshots.Dequeue());
		}

		public Task<PlcWriteReceipt> WriteOutputsAsync(
			PlcOutputCommand command,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WriteCallCount++;
			return Task.FromResult(new PlcWriteReceipt(
				command.CommandId,
				PlcTransportWriteStatus.Written));
		}

		public ValueTask DisposeAsync()
		{
			ConnectionState = PlcConnectionState.Disconnected;
			return ValueTask.CompletedTask;
		}
	}
}
