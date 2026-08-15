using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChamberControlSimulator.Plc.Abstractions.Tests;

[TestClass]
public sealed class PlcWriteReceiptTests
{
	[TestMethod]
	public void Constructor_WrittenReceipt_ExposesImmutableTransportResultWithoutAckVocabulary()
	{
		var receipt = new PlcWriteReceipt(7, PlcTransportWriteStatus.Written);

		Assert.AreEqual(7L, receipt.CommandId);
		Assert.AreEqual(PlcTransportWriteStatus.Written, receipt.TransportStatus);
		Assert.IsFalse(typeof(PlcWriteReceipt)
			.GetProperties()
			.Any(property => property.SetMethod is { IsPublic: true }));
		Assert.IsFalse(typeof(PlcWriteReceipt)
			.GetMembers()
			.Any(member => member.Name.Contains("Ack", StringComparison.OrdinalIgnoreCase)));
	}

	[TestMethod]
	public void Constructor_NonPositiveCommandId_Throws()
	{
		foreach (var commandId in new[] { 0L, -1L })
		{
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
				() => new PlcWriteReceipt(commandId, PlcTransportWriteStatus.Written));

			Assert.AreEqual("commandId", exception.ParamName);
		}
	}

	[TestMethod]
	public void Constructor_UndefinedTransportStatus_Throws()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new PlcWriteReceipt(1, (PlcTransportWriteStatus)99));

		Assert.AreEqual("transportStatus", exception.ParamName);
	}

	[TestMethod]
	public void TransportWriteStatus_DefinesTransportOutcomesInOrder()
	{
		CollectionAssert.AreEqual(
			new[]
			{
				PlcTransportWriteStatus.Written,
				PlcTransportWriteStatus.Failed
			},
			Enum.GetValues<PlcTransportWriteStatus>());
	}
}
