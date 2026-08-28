using ChamberControlSimulator.Core;

namespace ChamberControlSimulator.Presentation.Tests;

internal static class ThermalControllerTestCommands
{
	public static void CompleteStart(ThermalController controller)
	{
		var reservation = controller.TryReserveCommand(ControllerCommandKind.Start);
		if (reservation is null)
		{
			return;
		}

		if (!controller.TryCompleteAcknowledgedCommand(reservation))
		{
			throw new InvalidOperationException("Unable to complete reserved Start.");
		}
	}
}
