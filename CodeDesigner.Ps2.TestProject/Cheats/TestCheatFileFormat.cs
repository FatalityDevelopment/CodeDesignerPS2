using System.Linq;
using CodeDesigner.Ps2.CheatEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestCheatFileFormat
    {
        [TestMethod]
        public void Parse_AllowsQuestionAndAsteriskPlaceholdersInCodeLines()
        {
            const string text = """
[--SectionStart:Porting--]

[Find Health]
20?????? 0000****
???????? ********
""";

            var result = CheatFileFormat.Parse(text);

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.AreEqual(1, result.Entries.Count);
            CollectionAssert.AreEqual(
                new[] { "20?????? 0000****", "???????? ********" },
                result.Entries.Single().CodeLines);
        }
    }
}
