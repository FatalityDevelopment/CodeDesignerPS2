using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class CdTextTests
{
    [TestMethod]
    [DataRow("0x80010000", 0x80010000u)]
    [DataRow("0X1a", 0x1Au)]
    [DataRow("  0x10  ", 0x10u)]
    [DataRow("4096", 4096u)]
    [DataRow("0", 0u)]
    [DataRow("0xFFFFFFFF", 0xFFFFFFFFu)]
    public void TryParseAddress_Valid_Parses(string input, uint expected)
        => Assert.AreEqual(expected, CdText.TryParseAddress(input));

    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("zzz")]
    [DataRow("0xZZZ")]
    [DataRow("0x100000000")] // overflows uint
    [DataRow("-1")]
    public void TryParseAddress_Invalid_ReturnsNull(string? input)
        => Assert.IsNull(CdText.TryParseAddress(input));

    [TestMethod]
    public void HexDump_FormatsOffsetsAndBytes()
    {
        var dump = CdText.HexDump(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 0x80000000);
        StringAssert.Contains(dump, "0x80000000");
        StringAssert.Contains(dump, "80000000:");
        StringAssert.Contains(dump, "DE AD BE EF");
        StringAssert.Contains(dump, "(4 bytes)");
    }

    [TestMethod]
    public void HexDump_WrapsAt16BytesPerLine()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var lines = CdText.HexDump(bytes, 0).Split('\n');
        // header + 2 data lines (16 + 4)
        Assert.AreEqual(3, lines.Length);
        StringAssert.Contains(lines[2], "00000010:");
    }

    [TestMethod]
    public void Message_ExtractsErrorField()
        => Assert.AreEqual("Error: boom", CdText.Message("{\"Error\":\"boom\"}"));

    [TestMethod]
    public void Message_ExtractsMessageField()
        => Assert.AreEqual("hello", CdText.Message("{\"Message\":\"hello\"}"));

    [TestMethod]
    public void Message_NonJson_ReturnsRaw()
        => Assert.AreEqual("not json", CdText.Message("not json"));

    [TestMethod]
    public void Error_PrefixesWithError()
        => Assert.AreEqual("Error: bad input", CdText.Error("bad input"));

    [TestMethod]
    public void TryGetData_ReturnsBase64Field()
        => Assert.AreEqual("QUJD", CdText.TryGetData("{\"Data\":\"QUJD\"}"));

    [TestMethod]
    public void TryGetData_MissingOrInvalid_ReturnsNull()
    {
        Assert.IsNull(CdText.TryGetData("{\"Other\":1}"));
        Assert.IsNull(CdText.TryGetData("garbage"));
    }
}
