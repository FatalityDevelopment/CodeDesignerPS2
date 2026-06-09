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

        [TestMethod]
        public void Parse_IgnoresLineAndBlockComments()
        {
            const string text = """
// Cheats under test
[--SectionStart:Visuals--]

[Flash 20x] // display-only note
2028BE20 3C0344FA // command + split data
/* Temporarily disabled while porting:
[Draft]
2028BE20 3C0367FA
*/
[Hardcore Flash]
2028BE20 3C0367FA
""";

            var result = CheatFileFormat.Parse(text);

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.AreEqual(2, result.Entries.Count);
            Assert.AreEqual("Flash 20x", result.Entries[0].Label);
            Assert.AreEqual("Hardcore Flash", result.Entries[1].Label);
            CollectionAssert.AreEqual(new[] { "2028BE20 3C0344FA" }, result.Entries[0].CodeLines);
            CollectionAssert.AreEqual(new[] { "2028BE20 3C0367FA" }, result.Entries[1].CodeLines);
        }

        [TestMethod]
        public void SerializeAndParse_PreservesDescriptionAndAuthorComments()
        {
            var text = CheatFileFormat.Serialize(new[]
            {
                new CheatFileEntry
                {
                    Label = "Infinite Health",
                    Section = "Player",
                    Description = "Keeps health full.",
                    Author = "Archivist",
                    CodeLines = new() { "2028BE20 3C0344FA" },
                },
            });

            StringAssert.Contains(text, "// Description: Keeps health full.");
            StringAssert.Contains(text, "// Author: Archivist");

            var result = CheatFileFormat.Parse(text);

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.AreEqual("Keeps health full.", result.Entries.Single().Description);
            Assert.AreEqual("Archivist", result.Entries.Single().Author);
        }

    }
}
