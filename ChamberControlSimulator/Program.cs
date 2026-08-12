using ChamberControlSimulator.Presentation;
using ChamberControlSimulator.Core;

namespace ChamberControlSimulator
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
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

			var form = new Form1();

			var presenter = new EquipmentPresenter(
				form,
				controller);

			Application.Run(form);
		}
	}
}