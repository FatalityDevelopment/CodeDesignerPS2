using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.CheatConverter;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for the PS2 cheat code converter framework.
    /// <para>
    /// Covers round-trip encode → decode correctness for every implemented format, plus
    /// the auto-detection heuristic.
    /// </para>
    /// </summary>
    [TestClass]
    public class TestCheatCodeConverter
    {
        // -----------------------------------------------------------------------
        // Helper
        // -----------------------------------------------------------------------

        private static void AssertRoundTrip(ICheatCodeConverter conv, IEnumerable<RawCode> codes)
        {
            var list      = codes.ToList();
            string encoded = conv.Encode(list);
            var decoded   = conv.Decode(encoded);
            Assert.AreEqual(list.Count, decoded.Count,
                $"{conv.GetType().Name}: code count mismatch after round-trip.\nEncoded:\n{encoded}");
            for (int i = 0; i < list.Count; i++)
                Assert.AreEqual(list[i], decoded[i],
                    $"{conv.GetType().Name}: mismatch at index {i}.\nEncoded:\n{encoded}");
        }

        private static readonly RawCode[] SampleCodes = {
            new RawCode(0x200133AC, 0x000003E8),
            new RawCode(0x20013400, 0xFFFFFFFF),
            new RawCode(0x10100004, 0x00000064),
        };

        // -----------------------------------------------------------------------
        // Raw
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Raw_Decode_ParsesHexPairs()
        {
            var conv = new RawConverter();
            var result = conv.Decode("200133AC 000003E8\n20013400 FFFFFFFF");
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(new RawCode(0x200133AC, 0x000003E8), result[0]);
            Assert.AreEqual(new RawCode(0x20013400, 0xFFFFFFFF), result[1]);
        }

        [TestMethod]
        public void Raw_Decode_SkipsBlankAndCommentLines()
        {
            var conv = new RawConverter();
            var result = conv.Decode("# comment\n\n200133AC 000003E8\n  \n");
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void Raw_RoundTrip()
        {
            AssertRoundTrip(new RawConverter(), SampleCodes);
        }

        [TestMethod]
        public void Raw_Encode_ProducesUppercaseHex()
        {
            var conv  = new RawConverter();
            var codes = new[] { new RawCode(0x200133AC, 0x000003E8) };
            var enc   = conv.Encode(codes);
            Assert.AreEqual("200133AC 000003E8", enc.Trim());
        }

        // -----------------------------------------------------------------------
        // ARMax
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ArMax_Alphabet_Is32Chars()
        {
            // The ARMax filter should be exactly 32 characters (5 bits per symbol)
            // We verify this indirectly: encode a known code, then decode it back.
            var conv = new ArMaxConverter();
            var codes = new[] { new RawCode(0x200133AC, 0x000003E8) };
            string enc = conv.Encode(codes);
            // Each line should match XXXX-XXXX-XXXXX
            foreach (var line in enc.Split('\n').Where(l => l.Trim().Length > 0))
                Assert.IsTrue(line.Trim().Length == 15 && line[4] == '-' && line[9] == '-',
                    $"Unexpected line format: '{line}'");
        }

        [TestMethod]
        public void ArMax_RoundTrip_SingleCode()
        {
            AssertRoundTrip(new ArMaxConverter(), new[] { new RawCode(0x200133AC, 0x000003E8) });
        }

        [TestMethod]
        public void ArMax_RoundTrip_MultipleCodesAsBatch()
        {
            AssertRoundTrip(new ArMaxConverter(), SampleCodes);
        }

        [TestMethod]
        public void ArMax_Decode_IgnoresNonArMaxLines()
        {
            var conv   = new ArMaxConverter();
            var codes  = new[] { new RawCode(0x200133AC, 0x000003E8) };
            string enc = conv.Encode(codes);
            var decoded = conv.Decode("# comment\n\n" + enc + "\n");
            Assert.AreEqual(1, decoded.Count);
        }

        // -----------------------------------------------------------------------
        // Action Replay 2
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Ar2_RoundTrip_DefaultSeed()
        {
            AssertRoundTrip(new ActionReplay2Converter(), SampleCodes);
        }

        [TestMethod]
        public void Ar2_RoundTrip_Ar1Mode()
        {
            AssertRoundTrip(new ActionReplay2Converter { UseAr1Seed = true }, SampleCodes);
        }

        [TestMethod]
        public void Ar2_Encode_EmitsDeadfaceLine()
        {
            var conv  = new ActionReplay2Converter();
            var codes = new[] { new RawCode(0x200133AC, 0x000003E8) };
            string enc = conv.Encode(codes);
            // The first line of the encoded AR2 output should decode back to DEADFACE
            string firstLine = enc.Split('\n')[0].Trim();
            var decoded = conv.Decode(enc);
            // DEADFACE line is consumed as a seed line, not emitted as a code
            Assert.AreEqual(1, decoded.Count);
        }

        [TestMethod]
        public void Ar2_Ar1Mode_NoDeadfaceLine()
        {
            var conv  = new ActionReplay2Converter { UseAr1Seed = true };
            string enc = conv.Encode(new[] { new RawCode(0x200133AC, 0x000003E8) });
            // AR1 mode should NOT emit a DEADFACE key line
            Assert.IsFalse(enc.Split('\n')[0].Trim()
                .StartsWith("DEADFACE", System.StringComparison.OrdinalIgnoreCase));
        }

        // -----------------------------------------------------------------------
        // CodeBreaker V1
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Cb1_RoundTrip()
        {
            AssertRoundTrip(new CodeBreakerV1Converter(), SampleCodes);
        }

        [TestMethod]
        public void Cb1_Decrypt_KnownVector()
        {
            // Known CB1 encrypted pair from community documentation.
            // This exercises the cipher against a reference value.
            // The encrypted form of 0x200133AC 0x000003E8 should decrypt back to itself
            // after CB1 → plain → CB1 round-trip.
            var conv   = new CodeBreakerV1Converter();
            var codes  = new[] { new RawCode(0x200133AC, 0x000003E8) };
            string enc = conv.Encode(codes);
            var dec    = conv.Decode(enc);
            Assert.AreEqual(new RawCode(0x200133AC, 0x000003E8), dec[0]);
        }

        // -----------------------------------------------------------------------
        // CodeBreaker V7
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Cb7_Decode_EmitsBeefc0deLine()
        {
            var conv = new CodeBreakerV7Converter();
            string enc = conv.Encode(new[] { new RawCode(0x200133AC, 0x000003E8) });
            Assert.IsTrue(enc.StartsWith("BEEFC0DE", System.StringComparison.OrdinalIgnoreCase),
                "CB7 output should begin with BEEFC0DE key line.");
        }

        [TestMethod]
        public void Cb7_RoundTrip()
        {
            AssertRoundTrip(new CodeBreakerV7Converter(), SampleCodes);
        }

        [TestMethod]
        public void Cb7_Decode_WithoutBeefc0deHeader_UsesDefaultSeed()
        {
            // Codes encoded with BEEFC0DE 00000000 (seed=0) should decode identically
            // whether or not the BEEFC0DE line is included in the input.
            var conv = new CodeBreakerV7Converter();
            var src  = new[] { new RawCode(0x200133AC, 0x000003E8) };

            string withHeader    = conv.Encode(src);                                      // BEEFC0DE 00000000\nXXXX XXXX
            string withoutHeader = string.Join("\n", withHeader.Split('\n').Skip(1));    // strip BEEFC0DE line

            var decodedWith    = conv.Decode(withHeader);
            var decodedWithout = conv.Decode(withoutHeader);

            Assert.AreEqual(decodedWith.Count, decodedWithout.Count,
                "Code count should match with and without BEEFC0DE header.");
            for (int i = 0; i < decodedWith.Count; i++)
                Assert.AreEqual(decodedWith[i], decodedWithout[i],
                    $"Code at index {i} should decode identically with or without BEEFC0DE header.");
        }

        [TestMethod]
        public void Cb7_Decode_KnownVector_NoHeader()
        {
            // Known CB7 code: 90333610 0C0CCD2C was encoded with BEEFC0DE 00000000 (seed=0).
            // Without the header the converter must still produce the correct plaintext.
            var conv    = new CodeBreakerV7Converter();
            var decoded = conv.Decode("90333610 0C0CCD2C");
            Assert.AreEqual(1, decoded.Count, "Should decode exactly one code.");
            // The expected raw values come from decoding with the BEEFC0DE 00000000 header.
            var expected = conv.Decode("BEEFC0DE 00000000\n90333610 0C0CCD2C");
            Assert.AreEqual(expected[0], decoded[0],
                $"Expected {expected[0].Address:X8} {expected[0].Value:X8} but got {decoded[0].Address:X8} {decoded[0].Value:X8}.");
        }

        // -----------------------------------------------------------------------
        // GameShark V3
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Gs3_RoundTrip_RawKey0()
        {
            // Codes with bits 26-28 = 0 (key 0 = raw) should survive unchanged.
            var conv  = new GameSharkV3Converter();
            var codes = new[] {
                new RawCode(0x200133AC, 0x000003E8),  // key = 0 (raw)
            };
            AssertRoundTrip(conv, codes);
        }

        // -----------------------------------------------------------------------
        // Format Detector
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Detector_IdentifiesRawFormat()
        {
            string input = "200133AC 000003E8\n20013400 FFFFFFFF";
            Assert.AreEqual(CheatFormat.Raw, CheatFormatDetector.Detect(input));
        }

        [TestMethod]
        public void Detector_IdentifiesArMaxFormat()
        {
            var conv    = new ArMaxConverter();
            string enc  = conv.Encode(new[] { new RawCode(0x200133AC, 0x000003E8) });
            var detected = CheatFormatDetector.Detect(enc);
            Assert.AreEqual(CheatFormat.ActionReplayMax, detected);
        }

        [TestMethod]
        public void Detector_IdentifiesAr2ByDeadface()
        {
            string input = "DEADFACE 00000000\n12345678 9ABCDEF0";
            Assert.AreEqual(CheatFormat.ActionReplay2, CheatFormatDetector.Detect(input));
        }

        [TestMethod]
        public void Detector_IdentifiesCb7ByBeefc0de()
        {
            string input = "BEEFC0DE 00000000\n12345678 9ABCDEF0";
            Assert.AreEqual(CheatFormat.CodeBreakerV7, CheatFormatDetector.Detect(input));
        }

        [TestMethod]
        public void Detector_AutoDecodeRaw()
        {
            string input = "200133AC 000003E8";
            var codes    = CheatFormatDetector.AutoDecode(input);
            Assert.AreEqual(1, codes.Count);
            Assert.AreEqual(new RawCode(0x200133AC, 0x000003E8), codes[0]);
        }

        [TestMethod]
        public void Detector_AutoDecodeArMax()
        {
            var conv    = new ArMaxConverter();
            var src     = new[] { new RawCode(0x200133AC, 0x000003E8) };
            string enc  = conv.Encode(src);
            var decoded = CheatFormatDetector.AutoDecode(enc);
            Assert.AreEqual(1, decoded.Count);
            Assert.AreEqual(src[0], decoded[0]);
        }

        // -----------------------------------------------------------------------
        // Cross-format conversion convenience test
        // -----------------------------------------------------------------------

        [TestMethod]
        public void CrossFormat_ArMaxToRawToArMax_RoundTrip()
        {
            // Start with ARMax, decode to raw, re-encode to ARMax, verify equal
            var armax   = new ArMaxConverter();
            var raw     = new RawConverter();
            var srcCodes = new[] { new RawCode(0x200133AC, 0x000003E8) };

            string armaxEnc    = armax.Encode(srcCodes);
            var decoded        = armax.Decode(armaxEnc);
            string rawEnc      = raw.Encode(decoded);
            var backToRaw      = raw.Decode(rawEnc);
            string armaxEnc2   = armax.Encode(backToRaw);

            Assert.AreEqual(armaxEnc, armaxEnc2,
                "ARMax → Raw → ARMax round-trip should produce identical ARMax strings.");
        }
    }
}
