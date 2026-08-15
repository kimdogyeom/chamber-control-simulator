using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Application.Tests;

[TestClass]
public sealed class EquipmentCoordinatorTests
{
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
		public PlcConnectionState ConnectionState { get; private set; } = PlcConnectionState.Disconnected;

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
