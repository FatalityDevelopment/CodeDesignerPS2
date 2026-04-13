using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CodeDesigner.Ps2.CheatEngine.Enums;
using CodeDesigner.Ps2.CdsLanguage;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Injects a persistent MIPS cheat-engine routine into PS2 EE RAM and hooks
    /// it into the game's execution path.  Unlike <see cref="CheatEngine"/>, which
    /// performs one-shot memory writes that the CPU can immediately overwrite,
    /// this engine runs every frame from inside the game loop, continuously
    /// re-applying cheat values — the same approach used by classic GameShark /
    /// Action Replay hardware.
    ///
    /// <para><b>Workflow:</b></para>
    /// <list type="number">
    ///   <item>Call <see cref="Inject"/> with the cheats and hook address.</item>
    ///   <item>The engine is compiled from the embedded CDS template, written
    ///         into RAM, and hooked via a <c>j</c> instruction at the hook point.</item>
    ///   <item>Call <see cref="UpdateCheatTable"/> to change cheats without re-hooking.</item>
    ///   <item>Call <see cref="Remove"/> to unhook and restore the original code.</item>
    /// </list>
    ///
    /// <para><b>Memory layout</b> (offsets from <see cref="EngineBase"/>):</para>
    /// <list type="table">
    ///   <item><term><c>+0x0000</c></term><description>Engine code</description></item>
    ///   <item><term><c>+0x0200</c></term><description>Engine data (original instructions, return address)</description></item>
    ///   <item><term><c>+0x0300</c></term><description>Cheat table (8 bytes per entry + end marker)</description></item>
    /// </list>
    /// </summary>
    public sealed class CdsCheatEngine
    {
        // ── Layout constants ────────────────────────────────────────────────
        private const int DataOffset       = 0x0200;
        private const int CheatTableOffset = 0x0300;

        /// <summary>Maximum number of cheat table entries (each 8 bytes).</summary>
        private const int MaxCheatEntries  = 128;

        /// <summary>Memory offset applied to cheat addresses (PS2 uncached user-space base).</summary>
        private const uint MemoryOffset    = 0x20000000;

        // ── Instance state ──────────────────────────────────────────────────

        private readonly IByteEditor _byteEditor;

        /// <summary>Base address where the engine code is placed in EE RAM.</summary>
        public uint EngineBase { get; }

        /// <summary>Address where the hook <c>j</c> instruction is written.</summary>
        public uint HookAddress { get; private set; }

        /// <summary>Whether the engine is currently injected and hooked.</summary>
        public bool IsInjected { get; private set; }

        // Saved original instructions so we can restore on Remove().
        private uint _origWord0;
        private uint _origWord1;

        /// <summary>
        /// Create a new CDS cheat engine instance.
        /// </summary>
        /// <param name="byteEditor">Memory backend (emulator RAM or PINE).</param>
        /// <param name="engineBase">
        /// Virtual address where the engine code will be placed.
        /// Must point to unused EE RAM.  Default is <c>0x000C0000</c>.
        /// </param>
        public CdsCheatEngine(IByteEditor byteEditor, uint engineBase = 0x000C0000)
        {
            _byteEditor = byteEditor ?? throw new ArgumentNullException(nameof(byteEditor));
            EngineBase  = engineBase;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Compile the CDS cheat-engine template, write it into EE RAM, populate
        /// the cheat table, and hook the game at <paramref name="hookAddress"/>.
        /// </summary>
        /// <param name="hookAddress">
        /// Virtual address of the instruction to replace with a <c>j engineEntry</c>.
        /// This should be an instruction that executes every frame (e.g. inside the
        /// VBlank handler or main loop).  The instruction at <paramref name="hookAddress"/>
        /// and the one at <paramref name="hookAddress"/>+4 are saved and replayed
        /// inside the engine before returning.
        /// </param>
        /// <param name="cheats">Cheat codes in GameShark text format.</param>
        public void Inject(uint hookAddress, string cheats)
        {
            if (IsInjected)
                throw new InvalidOperationException("Engine is already injected. Call Remove() first.");

            HookAddress = hookAddress;

            // ── 1. Compile engine code ──────────────────────────────────────
            string cdsSource = GetCdsTemplate();
            // Rebase the template to the actual engine base address
            cdsSource = RebaseSource(cdsSource, EngineBase);

            var compiled = CompileCds(cdsSource);

            // ── 2. Write compiled engine into RAM ───────────────────────────
            foreach (var (addr, word) in compiled)
            {
                WriteWord(addr, word);
            }

            // ── 3. Save original instructions at the hook point ─────────────
            _origWord0 = ReadWord(hookAddress);
            _origWord1 = ReadWord(hookAddress + 4);

            // ── 4. Patch engine: embed original instructions ────────────────
            PatchOriginalInstructions(_origWord0, _origWord1);

            // ── 5. Patch engine: set return address ─────────────────────────
            uint returnAddr = hookAddress + 8;
            PatchReturnAddress(returnAddr);

            // ── 6. Patch engine: set cheat table base pointer ───────────────
            PatchTableBasePointer();

            // ── 7. Write cheat table ────────────────────────────────────────
            WriteCheatTable(cheats);

            // ── 8. Store metadata in engine data area ───────────────────────
            WriteWord(EngineBase + DataOffset,     _origWord0);
            WriteWord(EngineBase + DataOffset + 4, _origWord1);
            WriteWord(EngineBase + DataOffset + 8, returnAddr);

            // ── 9. Hook: replace instruction at hook point with j engine ────
            uint engineEntry = EngineBase; // entry label is at offset 0
            uint jInstr = EncodeJ(engineEntry);
            WriteWord(hookAddress,     jInstr);
            WriteWord(hookAddress + 4, 0x00000000); // nop in delay slot

            IsInjected = true;
        }

        /// <summary>
        /// Update the cheat table in-place without re-hooking.
        /// The engine continues running with the new cheat values on the next frame.
        /// </summary>
        /// <param name="cheats">Cheat codes in GameShark text format.</param>
        public void UpdateCheatTable(string cheats)
        {
            if (!IsInjected)
                throw new InvalidOperationException("Engine is not injected. Call Inject() first.");

            WriteCheatTable(cheats);
        }

        /// <summary>
        /// Remove the hook: restore the original instructions at the hook point
        /// and mark the engine as not injected.
        /// </summary>
        public void Remove()
        {
            if (!IsInjected) return;

            // Restore original instructions at the hook point
            WriteWord(HookAddress,     _origWord0);
            WriteWord(HookAddress + 4, _origWord1);

            IsInjected = false;
        }

        // ────────────────────────────────────────────────────────────────────
        //  CDS source generation
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generate the full CDS source for the engine with a specific cheat table
        /// embedded.  Useful for inspection or manual compilation.
        /// </summary>
        /// <param name="cheats">Cheat codes in GameShark text format.</param>
        /// <returns>Complete CDS source text.</returns>
        public string GenerateSource(string cheats)
        {
            string cdsSource = GetCdsTemplate();
            cdsSource = RebaseSource(cdsSource, EngineBase);

            // Append cheat table entries
            var tableEntries = BuildCheatTableEntries(cheats);
            var sb = new StringBuilder(cdsSource);
            sb.AppendLine();
            sb.AppendLine($"address ${(EngineBase + CheatTableOffset):X8}");
            sb.AppendLine("scope GeneratedCheatTable {");
            foreach (var (addr, val) in tableEntries)
            {
                sb.AppendLine($"    hexcode ${addr:X8}");
                sb.AppendLine($"    hexcode ${val:X8}");
            }
            // End marker
            sb.AppendLine("    hexcode $FFFFFFFF");
            sb.AppendLine("    hexcode $FFFFFFFF");
            sb.AppendLine("}");

            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────
        //  Internal helpers
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Load the CDS template from the embedded resource.</summary>
        private static string GetCdsTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "CodeDesigner.Ps2.CheatEngine.cds.cheatengine.cds";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException(
                    $"Embedded CDS template not found: {resourceName}");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Rewrite all <c>address $000C????</c> directives in the template to use
        /// the actual engine base address.
        /// </summary>
        private static string RebaseSource(string source, uint engineBase)
        {
            // The template uses 0x000C0000 as a placeholder base.
            // Replace the three address directives for code, data, and table sections.
            const uint templateBase = 0x000C0000;

            source = source.Replace(
                $"address ${templateBase:X8}",
                $"address ${engineBase:X8}");
            source = source.Replace(
                $"address ${(templateBase + DataOffset):X8}",
                $"address ${(engineBase + DataOffset):X8}");
            source = source.Replace(
                $"address ${(templateBase + CheatTableOffset):X8}",
                $"address ${(engineBase + CheatTableOffset):X8}");

            return source;
        }

        /// <summary>Compile CDS source into (address, word) pairs.</summary>
        private static List<(uint Address, uint Word)> CompileCds(string source)
        {
            var parser = new CdsParser();
            var items  = parser.Parse(source);
            return CdsCompiler.Compile(items);
        }

        /// <summary>
        /// Build cheat table entry pairs from GameShark-format text.
        /// Each entry is two 32-bit words: (type+address, value).
        /// </summary>
        private static List<(uint TypeAddr, uint Value)> BuildCheatTableEntries(string cheats)
        {
            var entries = new List<(uint, uint)>();
            if (string.IsNullOrWhiteSpace(cheats)) return entries;

            var cheatList = new CheatList(cheats);
            var blocks = cheatList.Parse();

            foreach (var block in blocks)
            {
                foreach (var cheat in block.Cheats)
                {
                    // Reconstruct the raw GameShark word: type_byte << 24 | 24-bit address
                    uint typeAddr = ((uint)cheat.CheatType << 24) | ((uint)cheat.Address & 0x00FFFFFF);

                    // Data is stored as little-endian byte[4]; convert back to uint (big-endian decode)
                    uint value = 0;
                    if (cheat.Data.Length >= 4)
                    {
                        value = (uint)(cheat.Data[3] << 24 | cheat.Data[2] << 16 |
                                       cheat.Data[1] << 8  | cheat.Data[0]);
                    }
                    else if (cheat.Data.Length >= 2)
                    {
                        value = (uint)(cheat.Data[1] << 8 | cheat.Data[0]);
                    }
                    else if (cheat.Data.Length >= 1)
                    {
                        value = cheat.Data[0];
                    }

                    entries.Add((typeAddr, value));
                }
            }

            return entries;
        }

        /// <summary>
        /// Write cheat table entries into the engine's table area in RAM.
        /// </summary>
        private void WriteCheatTable(string cheats)
        {
            var entries = BuildCheatTableEntries(cheats);
            if (entries.Count > MaxCheatEntries)
                throw new InvalidOperationException(
                    $"Too many cheat entries ({entries.Count}). Maximum is {MaxCheatEntries}.");

            uint tableBase = EngineBase + CheatTableOffset;

            for (int i = 0; i < entries.Count; i++)
            {
                WriteWord(tableBase + (uint)(i * 8),     entries[i].TypeAddr);
                WriteWord(tableBase + (uint)(i * 8) + 4, entries[i].Value);
            }

            // Write end marker after last entry
            uint endOffset = tableBase + (uint)(entries.Count * 8);
            WriteWord(endOffset,     0xFFFFFFFF);
            WriteWord(endOffset + 4, 0xFFFFFFFF);
        }

        /// <summary>
        /// Patch the engine code to embed the two original instructions from the
        /// hook point, so they execute inside the engine before returning.
        /// </summary>
        /// <remarks>
        /// The CDS template has two <c>nop</c> placeholders at the "origInstr0" and
        /// "origInstr1" labels.  We locate them by scanning the compiled output for
        /// the known pattern: they are at a fixed offset from the engine entry.
        /// Instead of scanning, we write directly to the data area and also patch
        /// the code in-place.
        /// </remarks>
        private void PatchOriginalInstructions(uint word0, uint word1)
        {
            // Scan the entire code section for the nop-nop-lui_v0-ori_v0 pattern.
            // The engine code may not fill the full DataOffset region, so we must
            // start from EngineBase rather than scanning backwards from DataOffset.
            uint patchAddr0 = 0;

            for (uint addr = EngineBase; addr < EngineBase + DataOffset - 20; addr += 4)
            {
                uint w0 = ReadWord(addr);
                uint w1 = ReadWord(addr + 4);
                uint w2 = ReadWord(addr + 8);
                uint w3 = ReadWord(addr + 12);

                // Pattern: nop, nop, lui v0 (0x3C020000), ori v0 (0x34420000)
                if (w0 == 0x00000000 && w1 == 0x00000000 &&
                    (w2 & 0xFFFF0000) == 0x3C020000 &&
                    (w3 & 0xFFFF0000) == 0x34420000)
                {
                    patchAddr0 = addr;
                    break;
                }
            }

            if (patchAddr0 == 0)
                throw new InvalidOperationException(
                    "Could not locate origInstr placeholders in compiled engine code.");

            WriteWord(patchAddr0,     word0);
            WriteWord(patchAddr0 + 4, word1);
        }

        /// <summary>
        /// Patch the engine's return-address load sequence (lui v0 + ori v0) to
        /// point to <paramref name="returnAddr"/>.
        /// </summary>
        private void PatchReturnAddress(uint returnAddr)
        {
            ushort upper = (ushort)(returnAddr >> 16);
            ushort lower = (ushort)(returnAddr & 0xFFFF);

            // Scan for lui v0 (0x3C02xxxx) + ori v0, v0 (0x3442xxxx) in the code section
            for (uint addr = EngineBase; addr < EngineBase + DataOffset - 8; addr += 4)
            {
                uint w0 = ReadWord(addr);
                uint w1 = ReadWord(addr + 4);

                if ((w0 & 0xFFFF0000) == 0x3C020000 &&
                    (w1 & 0xFFFF0000) == 0x34420000)
                {
                    WriteWord(addr,     0x3C020000 | (uint)upper);   // lui v0, upper
                    WriteWord(addr + 4, 0x34420000 | (uint)lower);   // ori v0, v0, lower
                    return;
                }
            }

            throw new InvalidOperationException(
                "Could not locate return-address load sequence in compiled engine code.");
        }

        /// <summary>
        /// Patch the cheat table base pointer (lui s0 + ori s0) in the engine code
        /// to point to the actual table address.
        /// </summary>
        private void PatchTableBasePointer()
        {
            uint tableAddr = EngineBase + CheatTableOffset;
            ushort upper = (ushort)(tableAddr >> 16);
            ushort lower = (ushort)(tableAddr & 0xFFFF);

            // Scan for lui s0 (0x3C10xxxx) + ori s0, s0 (0x3610xxxx) in the code section
            for (uint addr = EngineBase; addr < EngineBase + DataOffset - 8; addr += 4)
            {
                uint w0 = ReadWord(addr);
                uint w1 = ReadWord(addr + 4);

                if ((w0 & 0xFFFF0000) == 0x3C100000 &&
                    (w1 & 0xFFFF0000) == 0x36100000)
                {
                    WriteWord(addr,     0x3C100000 | (uint)upper);   // lui s0, upper
                    WriteWord(addr + 4, 0x36100000 | (uint)lower);   // ori s0, s0, lower
                    return;
                }
            }

            throw new InvalidOperationException(
                "Could not locate table-base-pointer load sequence in compiled engine code.");
        }

        // ── MIPS encoding helpers ───────────────────────────────────────────

        /// <summary>Encode a MIPS <c>j target</c> instruction.</summary>
        private static uint EncodeJ(uint targetAddress)
        {
            // J-type: opcode 0x02, 26-bit word address
            uint wordAddr = (targetAddress >> 2) & 0x03FFFFFF;
            return 0x08000000 | wordAddr;
        }

        // ── Memory I/O wrappers ─────────────────────────────────────────────

        private void WriteWord(uint address, uint word)
        {
            byte[] data = BitConverter.GetBytes(word);
            // PS2 is little-endian; BitConverter on LE hosts matches.
            // On BE hosts we'd need to reverse — but .NET on PS2 tooling is always LE.
            _byteEditor.Write((int)address, data);
        }

        private uint ReadWord(uint address)
        {
            byte[] data = _byteEditor.Read((int)address, 4);
            return BitConverter.ToUInt32(data, 0);
        }
    }
}
