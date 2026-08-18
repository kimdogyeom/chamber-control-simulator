using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Abstractions;

namespace ChamberControlSimulator.Application;

public enum EquipmentCommandAdmissionDisposition
{
	Accepted,
	Busy,
	Ineligible
}

public sealed record EquipmentCommandAdmission
{
	public EquipmentCommandAdmission(long commandId, ControllerCommandKind kind)
	{
		if (commandId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(commandId));
		}

		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		CommandId = commandId;
		Kind = kind;
	}

	public long CommandId { get; }

	public ControllerCommandKind Kind { get; }
}

public sealed record EquipmentCommandAdmissionResult
{
	public EquipmentCommandAdmissionResult(
		EquipmentCommandAdmissionDisposition disposition,
		EquipmentCommandAdmission? admission)
	{
		if (!Enum.IsDefined(disposition))
		{
			throw new ArgumentOutOfRangeException(nameof(disposition));
		}

		if ((disposition == EquipmentCommandAdmissionDisposition.Accepted) != (admission is not null))
		{
			throw new ArgumentException("Only accepted admission results may include a command.", nameof(admission));
		}

		Disposition = disposition;
		Admission = admission;
	}

	public EquipmentCommandAdmissionDisposition Disposition { get; }

	public EquipmentCommandAdmission? Admission { get; }
}

public enum EquipmentCommandTransportDisposition
{
	AwaitingAcknowledgement,
	DeliveryIndeterminate
}

public sealed record EquipmentCommandTransportResult
{
	public EquipmentCommandTransportResult(
		long commandId,
		EquipmentCommandTransportDisposition disposition)
	{
		if (commandId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(commandId));
		}

		if (!Enum.IsDefined(disposition))
		{
			throw new ArgumentOutOfRangeException(nameof(disposition));
		}

		CommandId = commandId;
		Disposition = disposition;
	}

	public long CommandId { get; }

	public EquipmentCommandTransportDisposition Disposition { get; }
}

public sealed class EquipmentCommandCoordinator
{
	private readonly object _gate = new();
	private readonly ThermalController _controller;
	private readonly IPlcOutputPort _outputPort;
	private ControllerCommandReservation? _reservation;
	private EquipmentCommandAdmission? _pendingCommand;
	private bool _dispatchStarted;
	private long? _nextCommandId = 1;

	public EquipmentCommandCoordinator(
		ThermalController controller,
		IPlcOutputPort outputPort)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
		_outputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
	}

	public EquipmentCommandAdmission? PendingCommand
	{
		get
		{
			lock (_gate)
			{
				return _pendingCommand;
			}
		}
	}

	public EquipmentCommandAdmissionResult TryAdmit(ControllerCommandKind kind)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		lock (_gate)
		{
			if (_pendingCommand is not null)
			{
				if (_reservation is null)
				{
					throw new InvalidOperationException("Pending command is missing its Core reservation.");
				}

				return new EquipmentCommandAdmissionResult(EquipmentCommandAdmissionDisposition.Busy, null);
			}

			if (_nextCommandId is null)
			{
				throw new InvalidOperationException("Command ID allocation is exhausted.");
			}

			var commandId = _nextCommandId.Value;
			var reservation = _controller.TryReserveCommand(kind);
			if (reservation is null)
			{
				return new EquipmentCommandAdmissionResult(EquipmentCommandAdmissionDisposition.Ineligible, null);
			}

			_nextCommandId = commandId == long.MaxValue ? null : commandId + 1;
			_pendingCommand = new EquipmentCommandAdmission(commandId, kind);
			_reservation = reservation;
			return new EquipmentCommandAdmissionResult(EquipmentCommandAdmissionDisposition.Accepted, _pendingCommand);
		}
	}

	public async Task<EquipmentCommandTransportResult> DispatchPendingAsync(CancellationToken cancellationToken)
	{
		EquipmentCommandAdmission pendingCommand;
		lock (_gate)
		{
			pendingCommand = _pendingCommand ?? throw new InvalidOperationException("No pending command is available for dispatch.");
			if (_reservation is null)
			{
				throw new InvalidOperationException("Pending command is missing its Core reservation.");
			}

			if (_dispatchStarted)
			{
				throw new InvalidOperationException("The pending command dispatch has already started.");
			}

			_dispatchStarted = true;
		}

		var outputCommand = new PlcOutputCommand(pendingCommand.CommandId, MapCommandKind(pendingCommand.Kind));
		var receipt = await _outputPort.WriteOutputsAsync(outputCommand, cancellationToken).ConfigureAwait(false) ??
			throw new InvalidOperationException("The PLC output port returned no transport receipt.");
		var disposition = receipt.CommandId == pendingCommand.CommandId &&
			receipt.TransportStatus == PlcTransportWriteStatus.Written
			? EquipmentCommandTransportDisposition.AwaitingAcknowledgement
			: EquipmentCommandTransportDisposition.DeliveryIndeterminate;

		return new EquipmentCommandTransportResult(pendingCommand.CommandId, disposition);
	}

	private static PlcCommandKind MapCommandKind(ControllerCommandKind kind) => kind switch
	{
		ControllerCommandKind.Start => PlcCommandKind.Start,
		ControllerCommandKind.Stop => PlcCommandKind.Stop,
		ControllerCommandKind.Reset => PlcCommandKind.Reset,
		_ => throw new InvalidOperationException($"Unsupported controller command kind: {kind}.")
	};
}
