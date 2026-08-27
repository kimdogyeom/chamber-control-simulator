using ChamberControlSimulator.Application;
using ChamberControlSimulator.Core;
using ChamberControlSimulator.Plc.Simulation;
using ChamberControlSimulator.Presentation;

namespace ChamberControlSimulator
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		static async Task Main()
		{
			// To customize application configuration such as set high DPI settings or default font,
			// see https://aka.ms/applicationconfiguration.
			ApplicationConfiguration.Initialize();

			var recipes = new[]
			{
				new Recipe("Standard 250C", targetTemperature: 250, safetyTemperature: 300),
				new Recipe("High Temp 300C", targetTemperature: 300, safetyTemperature: 350)
			};

			var controller = new ThermalController(
				recipes,
				SimulationSettings.Illustrative);

			var virtualPlc = new VirtualPlcClient(VirtualPlcOptions.Illustrative);
			var commandRuntime = new EquipmentCommandRuntime(
				controller,
				virtualPlc,
				virtualPlc,
				TimeProvider.System);

			using var form = new Form1();
			var observationRuntime = CreateObservationRuntime(commandRuntime, virtualPlc);
			await using var presenter = new EquipmentPresenter(
				form,
				controller,
				observationRuntime,
				observationRuntime);

			System.Windows.Forms.Application.Run(form);
		}

		internal static EquipmentObservationRuntime CreateObservationRuntime(
			EquipmentCommandRuntime commandRuntime,
			VirtualPlcClient virtualPlc)
		{
			ArgumentNullException.ThrowIfNull(commandRuntime);
			ArgumentNullException.ThrowIfNull(virtualPlc);

			return new EquipmentObservationRuntime(
				commandRuntime,
				virtualPlc.ObservationInputControl,
				virtualPlc.SimulationControl);
		}
	}
}
