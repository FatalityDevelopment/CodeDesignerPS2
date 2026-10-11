using CodeDesigner.Ps2.FileFormats.Zipper.ZDB;
using CodeDesigner.Ps2.FileFormats.Zipper.ZED;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Round-trips REAL retail SOCOM II files through the Zipper readers/writers: open, serialise unchanged,
    /// and require byte-identical output. This is the gate before any edited ZED/ZDB is ever served to the game.
    /// The files are retail data and are not committed: extract them into Mods/SOCOM2/packs (MP53.ZDB and
    /// mp53_clib/CLIB_*.ZED) or point SOCOM2_PACKS at a folder holding them; otherwise the tests are inconclusive.
    /// </summary>
    [TestClass]
    public sealed class TestZipperRealFileRoundTrip
    {
        private static string? PacksDir()
        {
            string? env = Environment.GetEnvironmentVariable("SOCOM2_PACKS");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Mods", "SOCOM2", "packs");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string RequireFile(params string[] parts)
        {
            string? packs = PacksDir();
            if (packs == null) Assert.Inconclusive("Mods/SOCOM2/packs not found (retail data is not committed).");
            string path = Path.Combine(new[] { packs! }.Concat(parts).ToArray());
            if (!File.Exists(path)) Assert.Inconclusive($"{path} not extracted.");
            return path;
        }

        private static void AssertIdentical(byte[] original, byte[] written, string what)
        {
            int n = Math.Min(original.Length, written.Length);
            for (int i = 0; i < n; i++)
            {
                if (original[i] != written[i])
                    Assert.Fail($"{what}: first difference at 0x{i:X} (original 0x{original[i]:X2}, written 0x{written[i]:X2}); " +
                                $"sizes {original.Length} vs {written.Length}.");
            }
            Assert.AreEqual(original.Length, written.Length, $"{what}: size differs (common prefix identical).");
        }

        [DataTestMethod]
        [DataRow("CLIB_TXR.ZED")]
        [DataRow("CLIB_PAL.ZED")]
        [DataRow("CLIB_GEO.ZED")]
        [DataRow("CLIB_MDL.ZED")]
        public void TestRetailZedRoundTripIsByteIdentical(string file)
        {
            byte[] original = File.ReadAllBytes(RequireFile("mp53_clib", file));
            using var zed = ZedContainer.FromBytes(original);
            AssertIdentical(original, zed.ToBytes(), file);
        }

        [TestMethod]
        public void TestRetailZdbRoundTripIsByteIdentical()
        {
            byte[] original = File.ReadAllBytes(RequireFile("MP53.ZDB"));
            var zdb = ZdbArchive.FromBytes(original);
            AssertIdentical(original, zdb.ToBytes(), "MP53.ZDB");
        }
    }
}
