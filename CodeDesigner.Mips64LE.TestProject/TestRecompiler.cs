using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Recompiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeDesigner.Mips64LE.TestProject
{
    /// <summary>
    /// Smoke tests for the static recompiler library
    /// (<see cref="Ps2ElfParser"/>, <see cref="InstructionTranslator"/>,
    /// <see cref="Ps2StaticRecompiler"/>).
    /// </summary>
    [TestClass]
    public class TestRecompiler
    {
        // ── ElfInfo helpers ────────────────────────────────────────────────────

        [TestMethod]
        public void Test_RecompFunction_MakeCppName_SymbolName()
        {
            Assert.AreEqual("main",        RecompFunction.MakeCppName("main",        0x100000));
            Assert.AreEqual("sceCdRead",   RecompFunction.MakeCppName("sceCdRead",   0x200000));
            Assert.AreEqual("__init",      RecompFunction.MakeCppName("__init",      0x300000));
            Assert.AreEqual("func_00100000", RecompFunction.MakeCppName("",          0x100000));
            Assert.AreEqual("func_00100000", RecompFunction.MakeCppName("   ",       0x100000));
        }

        [TestMethod]
        public void Test_RecompFunction_MakeCppName_StartsWithDigit()
        {
            // A symbol starting with a digit must get a leading underscore.
            string name = RecompFunction.MakeCppName("3dnow", 0);
            Assert.IsTrue(name.StartsWith("_"), $"Expected leading '_', got: {name}");
        }

        [TestMethod]
        public void Test_RecompFunction_MakeCppName_InvalidChars()
        {
            string name = RecompFunction.MakeCppName("some::func<T>", 0x1234);
            // colons, angle brackets → underscores
            Assert.IsFalse(name.Contains(':'), $"Should not contain ':': {name}");
            Assert.IsFalse(name.Contains('<'), $"Should not contain '<': {name}");
            Assert.IsFalse(name.Contains('>'), $"Should not contain '>': {name}");
        }

        // ── RecompilerConfig helpers ───────────────────────────────────────────

        [TestMethod]
        public void Test_RecompilerConfig_ParseStubAddressBindings()
        {
            var cfg = new RecompilerConfig
            {
                Stubs = new List<string>
                {
                    "sceCdRead@0x00123456",
                    "sceRead@0x789abc",
                    "plain_stub",          // no address — should be ignored
                }
            };

            var bindings = cfg.ParseStubAddressBindings();
            Assert.AreEqual(2, bindings.Count);
            Assert.AreEqual("sceCdRead", bindings[0x00123456]);
            Assert.AreEqual("sceRead",   bindings[0x00789abc]);
        }

        [TestMethod]
        public void Test_RecompilerConfig_GetStubNames()
        {
            var cfg = new RecompilerConfig
            {
                Stubs = new List<string>
                {
                    "sceCdRead@0x00123456",
                    "plainStub",
                }
            };

            var names = cfg.GetStubNames();
            Assert.IsTrue(names.Contains("sceCdRead"), "Should contain 'sceCdRead'");
            Assert.IsTrue(names.Contains("plainStub"), "Should contain 'plainStub'");
        }

        // ── InstructionTranslator ──────────────────────────────────────────────

        private static List<string> Translate(uint word, uint pc = 0x00100000, uint dsWord = 0,
            HashSet<uint>? labels = null, RecompilerConfig? cfg = null)
        {
            return InstructionTranslator.Translate(
                word, pc, dsWord,
                labels ?? new HashSet<uint>(),
                cfg ?? new RecompilerConfig());
        }

        [TestMethod]
        public void Test_Translate_NOP()
        {
            var lines = Translate(0x00000000);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "nop");
        }

        [TestMethod]
        public void Test_Translate_ADDIU_rt_rs_imm()
        {
            // ADDIU $a0, $a0, 0x20  =>  opcode=0x09, rs=4, rt=4, imm=0x20
            uint word = (0x09u << 26) | (4u << 21) | (4u << 16) | 0x20u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "ctx->r[4]");
            StringAssert.Contains(lines[0], "SE32");
        }

        [TestMethod]
        public void Test_Translate_ADDIU_DestZeroSuppressed()
        {
            // ADDIU $zero, $a0, 0x20 — write to $zero should be suppressed (comment)
            uint word = (0x09u << 26) | (4u << 21) | (0u << 16) | 0x20u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "$zero ignored");
        }

        [TestMethod]
        public void Test_Translate_LUI()
        {
            // LUI $v0, 0x8000  =>  opcode=0x0F, rs=0, rt=2, imm=0x8000
            uint word = (0x0Fu << 26) | (0u << 21) | (2u << 16) | 0x8000u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "ctx->r[2]");
            StringAssert.Contains(lines[0], "SE32");
            StringAssert.Contains(lines[0], "8000");
        }

        [TestMethod]
        public void Test_Translate_ORI()
        {
            // ORI $v0, $v0, 0xFF  =>  opcode=0x0D, rs=2, rt=2, imm=0xFF
            uint word = (0x0Du << 26) | (2u << 21) | (2u << 16) | 0xFFu;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "ctx->r[2]");
            StringAssert.Contains(lines[0], "|");
        }

        [TestMethod]
        public void Test_Translate_LW()
        {
            // LW $v0, 0x10($a0)  =>  opcode=0x23, rs=4, rt=2, imm=0x10
            uint word = (0x23u << 26) | (4u << 21) | (2u << 16) | 0x10u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "runtime->read32");
            StringAssert.Contains(lines[0], "ctx->r[4]");
        }

        [TestMethod]
        public void Test_Translate_SW()
        {
            // SW $v0, 0x10($a0)  =>  opcode=0x2B, rs=4, rt=2, imm=0x10
            uint word = (0x2Bu << 26) | (4u << 21) | (2u << 16) | 0x10u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "runtime->write32");
        }

        [TestMethod]
        public void Test_Translate_ADDU_R_type()
        {
            // ADDU $v0, $a0, $a1  =>  opcode=0, rs=4, rt=5, rd=2, funct=0x21
            uint word = (0u << 26) | (4u << 21) | (5u << 16) | (2u << 11) | 0x21u;
            var lines = Translate(word);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "ctx->r[2]");
            StringAssert.Contains(lines[0], "ctx->r[4]");
            StringAssert.Contains(lines[0], "ctx->r[5]");
        }

        [TestMethod]
        public void Test_Translate_JAL_emits_call_and_ra()
        {
            // JAL 0x00123ABC  =>  opcode=0x03, target=0x00123ABC>>2
            uint addr   = 0x00123ABC;
            uint target = (addr & 0x0FFFFFFF) >> 2;
            uint word   = (0x03u << 26) | target;
            uint pc     = 0x00100000;
            var lines = Translate(word, pc);
            string joined = string.Join(" ", lines);
            StringAssert.Contains(joined, "ctx->r[31]");   // $ra set
            StringAssert.Contains(joined, "RECOMP_CALL");
        }

        [TestMethod]
        public void Test_Translate_JR_emits_jump_reg()
        {
            // JR $ra  =>  opcode=0, rs=31, funct=0x08
            uint word = (0u << 26) | (31u << 21) | 0x08u;
            var lines = Translate(word);
            string joined = string.Join(" ", lines);
            StringAssert.Contains(joined, "RECOMP_JUMP_REG");
            StringAssert.Contains(joined, "ctx->r[31]");
        }

        [TestMethod]
        public void Test_Translate_BEQ_local_uses_goto()
        {
            uint pc     = 0x00100000;
            // BEQ $v0, $v1, +2  (offset=1 instructions forward, target = pc+4+4 = pc+8)
            uint tgt    = pc + 8;
            int  offset = (int)(tgt - pc - 4) >> 2; // =1
            uint word   = (0x04u << 26) | (2u << 21) | (3u << 16) | (uint)(offset & 0xFFFF);
            var labels  = new HashSet<uint> { tgt };
            var lines   = Translate(word, pc, 0, labels);
            string joined = string.Join("\n", lines);
            StringAssert.Contains(joined, "goto label_");
        }

        [TestMethod]
        public void Test_Translate_BNE_external_uses_RECOMP_JUMP()
        {
            uint pc   = 0x00100000;
            uint tgt  = 0x00200000; // far away — not in local labels
            int  imm  = (int)((tgt - pc - 4) >> 2);
            uint word = (0x05u << 26) | (2u << 21) | (3u << 16) | (uint)(imm & 0xFFFF);
            var lines = Translate(word, pc, 0, new HashSet<uint>());
            string joined = string.Join("\n", lines);
            StringAssert.Contains(joined, "RECOMP_JUMP");
        }

        [TestMethod]
        public void Test_Translate_SYSCALL_callsRuntime()
        {
            // SYSCALL  => opcode=0, funct=0x0C
            uint word = 0x0000000Cu;
            var lines = Translate(word);
            string joined = string.Join(" ", lines);
            StringAssert.Contains(joined, "handleSyscall");
        }

        [TestMethod]
        public void Test_Translate_MULT_producesHiLo()
        {
            // MULT $a0, $a1  =>  opcode=0, rs=4, rt=5, rd=0, funct=0x18
            uint word = (0u << 26) | (4u << 21) | (5u << 16) | 0x18u;
            var lines = Translate(word);
            string joined = string.Join(" ", lines);
            StringAssert.Contains(joined, "ctx->lo");
            StringAssert.Contains(joined, "ctx->hi");
        }

        [TestMethod]
        public void Test_Translate_CACHE_patchedToNop()
        {
            // CACHE op=1, base=$sp, offset=0  => opcode=0x2F, rt=1, rs=29
            uint word = (0x2Fu << 26) | (29u << 21) | (1u << 16);
            var cfg   = new RecompilerConfig { PatchCache = true };
            var lines = Translate(word, cfg: cfg);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "nop");
        }

        [TestMethod]
        public void Test_Translate_CACHE_notPatched_callsRuntime()
        {
            uint word = (0x2Fu << 26) | (29u << 21) | (1u << 16);
            var cfg   = new RecompilerConfig { PatchCache = false };
            var lines = Translate(word, cfg: cfg);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "runtime->cacheOp");
        }

        [TestMethod]
        public void Test_CollectLocalLabels_BEQ()
        {
            uint pc   = 0x00100000;
            uint tgt  = pc + 8;
            int  off  = (int)((tgt - pc - 4) >> 2);
            uint word = (0x04u << 26) | (2u << 21) | (3u << 16) | (uint)(off & 0xFFFF);

            var instrs = new List<(uint, uint)>
            {
                (pc,      word),
                (pc + 4,  0u),     // delay slot NOP
                (pc + 8,  0u),     // BEQ target
            };

            var labels = InstructionTranslator.CollectLocalLabels(instrs);
            Assert.IsTrue(labels.Contains(tgt), $"Expected label at 0x{tgt:X8}");
        }

        // ── Ps2ElfParser ──────────────────────────────────────────────────────

        [TestMethod]
        public void Test_Ps2ElfParser_InvalidMagic()
        {
            byte[] bad = new byte[64];
            Assert.ThrowsException<ElfParseException>(() => Ps2ElfParser.Parse(bad));
        }

        [TestMethod]
        public void Test_Ps2ElfParser_TooSmall()
        {
            byte[] tiny = new byte[10];
            Assert.ThrowsException<ElfParseException>(() => Ps2ElfParser.Parse(tiny));
        }

        [TestMethod]
        public void Test_Ps2ElfParser_MinimalELF()
        {
            // Build the smallest valid ELF32 LE with entry point 0x00100000 and
            // no sections / program headers.
            byte[] elf = BuildMinimalElf32(entryPoint: 0x00100000);
            var info = Ps2ElfParser.Parse(elf);
            Assert.AreEqual(0x00100000u, info.EntryPoint);
            Assert.AreEqual(0, info.Sections.Count);
            Assert.AreEqual(0, info.Symbols.Count);
        }

        // ── Ps2StaticRecompiler end-to-end smoke test ──────────────────────────

        [TestMethod]
        public void Test_Ps2StaticRecompiler_MinimalELF_WritesFiles()
        {
            // Build an ELF with two instructions in an executable section:
            //   ADDIU $v0, $zero, 1   (0x24020001)
            //   JR $ra                 (0x03E00008)
            uint[] code    = { 0x24020001u, 0x03E00008u };
            uint   codeVA  = 0x00100000u;
            uint   entry   = codeVA;

            byte[] elfData = BuildElfWithTextSection(entry, codeVA, code);

            string tmpDir = Path.Combine(Path.GetTempPath(), $"recomp_test_{Guid.NewGuid():N}");
            try
            {
                var cfg = new RecompilerConfig
                {
                    InputElf         = "test.elf",
                    OutputDirectory  = tmpDir,
                    SingleFileOutput = true,
                };

                var recompiler = new Ps2StaticRecompiler(cfg);
                var result = recompiler.Recompile(elfData);

                Assert.IsTrue(result.Functions.Count >= 1, "Should have found at least one function");
                Assert.IsTrue(result.WrittenFiles.Count >= 2, "Should have written at least a header + cpp");

                // Runtime header must exist
                string header = Path.Combine(tmpDir, "ps2_recomp_runtime.h");
                Assert.IsTrue(File.Exists(header), $"Runtime header missing: {header}");
                string headerText = File.ReadAllText(header);
                StringAssert.Contains(headerText, "Ps2Context");
                StringAssert.Contains(headerText, "Ps2Runtime");

                // Output cpp must exist and contain our function
                string cpp = Path.Combine(tmpDir, "recomp_output.cpp");
                Assert.IsTrue(File.Exists(cpp), $"Output cpp missing: {cpp}");
                string cppText = File.ReadAllText(cpp);
                StringAssert.Contains(cppText, "ps2_recomp_runtime.h");
                StringAssert.Contains(cppText, "Ps2Context");
            }
            finally
            {
                if (Directory.Exists(tmpDir))
                    Directory.Delete(tmpDir, recursive: true);
            }
        }

        // ── ELF builder helpers ────────────────────────────────────────────────

        /// <summary>
        /// Build the smallest valid ELF32 LE header with no sections or program headers.
        /// </summary>
        private static byte[] BuildMinimalElf32(uint entryPoint)
        {
            var buf = new byte[52]; // ELF32 header is exactly 52 bytes
            // Magic
            buf[0] = 0x7F; buf[1] = (byte)'E'; buf[2] = (byte)'L'; buf[3] = (byte)'F';
            buf[4] = 1;    // ELFCLASS32
            buf[5] = 1;    // ELFDATA2LSB
            buf[6] = 1;    // EV_CURRENT
            // e_type = ET_EXEC (2)
            buf[16] = 2; buf[17] = 0;
            // e_machine = MIPS (8)
            buf[18] = 8; buf[19] = 0;
            // e_version = 1
            buf[20] = 1;
            // e_entry
            Write32LE(buf, 24, entryPoint);
            // e_phoff = 0, e_shoff = 0 (no headers)
            // e_ehsize = 52
            buf[40] = 52; buf[41] = 0;
            // e_phentsize / phnum = 0
            // e_shentsize = 40
            buf[46] = 40; buf[47] = 0;
            // e_shnum = 0, e_shstrndx = 0
            return buf;
        }

        /// <summary>
        /// Build an ELF32 LE with a single executable .text section containing the
        /// given instruction words at the specified virtual address.
        /// </summary>
        private static byte[] BuildElfWithTextSection(uint entry, uint sectionVA, uint[] words)
        {
            // Layout:
            //   [0..51]   ELF header (52 bytes)
            //   [52..N]   .text section data (4*words.Length bytes)
            //   [N..]     .shstrtab section data (string table)
            //   [..]      Section headers (3 entries × 40 bytes: null, .text, .shstrtab)

            byte[] textData = new byte[words.Length * 4];
            for (int i = 0; i < words.Length; i++)
                Write32LE(textData, i * 4, words[i]);

            byte[] shstrtab = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");

            const uint ehdrSize    = 52;
            const uint shentSize   = 40;
            const uint shCount     = 3; // null, .text, .shstrtab

            uint textOffset    = ehdrSize;
            uint shstrtabOffset = textOffset + (uint)textData.Length;
            uint shOffset      = shstrtabOffset + (uint)shstrtab.Length;

            // Align shOffset to 4
            while (shOffset % 4 != 0) shOffset++;

            uint totalSize = shOffset + shCount * shentSize;
            var buf = new byte[totalSize];

            // ── ELF header ─────────────────────────────────────────────────────
            buf[0] = 0x7F; buf[1] = (byte)'E'; buf[2] = (byte)'L'; buf[3] = (byte)'F';
            buf[4] = 1; buf[5] = 1; buf[6] = 1;
            Write16LE(buf, 16, 2);   // e_type = ET_EXEC
            Write16LE(buf, 18, 8);   // e_machine = MIPS
            Write32LE(buf, 20, 1);   // e_version
            Write32LE(buf, 24, entry);
            // e_phoff = 0 (no program headers)
            Write32LE(buf, 32, shOffset);  // e_shoff
            Write16LE(buf, 40, (ushort)ehdrSize);  // e_ehsize
            Write16LE(buf, 42, 0);                  // e_phentsize
            Write16LE(buf, 44, 0);                  // e_phnum
            Write16LE(buf, 46, (ushort)shentSize);  // e_shentsize
            Write16LE(buf, 48, (ushort)shCount);    // e_shnum
            Write16LE(buf, 50, 2);         // e_shstrndx = 2 (.shstrtab is index 2)

            // ── Section data ───────────────────────────────────────────────────
            Array.Copy(textData, 0, buf, textOffset, textData.Length);
            Array.Copy(shstrtab, 0, buf, shstrtabOffset, shstrtab.Length);

            // ── Section headers ────────────────────────────────────────────────
            // SH[0] = null
            // SH[1] = .text
            uint sh1 = shOffset + shentSize;
            Write32LE(buf, (int)sh1 + 0,  1);               // sh_name index (-> ".text\0")
            Write32LE(buf, (int)sh1 + 4,  ElfSection.SHT_PROGBITS);
            Write32LE(buf, (int)sh1 + 8,  ElfSection.SHF_ALLOC | ElfSection.SHF_EXECINSTR);
            Write32LE(buf, (int)sh1 + 12, sectionVA);       // sh_addr
            Write32LE(buf, (int)sh1 + 16, textOffset);      // sh_offset
            Write32LE(buf, (int)sh1 + 20, (uint)textData.Length); // sh_size
            // sh_link=0, sh_info=0, sh_addralign=4, sh_entsize=0
            Write32LE(buf, (int)sh1 + 32, 4); // sh_addralign

            // SH[2] = .shstrtab
            uint sh2 = shOffset + 2 * shentSize;
            Write32LE(buf, (int)sh2 + 0,  7);               // sh_name index (-> ".shstrtab\0")
            Write32LE(buf, (int)sh2 + 4,  ElfSection.SHT_STRTAB);
            Write32LE(buf, (int)sh2 + 12, 0);               // sh_addr = 0 (not allocated)
            Write32LE(buf, (int)sh2 + 16, shstrtabOffset);  // sh_offset
            Write32LE(buf, (int)sh2 + 20, (uint)shstrtab.Length); // sh_size

            return buf;
        }

        private static void Write32LE(byte[] buf, int off, uint v)
        {
            buf[off]     = (byte)(v);
            buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16);
            buf[off + 3] = (byte)(v >> 24);
        }

        private static void Write16LE(byte[] buf, int off, ushort v)
        {
            buf[off]     = (byte)(v);
            buf[off + 1] = (byte)(v >> 8);
        }
    }
}
