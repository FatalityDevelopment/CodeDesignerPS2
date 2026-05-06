using CodeDesigner.Ps2.FileFormats.Zipper.ZAR;
using CodeDesigner.Ps2.FileFormats.Zipper.ZED;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public sealed class TestZedContainer
    {
        [TestMethod]
        public void TestZarArchiveRoundTrip()
        {
            using ZarArchive archive = ZarArchive.CreateNew(
                stringBasePointer: 0x151CF738u,
                rootValue: 2u,
                headerWord5C: 0x00000001u);

            byte[] aimaps = { 0x01, 0x02, 0x03 };
            byte[] map = { 0x10, 0x20, 0x30, 0x40 };

            archive.AddEntry("aimaps.rdr", aimaps);
            archive.AddEntry("map.rdr", map);

            byte[] encoded = archive.ToBytes();

            using ZarArchive reopened = ZarArchive.FromBytes(encoded);

            Assert.AreEqual(2, reopened.EntryCount);
            Assert.AreEqual(2u, reopened.RootValue);

            ZedNode first = reopened.GetEntry(0);
            ZedNode second = reopened.GetEntry(1);

            Assert.AreEqual("aimaps.rdr", first.Name);
            Assert.AreEqual("map.rdr", second.Name);
            Assert.AreEqual(0u, first.DataOffset);
            Assert.AreEqual(0x10u, second.DataOffset);

            CollectionAssert.AreEqual(aimaps, first.Data);
            CollectionAssert.AreEqual(map, second.Data);
            CollectionAssert.AreEqual(encoded, reopened.ToBytes());
        }

        [TestMethod]
        public void TestZedStructureRoundTrip()
        {
            using ZedStructure structure = ZedStructure.CreateNew(
                stringBasePointer: 0x13A79FF8u,
                rootValue: 1u,
                headerWord5C: 0x00010001u);

            byte[] nparams = Enumerable.Range(0, 0x60).Select(i => (byte)i).ToArray();
            byte[] vparams = Enumerable.Range(0, 0x18).Select(i => (byte)(0x80 + i)).ToArray();

            structure.AddNode("models", System.Array.Empty<byte>(), type: 1u);
            structure.AddNode("car_benz_black", System.Array.Empty<byte>(), type: 2u);
            structure.AddNode("nparams", nparams, type: 0u);
            structure.AddNode("vparams", vparams, type: 1u);

            byte[] encoded = structure.ToBytes();

            using ZedStructure reopened = ZedStructure.FromBytes(encoded);

            Assert.AreEqual(4, reopened.NodeCount);
            Assert.AreEqual(1u, reopened.RootValue);
            Assert.AreEqual(0x00010001u, reopened.HeaderWord5C);

            ZedNode root = reopened.GetNode(0);
            ZedNode child = reopened.GetNode(1);
            ZedNode nparamsNode = reopened.GetNode(2);
            ZedNode vparamsNode = reopened.GetNode(3);

            Assert.AreEqual("models", root.Name);
            Assert.AreEqual("car_benz_black", child.Name);
            Assert.AreEqual("nparams", nparamsNode.Name);
            Assert.AreEqual("vparams", vparamsNode.Name);

            Assert.AreEqual(1u, root.Type);
            Assert.AreEqual(2u, child.Type);
            Assert.AreEqual(0u, nparamsNode.DataOffset);
            Assert.AreEqual(0x60u, vparamsNode.DataOffset);

            CollectionAssert.AreEqual(nparams, nparamsNode.Data);
            CollectionAssert.AreEqual(vparams, vparamsNode.Data);
            CollectionAssert.AreEqual(encoded, reopened.ToBytes());
        }
    }
}
