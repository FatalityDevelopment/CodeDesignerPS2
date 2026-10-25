using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeDesigner.Ps2.CdsLanguage;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Injects a persistent MIPS cheat-engine routine into PS2 EE RAM and hooks
    /// it into the game's execution path.  Unlike <see cref="CheatEngine"/>, which
    /// performs one-shot memory writes that the game can immediately overwrite,
    /// this engine runs every time the hook fires (normally once per frame),
    /// re-applying cheat values — the same approach as GameShark / Action Replay
    /// hardware.
    ///
    /// <para><b>Workflow:</b></para>
    /// <list type="number">
    ///   <item>Raw codes are translated into engine rows by <see cref="RawCodeTranslator"/>
    ///         (CDS source is first compiled with <see cref="CdsCompiler.CompileToCodes"/>).</item>
    ///   <item>The engine image (code + data + table, see cds/cheatengine.cds) is built for
    ///         <see cref="EngineBase"/> and written into RAM.</item>
    ///   <item>The two instructions at the hook point are copied into the engine and
    ///         replaced by <c>j engine; nop</c>. The engine replays them and jumps back
    ///         to hook + 8.</item>
    ///   <item><see cref="UpdateCheatTable"/> swaps the table without re-hooking;
    ///         <see cref="Remove"/> restores the hook point.</item>
    /// </list>
    ///
    /// <para><b>Hook point:</b> any pair of instructions that runs every frame and contains
    /// no branch or jump — except <c>jr ra</c> followed by its delay slot, which is also
    /// safe (function epilogues make good hooks). PC-relative branches and linking jumps
    /// cannot be relocated into the engine and are rejected.</para>
    ///
    /// <para><b>Memory layout</b> (offsets from <see cref="EngineBase"/>):</para>
    /// <list type="table">
    ///   <item><term><c>+0x0000</c></term><description>Engine code</description></item>
    ///   <item><term><c>+0x0400</c></term><description>Engine data (original instructions, return address, frame counter)</description></item>
    ///   <item><term><c>+0x0500</c></term><description>Table rows (16 bytes each), terminated by a zero control word</description></item>
    /// </list>
    /// </summary>
    public sealed class CdsCheatEngine
    {
        // ── Layout constants ────────────────────────────────────────────────
        public const int DataOffset       = 0x0400;
        public const int CheatTableOffset = 0x0500;
        public const int EntrySize        = 16;

        /// <summary>Maximum number of table rows.</summary>
        public const int MaxCheatEntries  = 512;

        /// <summary>Bytes from <see cref="EngineBase"/> to the end of a full table (incl. end row).</summary>
        public const int ImageSize        = CheatTableOffset + (MaxCheatEntries + 1) * EntrySize;

        /// <summary>Default memory base OR'd onto code addresses (the uncached EE RAM mirror).</summary>
        public const uint DefaultMemoryBase = 0x20000000;

        private const uint MarkerOrig0  = 0xC0DE0001;
        private const uint MarkerOrig1  = 0xC0DE0002;
        private const uint MarkerReturn = 0xC0DE0003;

        // ── Instance state ──────────────────────────────────────────────────

        private readonly IByteEditor _byteEditor;
        private readonly uint _memoryBase;

        /// <summary>Base address where the engine code is placed in EE RAM.</summary>
        public uint EngineBase { get; }

        /// <summary>Address where the hook <c>j</c> instruction is written.</summary>
        public uint HookAddress { get; private set; }

        /// <summary>Whether the engine is currently injected and hooked.</summary>
        public bool IsInjected { get; private set; }

        /// <summary>Warnings from the last translation (e.g. ignored master codes).</summary>
        public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();

        /// <summary>Number of table rows written by the last inject/update.</summary>
        public int LastEntryCount { get; private set; }

        // Saved original instructions so we can restore on Remove().
        private uint _origWord0;
        private uint _origWord1;

        /// <summary>The two instructions the hook replaced (valid while <see cref="IsInjected"/>).</summary>
        public (uint Word0, uint Word1) OriginalInstructions => (_origWord0, _origWord1);

        /// <summary>
        /// Create a new CDS cheat engine instance.
        /// </summary>
        /// <param name="byteEditor">Memory backend (emulator RAM or PINE).</param>
        /// <param name="engineBase">
        /// Virtual address where the engine code will be placed.
        /// Must point to <see cref="ImageSize"/> bytes of unused EE RAM.  Default is <c>0x000C0000</c>.
        /// </param>
        /// <param name="memoryBase">
        /// OR'd onto code addresses; <see cref="DefaultMemoryBase"/> matches one-shot APPLYCHEATS.
        /// </param>
        public CdsCheatEngine(IByteEditor byteEditor, uint engineBase = 0x000C0000, uint memoryBase = DefaultMemoryBase)
        {
            _byteEditor = byteEditor ?? throw new ArgumentNullException(nameof(byteEditor));
            EngineBase  = engineBase;
            _memoryBase = memoryBase;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build and write the engine, fill the table from raw cheat codes, and hook
        /// the game at <paramref name="hookAddress"/>.
        /// </summary>
        /// <param name="hookAddress">Address of an instruction pair that runs every frame (see the class remarks).</param>
        /// <param name="cheats">Raw cheat codes; names and blank lines separate cheats.</param>
        public void Inject(uint hookAddress, string cheats)
        {
            if (IsInjected)
                throw new InvalidOperationException("Engine is already injected. Call Remove() first.");

            var entries = Translate(cheats);

            uint word0 = ReadWord(hookAddress);
            uint word1 = ReadWord(hookAddress + 4);
            ValidateHook(hookAddress, word0, word1, EngineBase);

            byte[] image = BuildImage(entries, EngineBase, hookAddress, word0, word1);
            _byteEditor.Write((int)EngineBase, image);

            _origWord0  = word0;
            _origWord1  = word1;
            HookAddress = hookAddress;

            // Hook: replace the instruction pair with `j engine; nop`.
            WriteWord(hookAddress,     EncodeJ(EngineBase));
            WriteWord(hookAddress + 4, 0x00000000);

            IsInjected = true;
        }

        /// <summary>
        /// Same as <see cref="Inject"/> but from CDS source: every construct that
        /// <see cref="CdsCompiler.CompileToCodes"/> understands (assembled code, raw code
        /// lines, <c>freeze</c>, <c>cond</c>, <c>timer</c>) becomes engine rows.
        /// </summary>
        public void InjectFromCds(uint hookAddress, string cdsSource)
            => Inject(hookAddress, CompileCdsToCodeText(cdsSource));

        /// <summary>
        /// Replace the table in place without re-hooking. The engine picks up the new
        /// rows on its next run.
        /// </summary>
        public void UpdateCheatTable(string cheats)
        {
            if (!IsInjected)
                throw new InvalidOperationException("Engine is not injected. Call Inject() first.");

            var entries = Translate(cheats);
            _byteEditor.Write((int)(EngineBase + CheatTableOffset), BuildTable(entries));
        }

        /// <summary>CDS-source variant of <see cref="UpdateCheatTable"/>.</summary>
        public void UpdateCheatTableFromCds(string cdsSource)
        {
            if (!IsInjected)
                throw new InvalidOperationException("Engine is not injected. Call InjectFromCds() or Inject() first.");

            UpdateCheatTable(CompileCdsToCodeText(cdsSource));
        }

        /// <summary>
        /// Remove the hook: restore the original instructions at the hook point
        /// and mark the engine as not injected.
        /// </summary>
        public void Remove()
        {
            if (!IsInjected) return;

            WriteWord(HookAddress,     _origWord0);
            WriteWord(HookAddress + 4, _origWord1);

            IsInjected = false;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Image building (also used to export the engine as a binary)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the engine image — code, data and table — as the bytes to place at
        /// <paramref name="engineBase"/>. When <paramref name="hookAddress"/> is given,
        /// the original hook instructions and the return jump are embedded; otherwise
        /// they are left as nops and the image must be patched before it is hooked.
        /// </summary>
        public static byte[] BuildImage(IReadOnlyList<EngineEntry> entries, uint engineBase,
                                        uint? hookAddress = null, uint origWord0 = 0, uint origWord1 = 0)
        {
            if (entries.Count > MaxCheatEntries)
                throw new InvalidOperationException(
                    $"Too many engine rows ({entries.Count}). Maximum is {MaxCheatEntries}.");

            var image = new byte[CheatTableOffset + (entries.Count + 1) * EntrySize];

            uint returnJump = hookAddress.HasValue ? EncodeJ(hookAddress.Value + 8) : 0;
            uint word0 = hookAddress.HasValue ? origWord0 : 0;
            uint word1 = hookAddress.HasValue ? origWord1 : 0;

            bool sawMarkers = false;
            foreach (var (addr, word) in CompileEngine(engineBase))
            {
                uint offset = addr - engineBase;
                if (offset + 4 > CheatTableOffset)
                    throw new InvalidOperationException(
                        $"Engine code at 0x{addr:X8} overflows the code/data area (base 0x{engineBase:X8}).");
                uint value = word switch
                {
                    MarkerOrig0  => word0,
                    MarkerOrig1  => word1,
                    MarkerReturn => returnJump,
                    _            => word,
                };
                sawMarkers |= word == MarkerReturn;
                BitConverter.GetBytes(value).CopyTo(image, (int)offset);
            }
            if (!sawMarkers)
                throw new InvalidOperationException("Engine template is missing its hook placeholders.");

            // Data area: original words + return address for Remove()/inspection.
            BitConverter.GetBytes(word0).CopyTo(image, DataOffset);
            BitConverter.GetBytes(word1).CopyTo(image, DataOffset + 4);
            BitConverter.GetBytes(hookAddress.HasValue ? hookAddress.Value + 8 : 0u).CopyTo(image, DataOffset + 8);

            BuildTable(entries).CopyTo(image, CheatTableOffset);
            return image;
        }

        /// <summary>Encode table rows plus the end row.</summary>
        public static byte[] BuildTable(IReadOnlyList<EngineEntry> entries)
        {
            if (entries.Count > MaxCheatEntries)
                throw new InvalidOperationException(
                    $"Too many engine rows ({entries.Count}). Maximum is {MaxCheatEntries}.");

            var table = new byte[(entries.Count + 1) * EntrySize];
            for (int i = 0; i < entries.Count; i++)
            {
                int o = i * EntrySize;
                BitConverter.GetBytes(entries[i].Control).CopyTo(table, o);
                BitConverter.GetBytes(entries[i].Address).CopyTo(table, o + 4);
                BitConverter.GetBytes(entries[i].Value).CopyTo(table, o + 8);
                BitConverter.GetBytes(entries[i].Extra).CopyTo(table, o + 12);
            }
            // End row is all zeroes (control word 0 = end).
            return table;
        }

        /// <summary>
        /// Throw when the instruction pair at the hook point cannot be relocated into
        /// the engine (branches, linking jumps, or an existing engine hook).
        /// </summary>
        public static void ValidateHook(uint hookAddress, uint word0, uint word1, uint engineBase)
        {
            if (word0 == EncodeJ(engineBase))
                throw new InvalidOperationException(
                    $"0x{hookAddress:X8} already jumps to the cheat engine. Remove it first.");

            const uint JrRa = 0x03E00008;
            if (word0 == JrRa)
            {
                if (IsControlTransfer(word1))
                    throw new InvalidOperationException(
                        $"The delay slot of jr ra at 0x{hookAddress:X8} is itself a branch/jump; pick another hook.");
                return;
            }
            if (IsControlTransfer(word0) || IsControlTransfer(word1))
                throw new InvalidOperationException(
                    $"The instructions at 0x{hookAddress:X8} (0x{word0:X8} 0x{word1:X8}) include a branch or jump " +
                    "that cannot run from inside the engine. Hook two plain instructions, or a 'jr ra' + delay slot.");
        }

        /// <summary>
        /// Generate the full CDS source for the engine with a specific table embedded
        /// (as hexcode rows). Useful for inspection or manual compilation.
        /// </summary>
        public string GenerateSource(string cheats)
        {
            var entries = Translate(cheats);
            var sb = new StringBuilder(GetEngineSource(EngineBase));
            sb.AppendLine();
            sb.AppendLine($"address ${(EngineBase + CheatTableOffset):X8}");
            sb.AppendLine("scope GeneratedCheatTable {");
            foreach (var e in entries)
                sb.AppendLine($"    hexcode ${e.Control:X8}\n    hexcode ${e.Address:X8}\n    hexcode ${e.Value:X8}\n    hexcode ${e.Extra:X8}");
            sb.AppendLine("    hexcode $00000000\n    hexcode $00000000\n    hexcode $00000000\n    hexcode $00000000");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>Compile CDS source into the raw code text the translator consumes.</summary>
        public static string CompileCdsToCodeText(string cdsSource)
        {
            var parser = new CdsParser();
            var items  = parser.Parse(cdsSource ?? string.Empty);
            var errors = parser.Diagnostics.Where(d => d.Severity == CdsDiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "CDS errors: " + string.Join("; ", errors.Select(d => $"line {d.LineNumber}: {d.Message}")));
            return string.Join("\n", CdsCompiler.CompileToCodes(items).Select(c => c.ToString()));
        }

        // ────────────────────────────────────────────────────────────────────
        //  Internal helpers
        // ────────────────────────────────────────────────────────────────────

        private List<EngineEntry> Translate(string cheats)
        {
            var translation = RawCodeTranslator.Translate(cheats, _memoryBase);
            if (!translation.IsValid)
                throw new InvalidOperationException("Cheat codes not supported by the engine: " + string.Join("; ", translation.Errors));
            LastWarnings   = translation.Warnings;
            LastEntryCount = translation.Entries.Count;
            return translation.Entries;
        }

        private static string GetEngineSource(uint engineBase)
        {
            uint table = engineBase + CheatTableOffset;
            return GetCdsTemplate()
                .Replace("{{BASE}}",     engineBase.ToString("X8"))
                .Replace("{{DATA}}",     (engineBase + DataOffset).ToString("X8"))
                .Replace("{{TABLE_HI}}", (table >> 16).ToString("X4"))
                .Replace("{{TABLE_LO}}", (table & 0xFFFF).ToString("X4"));
        }

        private static List<(uint Address, uint Word)> CompileEngine(uint engineBase)
        {
            var parser = new CdsParser();
            var items  = parser.Parse(GetEngineSource(engineBase));
            var errors = parser.Diagnostics.Where(d => d.Severity == CdsDiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "Engine template failed to compile: " + string.Join("; ", errors.Select(d => $"line {d.LineNumber}: {d.Message}")));
            return CdsCompiler.Compile(items);
        }

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

        /// <summary>True for branches, jumps and ERET — anything that changes the PC.</summary>
        private static bool IsControlTransfer(uint word)
        {
            uint op = word >> 26;
            if (op is 0x02 or 0x03) return true;                          // j / jal
            if (op is >= 0x04 and <= 0x07) return true;                   // beq bne blez bgtz
            if (op is >= 0x14 and <= 0x17) return true;                   // beql bnel blezl bgtzl
            if (op == 0x01) return true;                                  // REGIMM (bltz, bgez, bal …)
            if (op == 0x00 && (word & 0x3F) is 0x08 or 0x09) return true; // jr / jalr
            if (op is >= 0x10 and <= 0x12 && ((word >> 21) & 0x1F) == 0x08) return true; // bc0/bc1/bc2
            if (word == 0x42000018) return true;                          // eret
            return false;
        }

        /// <summary>Encode a MIPS <c>j target</c> instruction.</summary>
        public static uint EncodeJ(uint targetAddress)
            => 0x08000000 | ((targetAddress >> 2) & 0x03FFFFFF);

        // ── Memory I/O wrappers ─────────────────────────────────────────────

        private void WriteWord(uint address, uint word)
            => _byteEditor.Write((int)address, BitConverter.GetBytes(word));

        private uint ReadWord(uint address)
            => BitConverter.ToUInt32(_byteEditor.Read((int)address, 4), 0);
    }
}
