using CodeDesigner.Ps2.CheatEngine;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for <see cref="CdsCheatEngine"/> — the assembly-level persistent cheat engine
    /// that hooks into the game loop and continuously re-applies cheat values.
    /// </summary>
    [TestClass]
    public class TestCdsCheatEngine
    {
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

        // ── Helpers ─────────────────────────────────────────────────────────

        private static MockByteEditor CreateEditor()
        {
            var editor = new MockByteEditor();
            // Place a recognisable "original instruction" at the hook point.
            // e.g. addiu v0, zero, 1  →  0x24020001
            editor.WriteWord(0x00100000, 0x24020001);
            // And a second instruction at hook+4
            editor.WriteWord(0x00100004, 0x00000000); // nop
            return editor;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Construction
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_SetsDefaults()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor);
            Assert.AreEqual(0x000C0000u, engine.EngineBase);
            Assert.IsFalse(engine.IsInjected);
        }

        [TestMethod]
        public void Constructor_CustomBase()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor, 0x00200000);
            Assert.AreEqual(0x00200000u, engine.EngineBase);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullEditor_Throws()
        {
            _ = new CdsCheatEngine(null!);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Inject / Remove
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Inject_WritesEngineCode_AndHooksTarget()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            string cheats = "#Infinite Health\n20123456 000003E8";
            engine.Inject(0x00100000, cheats);

            Assert.IsTrue(engine.IsInjected);
            Assert.AreEqual(0x00100000u, engine.HookAddress);

            // The hook address should now contain a J instruction to the engine base.
            uint hookInstr = editor.ReadWord(0x00100000);
            uint expectedJ = 0x08000000 | ((0x000C0000 >> 2) & 0x03FFFFFF);
            Assert.AreEqual(expectedJ, hookInstr, $"Expected J 0x000C0000 (0x{expectedJ:X8}), got 0x{hookInstr:X8}");

            // Delay slot should be NOP
            uint delaySlot = editor.ReadWord(0x00100004);
            Assert.AreEqual(0x00000000u, delaySlot);
        }

        [TestMethod]
        public void Inject_WritesCheatTable()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            // 32-bit write of 0x000003E8 to address 0x123456
            string cheats = "20123456 000003E8";
            engine.Inject(0x00100000, cheats);

            // Cheat table is at engine base + 0x0300
            uint tableBase = 0x000C0000 + 0x0300;
            uint entry0Word0 = editor.ReadWord(tableBase);
            uint entry0Word1 = editor.ReadWord(tableBase + 4);

            // Word0 should be type(0x20) << 24 | address(0x123456)
            Assert.AreEqual(0x20123456u, entry0Word0,
                $"Expected 0x20123456, got 0x{entry0Word0:X8}");
            Assert.AreEqual(0x000003E8u, entry0Word1,
                $"Expected 0x000003E8, got 0x{entry0Word1:X8}");

            // End marker after the entry
            uint endWord0 = editor.ReadWord(tableBase + 8);
            uint endWord1 = editor.ReadWord(tableBase + 12);
            Assert.AreEqual(0xFFFFFFFFu, endWord0);
            Assert.AreEqual(0xFFFFFFFFu, endWord1);
        }

        [TestMethod]
        public void Remove_RestoresOriginalInstructions()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            uint origInstr = editor.ReadWord(0x00100000);
            engine.Inject(0x00100000, "20123456 000003E8");
            engine.Remove();

            Assert.IsFalse(engine.IsInjected);
            // Original instruction should be restored
            uint restored = editor.ReadWord(0x00100000);
            Assert.AreEqual(origInstr, restored,
                $"Expected original instruction 0x{origInstr:X8}, got 0x{restored:X8}");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Inject_WhenAlreadyInjected_Throws()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(0x00100000, "20123456 000003E8");
            engine.Inject(0x00100000, "20123456 000003E8"); // should throw
        }

        [TestMethod]
        public void Remove_WhenNotInjected_IsNoop()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor);
            engine.Remove(); // should not throw
        }

        // ────────────────────────────────────────────────────────────────────
        //  UpdateCheatTable
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void UpdateCheatTable_ChangesValues()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(0x00100000, "20123456 000003E8");

            // Update with different cheat
            engine.UpdateCheatTable("20AABBCC 0000FFFF");

            uint tableBase = 0x000C0000 + 0x0300;
            uint entry0Word0 = editor.ReadWord(tableBase);
            uint entry0Word1 = editor.ReadWord(tableBase + 4);

            Assert.AreEqual(0x20AABBCCu, entry0Word0);
            Assert.AreEqual(0x0000FFFFu, entry0Word1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdateCheatTable_WhenNotInjected_Throws()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor);
            engine.UpdateCheatTable("20123456 000003E8");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Multiple cheat entries
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Inject_MultipleEntries_AllPresent()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            string cheats = "20123456 000003E8\n20ABCDEF 0000002A";
            engine.Inject(0x00100000, cheats);

            uint tableBase = 0x000C0000 + 0x0300;

            // First entry
            Assert.AreEqual(0x20123456u, editor.ReadWord(tableBase));
            Assert.AreEqual(0x000003E8u, editor.ReadWord(tableBase + 4));

            // Second entry
            Assert.AreEqual(0x20ABCDEFu, editor.ReadWord(tableBase + 8));
            Assert.AreEqual(0x0000002Au, editor.ReadWord(tableBase + 12));

            // End marker
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 16));
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 20));
        }

        // ────────────────────────────────────────────────────────────────────
        //  GenerateSource
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void GenerateSource_ReturnsValidCds()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor);

            string source = engine.GenerateSource("20123456 000003E8");
            Assert.IsNotNull(source);

            // Should contain engine code and the cheat entry
            StringAssert.Contains(source, "scope CheatEngine");
            StringAssert.Contains(source, "20123456");
            StringAssert.Contains(source, "000003E8");
            // Should contain end marker
            StringAssert.Contains(source, "FFFFFFFF");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Engine code is non-empty
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Inject_WritesNonZeroEngineCode()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(0x00100000, "20123456 000003E8");

            // Check that engine code area has instructions (not all zeros)
            bool hasCode = false;
            for (uint addr = 0x000C0000; addr < 0x000C0100; addr += 4)
            {
                if (editor.ReadWord(addr) != 0)
                {
                    hasCode = true;
                    break;
                }
            }
            Assert.IsTrue(hasCode, "Engine code area should contain non-zero instructions.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Inject + Remove + Re-inject cycle
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void InjectRemoveReinject_WorksCorrectly()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.Inject(0x00100000, "20123456 000003E8");
            Assert.IsTrue(engine.IsInjected);

            engine.Remove();
            Assert.IsFalse(engine.IsInjected);

            // Re-inject with different cheats
            engine.Inject(0x00100000, "20AABBCC 0000FFFF");
            Assert.IsTrue(engine.IsInjected);

            uint tableBase = 0x000C0000 + 0x0300;
            Assert.AreEqual(0x20AABBCCu, editor.ReadWord(tableBase));
        }

        // ────────────────────────────────────────────────────────────────────
        //  InjectFromCds / UpdateCheatTableFromCds
        // ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void InjectFromCds_FreezeWord_WritesCorrectTableEntry()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 000003E8");

            uint tableBase = 0x000C0000 + 0x0300;
            // Type 0x20 (word), lower 24 bits of address = 0x123456
            Assert.AreEqual(0x20123456u, editor.ReadWord(tableBase));
            Assert.AreEqual(0x000003E8u, editor.ReadWord(tableBase + 4));
            // End marker
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 8));
        }

        [TestMethod]
        public void InjectFromCds_FreezeByte_WritesCorrectTableEntry()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze byte $00AABBCC = 63");

            uint tableBase = 0x000C0000 + 0x0300;
            // Type 0x00 (byte), lower 24 bits = 0xAABBCC
            Assert.AreEqual(0x00AABBCCu, editor.ReadWord(tableBase));
            Assert.AreEqual(0x00000063u, editor.ReadWord(tableBase + 4));
        }

        [TestMethod]
        public void InjectFromCds_FreezeHalf_WritesCorrectTableEntry()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze half $00AABBCC = 03E7");

            uint tableBase = 0x000C0000 + 0x0300;
            // Type 0x10 (half), lower 24 bits = 0xAABBCC
            Assert.AreEqual(0x10AABBCCu, editor.ReadWord(tableBase));
            Assert.AreEqual(0x000003E7u, editor.ReadWord(tableBase + 4));
        }

        [TestMethod]
        public void InjectFromCds_CondBlock_WritesConditionAndBodyEntries()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            var cds = @"
cond mem[$00010000] == 0001
  freeze word $00123456 = FFFFFFFF
endcond";

            engine.InjectFromCds(0x00100000, cds);

            uint tableBase = 0x000C0000 + 0x0300;
            // Condition entry: type 0xC0, lower 24 bits of 0x00010000 = 0x010000
            Assert.AreEqual(0xC0010000u, editor.ReadWord(tableBase));
            Assert.AreEqual(0x00000001u, editor.ReadWord(tableBase + 4));
            // Body entry: type 0x20, addr 0x123456, value 0xFFFFFFFF
            Assert.AreEqual(0x20123456u, editor.ReadWord(tableBase + 8));
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 12));
            // End marker
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 16));
        }

        [TestMethod]
        public void InjectFromCds_SetsIsInjected()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 03E8");
            Assert.IsTrue(engine.IsInjected);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void InjectFromCds_WhenAlreadyInjected_Throws()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 03E8");
            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 00FF");
        }

        [TestMethod]
        public void UpdateCheatTableFromCds_ChangesValues()
        {
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 000003E8");

            // Update with a different freeze
            engine.UpdateCheatTableFromCds("freeze half $00AABBCC = 0001");

            uint tableBase = 0x000C0000 + 0x0300;
            Assert.AreEqual(0x10AABBCCu, editor.ReadWord(tableBase));
            Assert.AreEqual(0x00000001u, editor.ReadWord(tableBase + 4));
            Assert.AreEqual(0xFFFFFFFFu, editor.ReadWord(tableBase + 8));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdateCheatTableFromCds_WhenNotInjected_Throws()
        {
            var editor = new MockByteEditor();
            var engine = new CdsCheatEngine(editor);
            engine.UpdateCheatTableFromCds("freeze word $00123456 = 03E8");
        }

        [TestMethod]
        public void InjectFromCds_ThenUpdateViaGameShark_Works()
        {
            // InjectFromCds should set up the engine so that
            // UpdateCheatTable (GameShark) also works afterwards.
            var editor = CreateEditor();
            var engine = new CdsCheatEngine(editor);

            engine.InjectFromCds(0x00100000, "freeze word $00123456 = 000003E8");
            engine.UpdateCheatTable("20AABBCC 0000FFFF");

            uint tableBase = 0x000C0000 + 0x0300;
            Assert.AreEqual(0x20AABBCCu, editor.ReadWord(tableBase));
            Assert.AreEqual(0x0000FFFFu, editor.ReadWord(tableBase + 4));
        }
    }
}
