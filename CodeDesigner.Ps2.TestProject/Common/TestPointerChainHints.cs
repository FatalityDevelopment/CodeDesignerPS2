using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Bridge.Services;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Pointer-chain hints ("ptr -> ptr -> \"string\"") in the shape the bridge runs the
    /// analyzer: hints read live memory, simulated loads read the page first.
    /// </summary>
    [TestClass]
    public class TestPointerChainHints
    {
        private const uint Code = 0x00100000;

        /// <summary>0x300010 -> 0x300100 -> 0x300200 -> 0x300300 "PlayerClass"; 0x300020 -> "Weapon".</summary>
        private static PS2MemoryBus Memory()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00300010, 0x00300100);
            bus.WriteWord(0x00300100, 0x00300200);
            bus.WriteWord(0x00300200, 0x00300300);
            bus.WriteWord(0x00300020, 0x00300400);
            void Str(uint at, string text)
            {
                byte[] s = System.Text.Encoding.ASCII.GetBytes(text + "\0");
                for (int i = 0; i < s.Length; i++) bus.WriteByte(at + (uint)i, s[i]);
            }
            Str(0x00300300, "PlayerClass");
            Str(0x00300400, "Weapon");
            return bus;
        }

        private static List<(uint, uint)> Rows(params string[] lines)
        {
            var rows = new List<(uint, uint)>();
            for (int i = 0; i < lines.Length; i++) rows.Add((MipsAssembler.Assemble(lines[i]), Code + (uint)(i * 4)));
            return rows;
        }

        /// <summary>The bridge's load bus: only the (empty) page, throwing outside it.</summary>
        private static PageBytesMemoryBus PageOnly() => new(new byte[16], Code, null);

        [TestMethod]
        public void Chain_ShownOnLoadAndFollowedThroughLoadedRegister()
        {
            var rows = Rows("lui $v0, 0x0030", "lw $a0, 0x10($v0)", "lw $a1, 0($a0)");
            var c = MipsAnalyzer.AnalyzeInstructions(rows, bus: Memory(), loadBus: PageOnly());

            string load1 = c.GetValueOrDefault(Code + 4, "");
            StringAssert.Contains(load1, "= 0x00300100", "the loaded pointer value");
            StringAssert.Contains(load1, "\"PlayerClass\"", "the chain to the string");
            // a0 came from memory outside the page; the next hop must still resolve.
            StringAssert.Contains(c.GetValueOrDefault(Code + 8, ""), "[0x00300100]");
            StringAssert.Contains(c.GetValueOrDefault(Code + 8, ""), "\"PlayerClass\"");
        }

        [TestMethod]
        public void Paused_LongLivedRegistersResolveBeforeThePc()
        {
            var seed = new CpuState();
            seed.RegsLo[16] = 0x00300010;   // s0: object pointer
            seed.RegsLo[28] = 0x00300000;   // gp
            var rows = Rows("lw $a0, 0($s0)", "lw $a1, 0x20($gp)", "nop");

            var c = MipsAnalyzer.AnalyzeInstructions(rows, bus: Memory(), loadBus: PageOnly(),
                seedCpu: seed, seedPc: Code + 0x100, includeStateHints: true, seedContext: _ => true);

            StringAssert.Contains(c.GetValueOrDefault(Code, ""), "\"PlayerClass\"", "s0 from the snapshot");
            StringAssert.Contains(c.GetValueOrDefault(Code + 4, ""), "\"Weapon\"", "gp from the snapshot");
        }

        [TestMethod]
        public void Paused_OutsideThePcFunction_OnlyGpCarries()
        {
            var seed = new CpuState();
            seed.RegsLo[16] = 0x00300010;
            seed.RegsLo[28] = 0x00300000;
            var rows = Rows("lw $a0, 0($s0)", "lw $a1, 0x20($gp)");

            var c = MipsAnalyzer.AnalyzeInstructions(rows, bus: Memory(), loadBus: PageOnly(),
                seedCpu: seed, seedPc: Code + 0x100, seedContext: _ => false);

            Assert.IsFalse(c.GetValueOrDefault(Code, "").Contains("PlayerClass"), "s0 belongs to another function");
            StringAssert.Contains(c.GetValueOrDefault(Code + 4, ""), "\"Weapon\"");
        }
    }
}
