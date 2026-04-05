using CodeDesigner.Ps2.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public sealed class TestValueScanner
    {
        // ── ParseScanUInt64 ──────────────────────────────────────────────────

        [TestMethod]
        public void ParseUInt64_HexPrefixed()
        {
            Assert.AreEqual(0x0000FFFFul, SocketServer.ParseScanUInt64("0x0000FFFF"));
        }

        [TestMethod]
        public void ParseUInt64_HexPrefixedLower()
        {
            Assert.AreEqual(0xDEADBEEFul, SocketServer.ParseScanUInt64("0xdeadbeef"));
        }

        [TestMethod]
        public void ParseUInt64_BareHex()
        {
            Assert.AreEqual(0x0000FFFFul, SocketServer.ParseScanUInt64("0000FFFF"));
        }

        [TestMethod]
        public void ParseUInt64_BareHexLower()
        {
            Assert.AreEqual(0xABCDul, SocketServer.ParseScanUInt64("abcd"));
        }

        [TestMethod]
        public void ParseUInt64_Decimal()
        {
            Assert.AreEqual(65535ul, SocketServer.ParseScanUInt64("65535"));
        }

        [TestMethod]
        public void ParseUInt64_Zero()
        {
            Assert.AreEqual(0ul, SocketServer.ParseScanUInt64("0"));
        }

        [TestMethod]
        public void ParseUInt64_MaxValue()
        {
            Assert.AreEqual(ulong.MaxValue, SocketServer.ParseScanUInt64("0xFFFFFFFFFFFFFFFF"));
        }

        [TestMethod]
        public void ParseUInt64_WithWhitespace()
        {
            Assert.AreEqual(255ul, SocketServer.ParseScanUInt64("  0xFF  "));
        }

        // ── ParseScanInt64 ───────────────────────────────────────────────────

        [TestMethod]
        public void ParseInt64_Negative()
        {
            Assert.AreEqual(-1L, SocketServer.ParseScanInt64("-1"));
        }

        [TestMethod]
        public void ParseInt64_HexPrefixed()
        {
            Assert.AreEqual(0x7FFF_FFFF_FFFF_FFFFL, SocketServer.ParseScanInt64("0x7FFFFFFFFFFFFFFF"));
        }

        [TestMethod]
        public void ParseInt64_BareHex()
        {
            Assert.AreEqual(0xABCDL, SocketServer.ParseScanInt64("ABCD"));
        }

        [TestMethod]
        public void ParseInt64_Decimal()
        {
            Assert.AreEqual(42L, SocketServer.ParseScanInt64("42"));
        }

        // ── ParseScanUInt8 ───────────────────────────────────────────────────

        [TestMethod]
        public void ParseUInt8_HexPrefixed()
        {
            Assert.AreEqual((byte)0xFF, SocketServer.ParseScanUInt8("0xFF"));
        }

        [TestMethod]
        public void ParseUInt8_BareHex()
        {
            Assert.AreEqual((byte)0xAB, SocketServer.ParseScanUInt8("AB"));
        }

        [TestMethod]
        public void ParseUInt8_Decimal()
        {
            Assert.AreEqual((byte)128, SocketServer.ParseScanUInt8("128"));
        }

        [TestMethod]
        [ExpectedException(typeof(OverflowException))]
        public void ParseUInt8_Overflow()
        {
            SocketServer.ParseScanUInt8("0x1FF");
        }

        // ── LooksLikeBareHex ─────────────────────────────────────────────────

        [TestMethod]
        public void LooksLikeBareHex_WithLetters()
        {
            Assert.IsTrue(SocketServer.LooksLikeBareHex("0000FFFF"));
        }

        [TestMethod]
        public void LooksLikeBareHex_AllDigits()
        {
            // Pure digits — could be decimal, so don't treat as bare hex.
            Assert.IsFalse(SocketServer.LooksLikeBareHex("12345"));
        }

        [TestMethod]
        public void LooksLikeBareHex_MixedCase()
        {
            Assert.IsTrue(SocketServer.LooksLikeBareHex("DeAdBeEf"));
        }

        [TestMethod]
        public void LooksLikeBareHex_WithInvalidChar()
        {
            Assert.IsFalse(SocketServer.LooksLikeBareHex("0xGG"));
        }

        [TestMethod]
        public void LooksLikeBareHex_Empty()
        {
            Assert.IsFalse(SocketServer.LooksLikeBareHex(""));
        }

        // ── ScanValueMatches ─────────────────────────────────────────────────

        [TestMethod]
        public void ScanValueMatches_UInt32_Equal()
        {
            // 0x0000FFFF in little-endian = FF FF 00 00
            byte[] mem = { 0xFF, 0xFF, 0x00, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0x0000FFFF", "eq", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt32_BareHex()
        {
            byte[] mem = { 0xFF, 0xFF, 0x00, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0000FFFF", "eq", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt32_Decimal()
        {
            byte[] mem = { 0xFF, 0xFF, 0x00, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "65535", "eq", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt32_NotEqual()
        {
            byte[] mem = { 0x00, 0x00, 0x01, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0x0000FFFF", "ne", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt32_GreaterThan()
        {
            // mem = 0x00010000, scan = 0x0000FFFF → 0x10000 > 0xFFFF → true
            byte[] mem = { 0x00, 0x00, 0x01, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0x0000FFFF", "gt", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt32_LessThan()
        {
            // mem = 0x0000FFFE, scan = 0x0000FFFF → 0xFFFE < 0xFFFF → true
            byte[] mem = { 0xFE, 0xFF, 0x00, 0x00 };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0x0000FFFF", "lt", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt16_Equal()
        {
            byte[] mem = { 0xFF, 0xFF };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0xFFFF", "eq", "uint16"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt8_Equal()
        {
            byte[] mem = { 0xAB };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0xAB", "eq", "uint8"));
        }

        [TestMethod]
        public void ScanValueMatches_Int32_Negative()
        {
            // -1 in 32-bit LE = FF FF FF FF
            byte[] mem = { 0xFF, 0xFF, 0xFF, 0xFF };
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "-1", "eq", "int32"));
        }

        [TestMethod]
        public void ScanValueMatches_Float32_Equal()
        {
            byte[] mem = BitConverter.GetBytes(3.14f);
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "3.14", "eq", "float32"));
        }

        [TestMethod]
        public void ScanValueMatches_Float64_Equal()
        {
            byte[] mem = BitConverter.GetBytes(3.14);
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "3.14", "eq", "float64"));
        }

        [TestMethod]
        public void ScanValueMatches_WithOffset()
        {
            byte[] mem = { 0x00, 0x00, 0x00, 0x00,  0xFF, 0xFF, 0x00, 0x00 };
            Assert.IsFalse(SocketServer.ScanValueMatches(mem, 0, "0x0000FFFF", "eq", "uint32"));
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 4, "0x0000FFFF", "eq", "uint32"));
        }

        [TestMethod]
        public void ScanValueMatches_UInt64_Equal()
        {
            byte[] mem = BitConverter.GetBytes(0xDEADBEEFCAFEBABEul);
            Assert.IsTrue(SocketServer.ScanValueMatches(mem, 0, "0xDEADBEEFCAFEBABE", "eq", "uint64"));
        }

        // ── FormatScanValue ──────────────────────────────────────────────────

        [TestMethod]
        public void FormatScanValue_UInt32()
        {
            byte[] mem = { 0xFF, 0xFF, 0x00, 0x00 };
            Assert.AreEqual("65535", SocketServer.FormatScanValue(mem, 0, "uint32"));
        }

        [TestMethod]
        public void FormatScanValue_Int32_Negative()
        {
            byte[] mem = { 0xFF, 0xFF, 0xFF, 0xFF };
            Assert.AreEqual("-1", SocketServer.FormatScanValue(mem, 0, "int32"));
        }

        [TestMethod]
        public void FormatScanValue_Float32()
        {
            byte[] mem = BitConverter.GetBytes(1.5f);
            Assert.AreEqual("1.5", SocketServer.FormatScanValue(mem, 0, "float32"));
        }

        [TestMethod]
        public void FormatScanValue_UInt8()
        {
            byte[] mem = { 0xAB };
            Assert.AreEqual("171", SocketServer.FormatScanValue(mem, 0, "uint8"));
        }

        // ── ValidateScanValue ────────────────────────────────────────────────

        [TestMethod]
        public void ValidateScanValue_ValidHex()
        {
            Assert.IsNull(SocketServer.ValidateScanValue("0x0000FFFF", "uint32"));
        }

        [TestMethod]
        public void ValidateScanValue_ValidBareHex()
        {
            Assert.IsNull(SocketServer.ValidateScanValue("0000FFFF", "uint32"));
        }

        [TestMethod]
        public void ValidateScanValue_ValidDecimal()
        {
            Assert.IsNull(SocketServer.ValidateScanValue("65535", "uint32"));
        }

        [TestMethod]
        public void ValidateScanValue_ValidFloat()
        {
            Assert.IsNull(SocketServer.ValidateScanValue("3.14", "float32"));
        }

        [TestMethod]
        public void ValidateScanValue_InvalidHex()
        {
            var error = SocketServer.ValidateScanValue("GHIJ", "uint32");
            Assert.IsNotNull(error);
            Assert.IsTrue(error!.Contains("Cannot parse"));
        }

        [TestMethod]
        public void ValidateScanValue_OverflowUInt8()
        {
            var error = SocketServer.ValidateScanValue("0x1FF", "uint8");
            Assert.IsNotNull(error);
            Assert.IsTrue(error!.Contains("out of range"));
        }

        [TestMethod]
        public void ValidateScanValue_Empty()
        {
            var error = SocketServer.ValidateScanValue("", "uint32");
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void ValidateScanValue_NegativeForSigned()
        {
            Assert.IsNull(SocketServer.ValidateScanValue("-42", "int32"));
        }

        // ── GetScanTypeSize ──────────────────────────────────────────────────

        [TestMethod]
        public void GetScanTypeSize_Defaults()
        {
            Assert.AreEqual(4, SocketServer.GetScanTypeSize(null));
            Assert.AreEqual(4, SocketServer.GetScanTypeSize("uint32"));
            Assert.AreEqual(4, SocketServer.GetScanTypeSize("int32"));
            Assert.AreEqual(4, SocketServer.GetScanTypeSize("float32"));
        }

        [TestMethod]
        public void GetScanTypeSize_AllTypes()
        {
            Assert.AreEqual(1, SocketServer.GetScanTypeSize("uint8"));
            Assert.AreEqual(1, SocketServer.GetScanTypeSize("int8"));
            Assert.AreEqual(2, SocketServer.GetScanTypeSize("uint16"));
            Assert.AreEqual(2, SocketServer.GetScanTypeSize("int16"));
            Assert.AreEqual(8, SocketServer.GetScanTypeSize("uint64"));
            Assert.AreEqual(8, SocketServer.GetScanTypeSize("int64"));
            Assert.AreEqual(8, SocketServer.GetScanTypeSize("float64"));
        }
    }
}
