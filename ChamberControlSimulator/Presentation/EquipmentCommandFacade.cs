using ChamberControlSimulator.Application;

namespace ChamberControlSimulator.Presentation
{
	internal sealed class EquipmentCommandFacade : IEquipmentCommandRuntime
	{
		private readonly EquipmentCommandRuntime _commandRuntime;

		public EquipmentCommandFacade(EquipmentCommandRuntime commandRuntime)
		{
			_commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
		}

		public EquipmentCommandLifecycleState CurrentState => _commandRuntime.CurrentState;

		public Task<EquipmentCommandRequestResult> RequestStartAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestStartAsync(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestStopAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestStopAsync(cancellationToken);

		public Task<EquipmentCommandRequestResult> RequestResetAsync(CancellationToken cancellationToken) =>
			_commandRuntime.RequestResetAsync(cancellationToken);

		public void StopAdmission() => _commandRuntime.StopAdmission();
	}
}
