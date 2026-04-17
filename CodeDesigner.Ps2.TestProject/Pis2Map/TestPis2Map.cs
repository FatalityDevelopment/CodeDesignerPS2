using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeDesigner.Ps2.Pis2Map;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestPis2Map
    {
        // Builds a minimal valid .pis byte array.
        // Format: magic(4) | filler(32) | 8×0x11 | lblSize(4) | unknown(4) | lblCount(4) | labels...
        private static byte[] BuildPis(params (uint addr, string name)[] labels)
        {
            var body = new System.Collections.Generic.List<byte>();
            body.AddRange(new byte[] { 0x50, 0x49, 0x53, 0x1F });
            body.AddRange(new byte[32]);
            body.AddRange(Enumerable.Repeat((byte)0x11, 8));
            int lblDataSize = labels.Sum(l => 4 + 1 + l.name.Length);
            body.AddRange(BitConverter.GetBytes((uint)lblDataSize));
            body.AddRange(BitConverter.GetBytes((uint)0));
            body.AddRange(BitConverter.GetBytes((uint)labels.Length));
            foreach (var (addr, name) in labels)
            {
                body.AddRange(BitConverter.GetBytes(addr));
                body.Add((byte)name.Length);
                body.AddRange(Encoding.ASCII.GetBytes(name));
            }
            return body.ToArray();
        }

        [TestMethod]
        public void PisConverter_InvalidMagic_Fails()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string pis = Path.Combine(tmp, "bad.pis");
                File.WriteAllBytes(pis, new byte[] { 0x00, 0x00, 0x00, 0x00 });
                var r = PisConverter.Convert(pis, Path.Combine(tmp, "out.map"));
                Assert.IsFalse(r.Success);
                Assert.IsFalse(string.IsNullOrEmpty(r.Error));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void PisConverter_MissingFile_Fails()
        {
            var r = PisConverter.Convert("/nonexistent/file.pis", "/tmp/out.map");
            Assert.IsFalse(r.Success);
            Assert.IsFalse(string.IsNullOrEmpty(r.Error));
        }

        [TestMethod]
        public void PisConverter_NoLabelBlock_Fails()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var data = new byte[64];
                data[0] = 0x50; data[1] = 0x49; data[2] = 0x53; data[3] = 0x1F;
                string pis = Path.Combine(tmp, "nolabel.pis");
                File.WriteAllBytes(pis, data);
                var r = PisConverter.Convert(pis, Path.Combine(tmp, "out.map"));
                Assert.IsFalse(r.Success);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void PisConverter_SingleLabel_WritesMap()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string pis = Path.Combine(tmp, "test.pis");
                string map = Path.Combine(tmp, "out.map");
                File.WriteAllBytes(pis, BuildPis((0x00100000u, "main")));

                var r = PisConverter.Convert(pis, map);
                Assert.IsTrue(r.Success);
                Assert.AreEqual(1, r.LabelCount);
                string content = File.ReadAllText(map);
                Assert.IsTrue(content.Contains("00100000"));
                Assert.IsTrue(content.Contains("main"));
                Assert.IsTrue(content.Contains(".code"));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void PisConverter_MultipleLabels_AllPresent()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string pis = Path.Combine(tmp, "multi.pis");
                string map = Path.Combine(tmp, "out.map");
                File.WriteAllBytes(pis, BuildPis(
                    (0x00200000u, "funcA"),
                    (0x00200100u, "funcB"),
                    (0x00200200u, "funcC")));

                var r = PisConverter.Convert(pis, map);
                Assert.IsTrue(r.Success);
                Assert.AreEqual(3, r.LabelCount);
                string content = File.ReadAllText(map);
                Assert.IsTrue(content.Contains("funcA") && content.Contains("00200000"));
                Assert.IsTrue(content.Contains("funcB") && content.Contains("00200100"));
                Assert.IsTrue(content.Contains("funcC") && content.Contains("00200200"));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void PisConverter_MapHeader_ContainsAuthor()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string pis = Path.Combine(tmp, "h.pis");
                string map = Path.Combine(tmp, "out.map");
                File.WriteAllBytes(pis, BuildPis((0x00300000u, "start")));
                PisConverter.Convert(pis, map);
                string content = File.ReadAllText(map);
                Assert.IsTrue(content.Contains("pis2map"));
                Assert.IsTrue(content.Contains("Gtlcpimp"));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void PisConverter_NoLabels_SucceedsWithZeroCount()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string pis = Path.Combine(tmp, "empty.pis");
                string map = Path.Combine(tmp, "out.map");
                File.WriteAllBytes(pis, BuildPis());
                var r = PisConverter.Convert(pis, map);
                Assert.IsTrue(r.Success);
                Assert.AreEqual(0, r.LabelCount);
                Assert.IsFalse(File.ReadAllText(map).Contains(".code"));
            }
            finally { Directory.Delete(tmp, true); }
        }
    }
}
