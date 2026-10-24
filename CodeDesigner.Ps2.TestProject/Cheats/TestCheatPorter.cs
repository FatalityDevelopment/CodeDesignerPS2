using System;
using System.Collections.Generic;
using System.Linq;
using CodeDesigner.Ps2.CheatEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for <see cref="CheatPorter"/> — relocating raw GameShark codes between two
    /// EE RAM dumps. The dumps are synthetic: random bytes with regions copied, shifted
    /// or duplicated so each test controls exactly where a pattern can be found.
    /// </summary>
    [TestClass]
    public class TestCheatPorter
    {
        private const int Mb = 1024 * 1024;

        private static byte[] RandomDump(int size, int seed)
        {
            var data = new byte[size];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static List<PortCheatResult> Port(byte[] source, byte[] target, string codes)
            => CheatPorter.Port(source, target, new CheatList(codes).Parse());

        [TestMethod]
        public void Port_UnmovedAddress_StaysPut_EvenWhenPatternIsDuplicatedElsewhere()
        {
            var source = RandomDump(2 * Mb, 1);
            // The 48 bytes before the cheat address also exist, unchanged, at a lower
            // address in both dumps (e.g. a duplicated table).
            Array.Copy(source, 0x100000 - 48, source, 0x40000 - 48, 48);
            var target = (byte[])source.Clone();

            var r = Port(source, target, "Test\n20100000 00000001").Single();

            Assert.IsTrue(r.Found);
            Assert.AreEqual("20100000 00000001", r.NewCode);
        }

        [TestMethod]
        public void Port_ShiftedAddress_IsRelocated()
        {
            var source = RandomDump(2 * Mb, 2);
            var target = new byte[source.Length];
            Array.Copy(source, 0, target, 0x40, source.Length - 0x40);

            var r = Port(source, target, "Test\n20100000 00000001").Single();

            Assert.IsTrue(r.Found);
            Assert.AreEqual("20100040 00000001", r.NewCode);
            Assert.AreEqual(100, r.Confidence);
        }

        [TestMethod]
        public void Port_AddressAbove16Mb_UsesFull25BitAddress()
        {
            var source = RandomDump(32 * Mb, 3);
            var target = (byte[])source.Clone();
            // Only the upper 16 MB moves; the low half is identical in both dumps.
            Array.Copy(source, 16 * Mb, target, 16 * Mb + 0x40, 16 * Mb - 0x40);

            var r = Port(source, target, "Test\n21234560 00000001").Single();

            Assert.IsTrue(r.Found);
            Assert.AreEqual("212345A0 00000001", r.NewCode);
        }

        [TestMethod]
        public void Port_SerialWrite_SecondLineIsCopiedVerbatim()
        {
            var source = RandomDump(2 * Mb, 4);
            var target = new byte[source.Length];
            Array.Copy(source, 0, target, 0x40, source.Length - 0x40);

            var results = Port(source, target, "Test\n40100000 00040002\n00000001 00000000");

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("40100040 00040002", results[0].NewCode);
            Assert.AreEqual("00000001 00000000", results[1].NewCode);
            Assert.IsTrue(results[1].Found);
        }

        [TestMethod]
        public void Port_MultiLineConditional_RelocatesAddressInSecondWord()
        {
            var source = RandomDump(2 * Mb, 5);
            var target = new byte[source.Length];
            Array.Copy(source, 0, target, 0x40, source.Length - 0x40);

            var results = Port(source, target, "Test\nE0010001 00100000\n20110000 00000001");

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("E0010001 00100040", results[0].NewCode);
            Assert.AreEqual("20110040 00000001", results[1].NewCode);
        }

        [TestMethod]
        public void Port_CopyBytes_RelocatesSourceAndDestination()
        {
            var source = RandomDump(2 * Mb, 6);
            var target = new byte[source.Length];
            Array.Copy(source, 0, target, 0x40, source.Length - 0x40);

            var results = Port(source, target, "Test\n50100000 00000010\n00120000 00000000");

            Assert.AreEqual("50100040 00000010", results[0].NewCode);
            Assert.AreEqual("00120040 00000000", results[1].NewCode);
        }

        [TestMethod]
        public void Port_PointerWrite_KeepsOffsetLines()
        {
            var source = RandomDump(2 * Mb, 7);
            var target = new byte[source.Length];
            Array.Copy(source, 0, target, 0x40, source.Length - 0x40);

            // 3 pointer levels: the first offset is on line 2, two more on line 3.
            var results = Port(source, target, "Test\n60100000 00000001\n00020003 00000010\n00000020 00000030");

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual("60100040 00000001", results[0].NewCode);
            Assert.AreEqual("00020003 00000010", results[1].NewCode);
            Assert.AreEqual("00000020 00000030", results[2].NewCode);
        }

        [TestMethod]
        public void Port_ResultsCarryLineIndexAndSourceAddress()
        {
            var source = RandomDump(2 * Mb, 8);
            var target = (byte[])source.Clone();

            var results = Port(source, target, "A\n20100000 00000001\n20100000 00000001\n\nB\n20110000 00000002");

            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, results.Select(r => r.LineIndex).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, results.Select(r => r.BlockIndex).ToArray());
            Assert.AreEqual(0x100000, results[0].SourceAddress);
            Assert.AreEqual(0x100000, results[0].NewAddress);
        }
    }
}
