using ChamberControlSimulator.Core;

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

public sealed class EquipmentCommandCoordinator
{
	private readonly object _gate = new();
	private readonly ThermalController _controller;
	private ControllerCommandReservation? _reservation;
	private EquipmentCommandAdmission? _pendingCommand;
	private long? _nextCommandId = 1;

	public EquipmentCommandCoordinator(ThermalController controller)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
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
}
