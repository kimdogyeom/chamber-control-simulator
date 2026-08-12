namespace ChamberControlSimulator.Presentation
{
	public sealed class TimerTickedEventArgs : EventArgs
	{
		public TimerTickedEventArgs(TimeSpan elapsed)
		{
			Elapsed = elapsed;
		}

		public TimeSpan Elapsed { get; }
	}
}