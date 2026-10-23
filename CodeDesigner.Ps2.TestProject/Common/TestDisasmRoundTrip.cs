using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Text the disassembler prints must assemble back to an instruction that prints
    /// the same, so text copied out of the disassembler is safe to paste into a CDS
    /// script or the edit overlay. (Compared as text: a word with bits the CPU
    /// ignores — e.g. `sll` with a non-zero rs field — re-assembles to the canonical
    /// encoding of the same instruction.)
    /// </summary>
    [TestClass]
    public class TestDisasmRoundTrip
    {
        /// <summary>
        /// Field patterns per opcode/funct pair: register fields varied so both
        /// "special" values (0, 31) and ordinary ones are covered, plus shamt.
        /// </summary>
        private static IEnumerable<uint> Candidates()
        {
            (int rs, int rt, int rd, int sa)[] fields =
            [
                (0, 0, 0, 0), (4, 5, 6, 0), (31, 31, 31, 31), (2, 3, 8, 7), (16, 0, 9, 16), (0, 17, 18, 1),
            ];
            for (uint op = 0; op < 64; op++)
            for (uint funct = 0; funct < 64; funct++)
            foreach (var (rs, rt, rd, sa) in fields)
            {
                yield return (op << 26) | ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) | ((uint)sa << 6) | funct;
            }

            // COP0/1/2 sub-ops live in rs; MMI sub-groups in sa; REGIMM in rt.
            for (uint op = 0x10; op <= 0x12; op++)
            for (uint rs = 0; rs < 32; rs++)
            for (uint funct = 0; funct < 64; funct++)
            foreach (uint rt in new uint[] { 0, 1, 2, 3, 8 })
                yield return (op << 26) | (rs << 21) | (rt << 16) | (9u << 11) | (5u << 6) | funct;

            foreach (uint funct in new uint[] { 0x08, 0x09, 0x28, 0x29 })
            for (uint sa = 0; sa < 32; sa++)
                yield return (0x1Cu << 26) | (4u << 21) | (5u << 16) | (6u << 11) | (sa << 6) | funct;

            for (uint rt = 0; rt < 32; rt++)
                yield return (0x01u << 26) | (4u << 21) | (rt << 16) | 0x0010u;
        }

        [TestMethod]
        public void EveryDecodedWord_ReassemblesToItself()
        {
            var failures = new List<string>();
            var seen = new HashSet<uint>();
            int checkedCount = 0;

            foreach (uint word in Candidates())
            {
                if (!seen.Add(word) || word == 0) continue;
                string text = MipsDisassembler.Disassemble(word);
                if (text.StartsWith(".word", StringComparison.Ordinal)) continue;
                checkedCount++;

                if (!MipsAssembler.TryAssemble(text, out uint back, out string? error))
                {
                    failures.Add($"0x{word:X8} '{text}' does not assemble: {error}");
                    continue;
                }
                string again = MipsDisassembler.Disassemble(back);
                if (again != text)
                    failures.Add($"0x{word:X8} '{text}' assembles to 0x{back:X8} '{again}'");
            }

            Assert.IsTrue(checkedCount > 1000, $"only {checkedCount} encodings were checked");
            if (failures.Count > 0)
            {
                Assert.Fail($"{failures.Count} of {checkedCount} encodings do not round-trip:\n"
                    + string.Join("\n", failures.Take(60)));
            }
        }
    }
}
