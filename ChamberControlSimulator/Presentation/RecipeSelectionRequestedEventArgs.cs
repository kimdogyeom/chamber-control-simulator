namespace ChamberControlSimulator.Presentation;

public sealed class RecipeSelectionRequestedEventArgs : EventArgs
{
	public RecipeSelectionRequestedEventArgs(string recipeName)
	{
		RecipeName = recipeName;
	}

	public string RecipeName { get; }
}