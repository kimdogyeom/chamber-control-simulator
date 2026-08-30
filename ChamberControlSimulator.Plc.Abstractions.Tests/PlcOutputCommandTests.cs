using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcOutputCommandTests
{
	[TestMethod]
	public void Constructor_ValidCommand_ExposesSingleImmutableKind()
	{
		var command = new PlcOutputCommand(7, PlcCommandKind.Reset);

		Assert.AreEqual(7L, command.CommandId);
		Assert.AreEqual(PlcCommandKind.Reset, command.Kind);
		Assert.IsFalse(typeof(PlcOutputCommand)
			.GetProperties()
			.Any(property => property.SetMethod is { IsPublic: true }));
	}

	[TestMethod]
	public void Constructor_NonPositiveCommandId_Throws()
	{
		foreach (var commandId in new[] { 0L, -1L })
		{
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
				() => new PlcOutputCommand(commandId, PlcCommandKind.Start));

			Assert.AreEqual("commandId", exception.ParamName);
		}
	}

	[TestMethod]
	public void Constructor_UndefinedCommandKind_Throws()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new PlcOutputCommand(1, (PlcCommandKind)99));

		Assert.AreEqual("kind", exception.ParamName);
	}

	[TestMethod]
	public void CommandKind_DefinesSingleOneShotCommandsInOrder()
	{
		CollectionAssert.AreEqual(
			new[]
			{
				PlcCommandKind.Start,
				PlcCommandKind.Stop,
				PlcCommandKind.Reset,
				PlcCommandKind.Abort
			},
			Enum.GetValues<PlcCommandKind>());
	}
}
