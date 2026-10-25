using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>One 16-byte row of the <see cref="CdsCheatEngine"/> table (see cheatengine.cds).</summary>
    /// <param name="Control">Opcode in bits 7..0 plus op-specific fields (count, size, compare, stride).</param>
    /// <param name="Address">Full virtual address the row reads or writes.</param>
    /// <param name="Value">Value / compare operand / timer interval.</param>
    /// <param name="Extra">Op-specific extra operand (serial value increment).</param>
    public readonly record struct EngineEntry(uint Control, uint Address, uint Value, uint Extra);

    /// <summary>Engine table opcodes (low byte of <see cref="EngineEntry.Control"/>).</summary>
    public static class EngineOp
    {
        public const uint End      = 0x00;
        public const uint Write8   = 0x01;
        public const uint Write16  = 0x02;
        public const uint Write32  = 0x03;
        public const uint Add8     = 0x04;
        public const uint Add16    = 0x05;
        public const uint Add32    = 0x06;
        /// <summary>Control = op | strideWords&lt;&lt;8 | count&lt;&lt;16; Extra = value increment.</summary>
        public const uint Serial32 = 0x07;
        /// <summary>Control = op | compare&lt;&lt;8 | size&lt;&lt;12 | skip&lt;&lt;16. On a false compare, skip rows.</summary>
        public const uint Cond     = 0x08;
        /// <summary>Control = op | skip&lt;&lt;16; Value = interval in frames. Off-frames skip rows.</summary>
        public const uint Timer    = 0x09;

        public const uint CmpEqual   = 0;
        public const uint CmpNotEq   = 1;
        public const uint CmpLess    = 2;
        public const uint CmpGreater = 3;
    }

    /// <summary>Result of <see cref="RawCodeTranslator.Translate"/>.</summary>
    public sealed class RawCodeTranslation
    {
        public List<EngineEntry> Entries { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Translates raw (unencrypted) PS2 cheat codes into <see cref="CdsCheatEngine"/>
    /// table rows. Supported code types:
    /// <list type="table">
    ///   <item><term>0/1/2</term><description>8/16/32-bit constant write</description></item>
    ///   <item><term>3</term><description>8/16/32-bit increment / decrement</description></item>
    ///   <item><term>4</term><description>32-bit serial (slide) write</description></item>
    ///   <item><term>B</term><description>CodeDesigner timer: <c>Bnnnnnnn IIIIIIII</c> runs the next n lines every I frames</description></item>
    ///   <item><term>C</term><description>32-bit equal: when false, skip the rest of the cheat</description></item>
    ///   <item><term>D</term><description>16-bit compare, guards the next line</description></item>
    ///   <item><term>E</term><description>8/16-bit compare, guards the next nn lines</description></item>
    ///   <item><term>9/F</term><description>hook / master codes — ignored (the engine has its own hook)</description></item>
    /// </list>
    /// A cheat is a run of code lines; a non-code line (a name) or a blank line starts the next cheat.
    /// </summary>
    public static class RawCodeTranslator
    {
        private static readonly Regex CodeLine =
            new(@"^\s*([0-9a-f]{8})\s+([0-9a-f]{8})\s*(?://.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const uint AddressMask = 0x01FFFFFF;

        private sealed record Line(uint A, uint B, int Number);

        /// <param name="text">Cheat list text: names, blank lines and raw code pairs.</param>
        /// <param name="memoryBase">
        /// OR'd onto every 25-bit code address. 0x20000000 (the uncached EE RAM mirror games
        /// map through the TLB) matches what one-shot APPLYCHEATS writes to.
        /// </param>
        public static RawCodeTranslation Translate(string? text, uint memoryBase = 0x20000000)
        {
            var result = new RawCodeTranslation();
            foreach (var cheat in SplitCheats(text ?? string.Empty))
                TranslateCheat(cheat, memoryBase, result);
            return result;
        }

        private static List<List<Line>> SplitCheats(string text)
        {
            var cheats = new List<List<Line>>();
            var current = new List<Line>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = CodeLine.Match(lines[i]);
                if (m.Success)
                {
                    current.Add(new Line(Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt32(m.Groups[2].Value, 16), i + 1));
                    continue;
                }
                if (current.Count > 0) { cheats.Add(current); current = new List<Line>(); }
            }
            if (current.Count > 0) cheats.Add(current);
            return cheats;
        }

        /// <summary>A logical code: one or more lines that translate to one or more rows.</summary>
        private sealed class Code
        {
            public int FirstLine;
            public int LineCount;
            public List<EngineEntry> Rows = new();
            /// <summary>Guard: compare row that skips the rows of the following <see cref="GuardLines"/> lines (or the rest when -1).</summary>
            public int GuardLines;
            public bool IsGuard;
        }

        private static void TranslateCheat(List<Line> lines, uint memoryBase, RawCodeTranslation result)
        {
            var codes = new List<Code>();
            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                var code = new Code { FirstLine = i, LineCount = 1 };
                uint type = line.A >> 28;
                uint addr = (line.A & AddressMask) | memoryBase;
                switch (type)
                {
                    case 0x0: code.Rows.Add(new EngineEntry(EngineOp.Write8,  addr, line.B & 0xFF,   0)); break;
                    case 0x1: code.Rows.Add(new EngineEntry(EngineOp.Write16, addr, line.B & 0xFFFF, 0)); break;
                    case 0x2: code.Rows.Add(new EngineEntry(EngineOp.Write32, addr, line.B,          0)); break;

                    case 0x3:
                    {
                        // 30S0VVVV 0AAAAAAA  (S: 0/1 = +/- 8-bit, 2/3 = +/- 16-bit, 4/5 = +/- 32-bit + value line)
                        uint sub = (line.A >> 20) & 0xF;
                        uint target = (line.B & AddressMask) | memoryBase;
                        uint delta;
                        uint op;
                        switch (sub)
                        {
                            case 0: case 1: op = EngineOp.Add8;  delta = line.A & 0xFF;   break;
                            case 2: case 3: op = EngineOp.Add16; delta = line.A & 0xFFFF; break;
                            case 4: case 5:
                                if (i + 1 >= lines.Count) { result.Errors.Add($"line {line.Number}: 32-bit increment needs a value line after it"); i++; continue; }
                                op = EngineOp.Add32; delta = lines[i + 1].A; code.LineCount = 2; break;
                            default:
                                result.Errors.Add($"line {line.Number}: unknown type-3 increment form {line.A:X8}"); i++; continue;
                        }
                        if ((sub & 1) == 1) delta = unchecked(0u - delta);
                        code.Rows.Add(new EngineEntry(op, target, delta, 0));
                        break;
                    }

                    case 0x4:
                    {
                        // 4AAAAAAA NNNNSSSS / VVVVVVVV IIIIIIII
                        if (i + 1 >= lines.Count) { result.Errors.Add($"line {line.Number}: serial write needs a second line"); i++; continue; }
                        uint count = line.B >> 16, stride = line.B & 0xFFFF;
                        if (stride > 0xFF) { result.Errors.Add($"line {line.Number}: serial stride {stride} words exceeds the engine limit of 255"); i += 2; continue; }
                        code.LineCount = 2;
                        code.Rows.Add(new EngineEntry(EngineOp.Serial32 | (stride << 8) | (count << 16), addr, lines[i + 1].A, lines[i + 1].B));
                        break;
                    }

                    case 0xB:
                        // Bnnnnnnn IIIIIIII — CodeDesigner timer over the next n lines
                        code.IsGuard = true;
                        code.GuardLines = (int)(line.A & 0x0FFFFFFF);
                        code.Rows.Add(new EngineEntry(EngineOp.Timer, 0, line.B, 0));
                        break;

                    case 0xC:
                        // CAAAAAAA VVVVVVVV — 32-bit equal; false skips the rest of the cheat
                        code.IsGuard = true;
                        code.GuardLines = -1;
                        code.Rows.Add(new EngineEntry(EngineOp.Cond | (EngineOp.CmpEqual << 8) | (4u << 12), addr, line.B, 0));
                        break;

                    case 0xD:
                    {
                        // DAAAAAAA 00T0VVVV — 16-bit compare guarding the next line
                        uint cmp = (line.B >> 20) & 0xF;
                        if (cmp > EngineOp.CmpGreater) { result.Errors.Add($"line {line.Number}: compare type {cmp} (bitwise) is not supported by the engine yet"); i++; continue; }
                        code.IsGuard = true;
                        code.GuardLines = 1;
                        code.Rows.Add(new EngineEntry(EngineOp.Cond | (cmp << 8) | (2u << 12), addr, line.B & 0xFFFF, 0));
                        break;
                    }

                    case 0xE:
                    {
                        // E0NNVVVV TAAAAAAA (16-bit) / E1NN00VV TAAAAAAA (8-bit) — guards the next NN lines
                        uint size = ((line.A >> 24) & 0xF) == 1 ? 1u : 2u;
                        uint cmp = line.B >> 28;
                        if (cmp > EngineOp.CmpGreater) { result.Errors.Add($"line {line.Number}: compare type {cmp} (bitwise) is not supported by the engine yet"); i++; continue; }
                        uint value = size == 1 ? line.A & 0xFF : line.A & 0xFFFF;
                        code.IsGuard = true;
                        code.GuardLines = (int)((line.A >> 16) & 0xFF);
                        code.Rows.Add(new EngineEntry(EngineOp.Cond | (cmp << 8) | (size << 12), (line.B & AddressMask) | memoryBase, value, 0));
                        break;
                    }

                    case 0x9:
                    case 0xF:
                        result.Warnings.Add($"line {line.Number}: hook/master code {line.A:X8} ignored — the engine is hooked separately");
                        break;

                    default:
                        result.Errors.Add($"line {line.Number}: code type {type:X} ({line.A:X8}) is not supported by the engine yet");
                        break;
                }
                codes.Add(code);
                i += code.LineCount;
            }

            // Resolve guard skip counts: rows covered by the next N lines (rounded to whole codes).
            for (int c = 0; c < codes.Count; c++)
            {
                var code = codes[c];
                if (!code.IsGuard || code.Rows.Count == 0) continue;
                int lastLine = code.GuardLines < 0 ? int.MaxValue : code.FirstLine + code.LineCount - 1 + code.GuardLines;
                int skipRows = 0;
                for (int n = c + 1; n < codes.Count && codes[n].FirstLine <= lastLine; n++)
                    skipRows += codes[n].Rows.Count;
                if (skipRows > 0xFFFF)
                {
                    result.Errors.Add($"line {lines[code.FirstLine].Number}: guards {skipRows} engine rows; the limit is 65535");
                    continue;
                }
                var row = code.Rows[0];
                code.Rows[0] = row with { Control = row.Control | ((uint)skipRows << 16) };
            }

            foreach (var code in codes)
                result.Entries.AddRange(code.Rows);
        }
    }
}
