using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for <see cref="CdsCheatEngine"/> — the assembly-level persistent cheat engine
    /// that hooks into the game loop and continuously re-applies cheat values.
    /// The "Run_" tests execute the injected engine on the EE interpreter.
    /// </summary>
    [TestClass]
    public class TestCdsCheatEngine
    {
        private const uint Hook      = 0x00100000;
        private const uint TableBase = 0x000C0000 + CdsCheatEngine.CheatTableOffset;

        // ── Mock IByteEditor backed by a simple byte array ──────────────────

        private sealed class MockByteEditor : IByteEditor
        {
            private readonly byte[] _ram;
            public MockByteEditor(int size = 0x02000000) => _ram = new byte[size];

            public byte[] Read(int address, int byteLength)
            {
                var buf = new byte[byteLength];
                Array.Copy(_ram, address, buf, 0, byteLength);
                return buf;
            }

            public byte[] Read(string addressHex, int byteLength)
                => Read(Convert.ToInt32(addressHex, 16), byteLength);

            public void Write(int address, byte[] data)
                => Array.Copy(data, 0, _ram, address, data.Length);

            public void Write(string addressHex, byte[] data)
                => Write(Convert.ToInt32(addressHex, 16), data);

            public uint ReadWord(uint addr)
                => BitConverter.ToUInt32(_ram, (int)addr);

            public void WriteWord(uint addr, uint word)
                => BitConverter.GetBytes(word).CopyTo(_ram, (int)addr);
        }

        /// <summary>IByteEditor over the interpreter's main RAM.</summary>
        private sealed class EmulatorRamEditor : IByteEditor
        {
            private readonly PS2Emulator _emu;
            public EmulatorRamEditor(PS2Emulator emu) => _emu = emu;
            public byte[] Read(int address, int byteLength)
            {
                var buf = new byte[byteLength];
                for (int i = 0; i < byteLength; i++) buf[i] = _emu.Bus.ReadByte((uint)(address + i));
                return buf;
            }
            public byte[] Read(string addressHex, int byteLength) => Read(Convert.ToInt32(addressHex, 16), byteLength);
            public void Write(int address, byte[] data)
            {
                for (int i = 0; i < data.Length; i++) _emu.Bus.WriteByte((uint)(address + i), data[i]);
            }
            public void Write(string addressHex, byte[] data) => Write(Convert.ToInt32(addressHex, 16), data);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static MockByteEditor CreateEditor()
        {
            var editor = new MockByteEditor();
            // A recognisable "original instruction" pair at the hook point.
            editor.WriteWord(Hook, 0x24020001);     // addiu v0, zero, 1
            editor.WriteWord(Hook + 4, 0x00000000); // nop
            return editor;
        }

        private static EngineEntry Row(MockByteEditor editor, int index)
        {
            uint a = TableBase + (uint)(index * CdsCheatEngine.EntrySize);
            return new EngineEntry(editor.ReadWord(a), editor.ReadWord(a + 4), editor.ReadWord(a + 8), editor.ReadWord(a + 12));
        }

        /// <summary>
        /// Interpreter fixture: a "game loop" at <see cref="Hook"/> that counts its passes in
        /// t5 (addiu t5, t5, 1; nop; j Hook; nop). The interpreter has no TLB, so the engine is
        /// built with memory base 0 and code addresses hit RAM directly.
        /// </summary>
        private sealed class Machine
        {
            public readonly PS2Emulator Emu = new();
            public readonly CdsCheatEngine Engine;

            public Machine()
            {
                Emu.LoadProgram(Hook, [0x25AD0001u, 0x00000000u, CdsCheatEngine.EncodeJ(Hook), 0x00000000u]);
                Emu.Cpu.PC = Hook;
                Emu.Cpu.RegsLo[29] = 0x01F00000; // sp
                Engine = new CdsCheatEngine(new EmulatorRamEditor(Emu), 0x000C0000, memoryBase: 0);
            }

            public uint Passes => (uint)Emu.Cpu.RegsLo[13];

            /// <summary>Step until the loop has completed <paramref name="frames"/> more passes.</summary>
            public void RunFrames(uint frames)
            {
                uint target = Passes + frames;
                for (int guard = 0; Passes < target; guard++)
                {
                    if (guard > 2_000_000) Assert.Fail("Engine did not return to the game loop.");
                    Emu.Step();
                }
            }

            public uint Word(uint a) => Emu.Bus.ReadWord(a);
            public ushort Half(uint a) => (ushort)(Emu.Bus.ReadByte(a) | (Emu.Bus.ReadByte(a + 1) << 8));
            public byte Byte(uint a) => Emu.Bus.ReadByte(a);
            public void SetWord(uint a, uint v) => Emu.Bus.WriteWord(a, v);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Construction
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_SetsDefaults()
        {
            var engine = new CdsCheatEngine(new MockByteEditor());
            Assert.AreEqual(0x000C0000u, engine.EngineBase);
            Assert.IsFalse(engine.IsInjected);
        }

        [TestMethod]
        public void Constructor_CustomBase()
        {
            var engine = new CdsCheatEngine(new MockByteEditor(), 0x00200000);
            Assert.AreEqual(0x00200000u, engine.EngineBase);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullEditor_Throws()
        {
            _ = new CdsCheatEngine(null!);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Inject / Remove / table layout
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Inject_HooksTargetWithJumpAndNop()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(Hook, "#Infinite Health\n20123456 000003E8");

            Assert.IsTrue(engine.IsInjected);
            Assert.AreEqual(Hook, engine.HookAddress);
            Assert.AreEqual(CdsCheatEngine.EncodeJ(0x000C0000), editor.ReadWord(Hook));
            Assert.AreEqual(0u, editor.ReadWord(Hook + 4));
        }

        [TestMethod]
        public void Inject_WritesTableRowAndEndRow()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(Hook, "20123456 000003E8");

            Assert.AreEqual(new EngineEntry(EngineOp.Write32, 0x20123456, 0x3E8, 0), Row(editor, 0));
            Assert.AreEqual(new EngineEntry(0, 0, 0, 0), Row(editor, 1));
        }

        [TestMethod]
        public void Inject_EmbedsOriginalInstructionsAndReturnJump()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);
            engine.Inject(Hook, "20123456 000003E8");

            // The engine code must contain: original pair, then `j hook+8`.
            bool found = false;
            for (uint a = 0x000C0000; a < 0x000C0000 + CdsCheatEngine.DataOffset; a += 4)
            {
                if (editor.ReadWord(a) == 0x24020001 && editor.ReadWord(a + 8) == CdsCheatEngine.EncodeJ(Hook + 8))
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "original instructions + return jump not found inside the engine code area");
        }

        [TestMethod]
        public void Remove_RestoresOriginalInstructions()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(Hook, "20123456 000003E8");
            engine.Remove();

            Assert.IsFalse(engine.IsInjected);
            Assert.AreEqual(0x24020001u, editor.ReadWord(Hook));
            Assert.AreEqual(0u, editor.ReadWord(Hook + 4));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Inject_WhenAlreadyInjected_Throws()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);
            engine.Inject(Hook, "20123456 000003E8");
            engine.Inject(Hook, "20123456 000003E8");
        }

        [TestMethod]
        public void Remove_WhenNotInjected_IsNoop()
        {
            new CdsCheatEngine(new MockByteEditor()).Remove();
        }

        [TestMethod]
        public void InjectRemoveReinject_WorksCorrectly()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(Hook, "20123456 000003E8");
            engine.Remove();
            engine.Inject(Hook, "20AABBCC 0000FFFF");

            Assert.IsTrue(engine.IsInjected);
            Assert.AreEqual(0x20AABBCCu, Row(editor, 0).Address);
        }

        [TestMethod]
        public void UpdateCheatTable_ReplacesRows()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(Hook, "20123456 000003E8\n20123460 00000001");
            engine.UpdateCheatTable("10AABBCC 0000FFFF");

            Assert.AreEqual(new EngineEntry(EngineOp.Write16, 0x20AABBCC, 0xFFFF, 0), Row(editor, 0));
            Assert.AreEqual(0u, Row(editor, 1).Control);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdateCheatTable_WhenNotInjected_Throws()
        {
            new CdsCheatEngine(new MockByteEditor()).UpdateCheatTable("20123456 000003E8");
        }

        [TestMethod]
        public void GenerateSource_ContainsEngineAndRows()
        {
            string source = new CdsCheatEngine(new MockByteEditor()).GenerateSource("20123456 000003E8");
            StringAssert.Contains(source, "scope CheatEngine");
            StringAssert.Contains(source, "hexcode $20123456");
            StringAssert.Contains(source, "hexcode $000003E8");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Hook validation
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Inject_BranchAtHook_Throws()
        {
            var editor = CreateEditor();
            editor.WriteWord(Hook, 0x1040FFFF); // beq v0, zero, -1
            var engine = new CdsCheatEngine(editor);
            Assert.ThrowsException<InvalidOperationException>(() => engine.Inject(Hook, "20123456 000003E8"));
            Assert.IsFalse(engine.IsInjected);
        }

        [TestMethod]
        public void Inject_JrRaHook_IsAllowed()
        {
            var editor = CreateEditor();
            editor.WriteWord(Hook, 0x03E00008); // jr ra
            editor.WriteWord(Hook + 4, 0x27BD0010); // addiu sp, sp, 16 (delay slot)
            var engine = new CdsCheatEngine(editor);
            engine.Inject(Hook, "20123456 000003E8");
            Assert.IsTrue(engine.IsInjected);
        }

        [TestMethod]
        public void Inject_UnsupportedCode_ThrowsWithLine()
        {
            var engine = new CdsCheatEngine(CreateEditor());
            var ex = Assert.ThrowsException<InvalidOperationException>(() => engine.Inject(Hook, "50123456 00000010\n00200000 00000000"));
            StringAssert.Contains(ex.Message, "line 1");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Translator
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Translate_ConditionalSkipCountsCoverWholeCodes()
        {
            // D guards the next line; E guards 2 lines, one of which is a 2-line serial code.
            var t = RawCodeTranslator.Translate(
                "D0100000 00000001\n" +
                "20100004 00000002\n" +
                "E0020005 00100008\n" +
                "4010000C 00020001\n" +
                "00000001 00000000\n" +
                "00100010 000000FF", 0);

            Assert.IsTrue(t.IsValid, string.Join("; ", t.Errors));
            Assert.AreEqual(5, t.Entries.Count);
            Assert.AreEqual(1u, t.Entries[0].Control >> 16, "D skips 1 row");
            Assert.AreEqual(1u, t.Entries[2].Control >> 16, "E covering the 2 serial lines skips its 1 row");
        }

        [TestMethod]
        public void Translate_MasterCodesAreIgnoredWithWarning()
        {
            var t = RawCodeTranslator.Translate("F0100008 001003E8\n20100000 00000001", 0);
            Assert.IsTrue(t.IsValid);
            Assert.AreEqual(1, t.Entries.Count);
            Assert.AreEqual(1, t.Warnings.Count);
        }

        [TestMethod]
        public void BuildImage_CodeFitsBeforeDataArea()
        {
            var image = CdsCheatEngine.BuildImage(new List<EngineEntry>(), 0x000C0000, Hook, 0x24020001, 0);
            // Find the return jump: it must sit inside the code area.
            int at = -1;
            for (int o = 0; o + 4 <= CdsCheatEngine.DataOffset; o += 4)
                if (BitConverter.ToUInt32(image, o) == CdsCheatEngine.EncodeJ(Hook + 8)) { at = o; break; }
            Assert.IsTrue(at > 0, "return jump not found before the data area");
            Assert.AreEqual(CdsCheatEngine.CheatTableOffset + CdsCheatEngine.EntrySize, image.Length);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Execution on the EE interpreter
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Run_ConstantWrites_HoldAgainstTheGame()
        {
            var m = new Machine();
            m.Engine.Inject(Hook, "20200000 DEADBEEF\n10200010 00001234\n00200020 000000AB");

            m.RunFrames(2);
            Assert.AreEqual(0xDEADBEEFu, m.Word(0x00200000));
            Assert.AreEqual((ushort)0x1234, m.Half(0x00200010));
            Assert.AreEqual((byte)0xAB, m.Byte(0x00200020));

            // The "game" overwrites the value; the next frame puts it back.
            m.SetWord(0x00200000, 0);
            m.RunFrames(1);
            Assert.AreEqual(0xDEADBEEFu, m.Word(0x00200000));
        }

        [TestMethod]
        public void Run_ReplaysHookedInstructionsAndPreservesRegisters()
        {
            var m = new Machine();
            var cpu = m.Emu.Cpu;
            // Registers the engine uses internally, with distinctive 128-bit contents.
            int[] regs = [1, 2, 3, 8, 9, 10, 11, 12, 16];
            foreach (int r in regs)
            {
                cpu.RegsLo[r] = 0x1111_0000_0000_0000UL | (ulong)r;
                cpu.RegsHi[r] = 0x2222_0000_0000_0000UL | (ulong)r;
            }
            cpu.HI = 0x0123_4567_89AB_CDEFUL;
            cpu.LO = 0x0FED_CBA9_8765_4321UL;
            m.Engine.Inject(Hook, "B0000001 00000002\n20200000 00000001");

            m.RunFrames(5);

            Assert.AreEqual(5u, m.Passes, "addiu t5 at the hook point must still run every pass");
            foreach (int r in regs)
            {
                Assert.AreEqual(0x1111_0000_0000_0000UL | (ulong)r, cpu.RegsLo[r], $"r{r} low");
                Assert.AreEqual(0x2222_0000_0000_0000UL | (ulong)r, cpu.RegsHi[r], $"r{r} high");
            }
            Assert.AreEqual(0x0123_4567_89AB_CDEFUL, cpu.HI);
            Assert.AreEqual(0x0FED_CBA9_8765_4321UL, cpu.LO);
            Assert.AreEqual(0x01F00000UL, cpu.RegsLo[29], "sp");
        }

        [TestMethod]
        public void Run_IncrementAndDecrement()
        {
            var m = new Machine();
            m.SetWord(0x00200000, 100);
            m.SetWord(0x00200010, 100);
            m.Engine.Inject(Hook, "30000005 00200000\n30500000 00200010\n00000003 00000000");

            m.RunFrames(4);
            Assert.AreEqual((byte)(100 + 4 * 5), m.Byte(0x00200000));
            Assert.AreEqual(100u - 4 * 3, m.Word(0x00200010));
        }

        [TestMethod]
        public void Run_SerialWrite()
        {
            var m = new Machine();
            m.Engine.Inject(Hook, "40200000 00030002\n00000010 00000001");
            m.RunFrames(1);
            Assert.AreEqual(0x10u, m.Word(0x00200000));
            Assert.AreEqual(0x11u, m.Word(0x00200008));
            Assert.AreEqual(0x12u, m.Word(0x00200010));
            Assert.AreEqual(0u, m.Word(0x00200018));
        }

        [TestMethod]
        public void Run_ConditionalsGateTheirLines()
        {
            var m = new Machine();
            m.SetWord(0x00200100, 0x0001);
            m.Engine.Inject(Hook,
                "Met\nD0200100 00000001\n20200000 00000011\n\n" +
                "Not met\nD0200100 00000002\n20200004 00000022\n\n" +
                "Multi\nE0020000 00200102\n20200008 00000033\n2020000C 00000044\n20200010 00000055\n\n" +
                "Stop\nC0200100 00000005\n20200014 00000066\n20200018 00000077");

            m.RunFrames(1);
            Assert.AreEqual(0x11u, m.Word(0x00200000), "D equal → next line runs");
            Assert.AreEqual(0u,    m.Word(0x00200004), "D not equal → next line skipped");
            Assert.AreEqual(0x33u, m.Word(0x00200008), "E (high half == 0) → its 2 lines run");
            Assert.AreEqual(0x44u, m.Word(0x0020000C));
            Assert.AreEqual(0x55u, m.Word(0x00200010), "line after the E block always runs");
            Assert.AreEqual(0u,    m.Word(0x00200014), "C false → rest of the cheat skipped");
            Assert.AreEqual(0u,    m.Word(0x00200018));
        }

        [TestMethod]
        public void Run_TimerRunsEveryNthFrame()
        {
            var m = new Machine();
            m.SetWord(0x00200000, 0);
            m.Engine.Inject(Hook, "B0000001 00000003\n30400000 00200000\n00000001 00000000");
            m.RunFrames(9);
            Assert.AreEqual(3u, m.Word(0x00200000), "increment guarded by a 3-frame timer runs 3 times in 9 frames");
        }

        [TestMethod]
        public void Run_CdsSource_FreezeCondAndAssembly()
        {
            var m = new Machine();
            m.SetWord(0x00200100, 0x00010002);
            m.Engine.InjectFromCds(Hook, @"
// raw code, freeze and an assembled word in one source
20200000 00000001
freeze half $00200010 = 0BAD
cond mem[$00200100] == 00010002
  freeze word $00200020 = CAFEF00D
endcond
cond mem[$00200100] == 00010003
  freeze word $00200024 = BAADF00D
endcond
address $00200030
  addiu v0, zero, 1
");
            m.RunFrames(1);
            Assert.AreEqual(1u, m.Word(0x00200000));
            Assert.AreEqual((ushort)0x0BAD, m.Half(0x00200010));
            Assert.AreEqual(0xCAFEF00Du, m.Word(0x00200020), "32-bit cond met");
            Assert.AreEqual(0u, m.Word(0x00200024), "32-bit cond not met (high half differs)");
            Assert.AreEqual(0x24020001u, m.Word(0x00200030), "assembled instruction written");
        }

        [TestMethod]
        public void Run_RemoveStopsTheEngine()
        {
            var m = new Machine();
            m.Engine.Inject(Hook, "20200000 00000001");
            m.RunFrames(1);
            m.Engine.Remove();
            m.SetWord(0x00200000, 0);
            m.RunFrames(2);
            Assert.AreEqual(0u, m.Word(0x00200000));
        }
    }
}
