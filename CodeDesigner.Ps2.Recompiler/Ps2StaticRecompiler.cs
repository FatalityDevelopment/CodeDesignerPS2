using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeDesigner.Ps2.Recompiler
{
    /// <summary>
    /// Orchestrates static recompilation of a PS2 ELF binary to C++ source files.
    ///
    /// Workflow:
    /// <list type="number">
    ///   <item>Parse the ELF with <see cref="Ps2ElfParser"/> to obtain sections and symbols.</item>
    ///   <item>Build a function map from symbol info and optional Ghidra CSV.</item>
    ///   <item>For each function, extract instruction words from the code section data.</item>
    ///   <item>Translate instructions to C++ using <see cref="InstructionTranslator"/>.</item>
    ///   <item>Write the generated C++ files and the runtime header.</item>
    /// </list>
    /// </summary>
    public sealed class Ps2StaticRecompiler
    {
        private readonly RecompilerConfig _cfg;

        public Ps2StaticRecompiler(RecompilerConfig config)
        {
            _cfg = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Run the full recompilation pipeline.
        /// </summary>
        /// <param name="elfData">Raw bytes of the PS2 ELF file.</param>
        /// <param name="progress">Optional progress callback (message string).</param>
        /// <returns>A <see cref="RecompilerResult"/> describing what was generated.</returns>
        public RecompilerResult Recompile(ReadOnlySpan<byte> elfData, Action<string>? progress = null)
        {
            progress?.Invoke("Parsing ELF…");
            var elf = Ps2ElfParser.Parse(elfData);

            progress?.Invoke($"Entry point: 0x{elf.EntryPoint:X8}");
            progress?.Invoke($"Sections: {elf.Sections.Count}  Symbols: {elf.Symbols.Count}");

            // ── Build function map ─────────────────────────────────────────────
            var functions = BuildFunctionList(elf, progress);
            progress?.Invoke($"Functions to recompile: {functions.Count}");

            // ── Prepare output directory ───────────────────────────────────────
            Directory.CreateDirectory(_cfg.OutputDirectory);

            // ── Emit runtime header ────────────────────────────────────────────
            string headerPath = Path.Combine(_cfg.OutputDirectory, "ps2_recomp_runtime.h");
            File.WriteAllText(headerPath, Ps2RuntimeTemplate.HeaderContent, Encoding.UTF8);
            progress?.Invoke($"Wrote {headerPath}");

            // ── Emit C++ source(s) ─────────────────────────────────────────────
            var writtenFiles = new List<string> { headerPath };

            if (_cfg.SingleFileOutput)
            {
                string outPath = Path.Combine(_cfg.OutputDirectory, "recomp_output.cpp");
                using var writer = new StreamWriter(outPath, false, Encoding.UTF8);
                EmitFileHeader(writer);
                foreach (var fn in functions)
                {
                    EmitFunction(writer, fn);
                    progress?.Invoke($"  recompiled {fn.Name}");
                }
                EmitDispatchTable(writer, functions);
                writtenFiles.Add(outPath);
                progress?.Invoke($"Wrote {outPath}");
            }
            else
            {
                foreach (var fn in functions)
                {
                    string outPath = Path.Combine(_cfg.OutputDirectory, $"func_{fn.StartAddress:X8}.cpp");
                    using var writer = new StreamWriter(outPath, false, Encoding.UTF8);
                    EmitFileHeader(writer);
                    EmitFunction(writer, fn);
                    writtenFiles.Add(outPath);
                    progress?.Invoke($"  wrote {outPath}");
                }

                // Dispatch table in its own file
                string dispatchPath = Path.Combine(_cfg.OutputDirectory, "dispatch.cpp");
                using var dw = new StreamWriter(dispatchPath, false, Encoding.UTF8);
                EmitFileHeader(dw);
                EmitDispatchTable(dw, functions);
                writtenFiles.Add(dispatchPath);
            }

            progress?.Invoke("Done.");
            return new RecompilerResult
            {
                Functions    = functions,
                WrittenFiles = writtenFiles,
            };
        }

        // ── Function discovery ─────────────────────────────────────────────────

        private List<RecompFunction> BuildFunctionList(ElfInfo elf, Action<string>? progress)
        {
            // Build a map of executable memory: address → instruction word
            var codeMap = BuildCodeMap(elf);
            if (codeMap.Count == 0)
            {
                progress?.Invoke("Warning: no executable code found in ELF.");
                return new List<RecompFunction>();
            }

            uint codeStart = uint.MaxValue;
            uint codeEnd   = 0;
            foreach (var addr in codeMap.Keys)
            {
                if (addr < codeStart) codeStart = addr;
                if (addr + 4 > codeEnd) codeEnd = addr + 4;
            }

            // Stub / skip name sets
            var stubNames        = _cfg.GetStubNames();
            var skipNames        = new HashSet<string>(_cfg.Skips, StringComparer.Ordinal);
            var stubAddrBindings = _cfg.ParseStubAddressBindings();

            // Ghidra function map (address → name)
            var ghidraMap = LoadGhidraMap(_cfg.GhidraFunctionMap);

            // Collect function entry points from ELF symbols
            var funcEntries = new SortedDictionary<uint, string>(); // address → name

            // Always include the ELF entry point
            funcEntries[elf.EntryPoint] = ghidraMap.TryGetValue(elf.EntryPoint, out var epName) ? epName : "entry";

            foreach (var sym in elf.FunctionSymbols())
            {
                if (!codeMap.ContainsKey(sym.Address)) continue;
                string name = ghidraMap.TryGetValue(sym.Address, out var gn) ? gn : sym.Name;
                funcEntries[sym.Address] = name;
            }

            // Merge any Ghidra-only entries
            foreach (var (addr, name) in ghidraMap)
            {
                if (!funcEntries.ContainsKey(addr) && codeMap.ContainsKey(addr))
                    funcEntries[addr] = name;
            }

            // Merge stub address bindings
            foreach (var (addr, handler) in stubAddrBindings)
            {
                if (!funcEntries.ContainsKey(addr))
                    funcEntries[addr] = handler;
            }

            // Build function list with instruction extraction
            var functions = new List<RecompFunction>();
            var sortedAddrs = new List<uint>(funcEntries.Keys);

            for (int i = 0; i < sortedAddrs.Count; i++)
            {
                uint start    = sortedAddrs[i];
                uint end      = (i + 1 < sortedAddrs.Count) ? sortedAddrs[i + 1] : codeEnd;
                string symName = funcEntries[start];

                // Apply patches: try ELF symbol size
                var matchingSym = FindSymbol(elf, start);
                if (matchingSym != null && matchingSym.Size > 0)
                    end = start + matchingSym.Size;

                string cppName = RecompFunction.MakeCppName(symName, start);

                // Collect instruction words
                var instrs = new List<(uint address, uint word)>();
                for (uint addr = start; addr < end && codeMap.ContainsKey(addr); addr += 4)
                {
                    uint word = codeMap[addr];
                    // Apply per-address patches
                    if (_cfg.InstructionPatches.TryGetValue(addr, out uint patched))
                        word = patched;
                    instrs.Add((addr, word));
                }

                if (instrs.Count == 0) continue;

                bool isStub    = stubNames.Contains(symName) || stubAddrBindings.ContainsKey(start);
                bool isSkipped = skipNames.Contains(symName);
                string stubHandler = stubAddrBindings.TryGetValue(start, out var sh) ? sh : symName;

                functions.Add(new RecompFunction
                {
                    Name           = cppName,
                    StartAddress   = start,
                    EndAddress     = end,
                    Instructions   = instrs,
                    IsStub         = isStub,
                    IsSkipped      = isSkipped,
                    StubHandlerName = stubHandler,
                });
            }

            return functions;
        }

        /// <summary>
        /// Build a flat address → instruction-word map from all executable sections.
        /// </summary>
        private static SortedDictionary<uint, uint> BuildCodeMap(ElfInfo elf)
        {
            var map = new SortedDictionary<uint, uint>();
            foreach (var section in elf.ExecutableSections())
            {
                if (section.Data.Length < 4) continue;
                uint baseAddr = section.VirtualAddress;
                for (int off = 0; off + 3 < section.Data.Length; off += 4)
                {
                    uint word = (uint)(
                        section.Data[off    ]       |
                        section.Data[off + 1] << 8  |
                        section.Data[off + 2] << 16 |
                        section.Data[off + 3] << 24);
                    map[baseAddr + (uint)off] = word;
                }
            }
            return map;
        }

        private static ElfSymbol? FindSymbol(ElfInfo elf, uint address)
        {
            foreach (var sym in elf.Symbols)
                if (sym.Address == address && sym.Type == ElfSymbolType.Func)
                    return sym;
            return null;
        }

        // ── Ghidra CSV loader ──────────────────────────────────────────────────

        private static Dictionary<uint, string> LoadGhidraMap(string? path)
        {
            var map = new Dictionary<uint, string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return map;

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                int comma = line.IndexOf(',');
                if (comma < 0) continue;

                string addrStr = line[..comma].Trim();
                string name    = line[(comma + 1)..].Trim();

                if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrStr = addrStr[2..];

                if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    map[addr] = name;
            }
            return map;
        }

        // ── Code emission ──────────────────────────────────────────────────────

        private static void EmitFileHeader(StreamWriter w)
        {
            w.WriteLine("// Auto-generated by CodeDesigner.Ps2.Recompiler — do not edit.");
            w.WriteLine("#include \"ps2_recomp_runtime.h\"");
            w.WriteLine();
        }

        private void EmitFunction(StreamWriter w, RecompFunction fn)
        {
            w.WriteLine($"// {fn.Name} @ 0x{fn.StartAddress:X8}  ({fn.InstructionCount} instructions)");

            if (fn.IsSkipped)
            {
                w.WriteLine($"void {fn.Name}(Ps2Context* ctx, Ps2Runtime* runtime) {{");
                w.WriteLine($"    /* TODO: {fn.Name} is marked as skipped */");
                w.WriteLine($"    runtime->unimplemented(0x{fn.StartAddress:X8}U, ctx);");
                w.WriteLine("}");
                w.WriteLine();
                return;
            }

            if (fn.IsStub)
            {
                w.WriteLine($"void {fn.Name}(Ps2Context* ctx, Ps2Runtime* runtime) {{");
                w.WriteLine($"    ps2_stubs::{fn.StubHandlerName}(ctx, runtime);");
                w.WriteLine("}");
                w.WriteLine();
                return;
            }

            // ── Full recompilation ─────────────────────────────────────────────
            var labels = InstructionTranslator.CollectLocalLabels(fn.Instructions);

            w.WriteLine($"void {fn.Name}(Ps2Context* ctx, Ps2Runtime* runtime) {{");

            // Emit addresses that are targets of intra-function branches as a
            // declare-use pattern: we mark visited to avoid re-emitting delay slots.
            var delaySlots = CollectDelaySlotAddresses(fn.Instructions);

            for (int i = 0; i < fn.Instructions.Count; i++)
            {
                var (addr, word) = fn.Instructions[i];

                // Emit goto label if this address is a branch target
                if (labels.Contains(addr))
                    w.WriteLine($"  {LabelName(addr)}:;");

                // Skip if this instruction is a delay slot that will be emitted
                // inline by the preceding branch (it will be re-processed there).
                if (delaySlots.Contains(addr))
                    continue;

                // Fetch delay slot word (next instruction, 0 if this is the last)
                uint dsWord = (i + 1 < fn.Instructions.Count) ? fn.Instructions[i + 1].word : 0u;

                w.Write($"  /* 0x{addr:X8} */ ");

                var lines = InstructionTranslator.Translate(word, addr, dsWord, labels, _cfg);
                if (lines.Count == 1)
                {
                    w.WriteLine(lines[0]);
                }
                else
                {
                    w.WriteLine();
                    foreach (var line in lines)
                        w.WriteLine($"  {line}");
                }
            }

            w.WriteLine("}");
            w.WriteLine();
        }

        /// <summary>
        /// Collect the addresses of instructions that are delay slots for a branch/jump.
        /// These are emitted inline by the preceding translation and must be skipped
        /// in the main loop to avoid double-emission.
        /// </summary>
        private static HashSet<uint> CollectDelaySlotAddresses(
            IReadOnlyList<(uint address, uint word)> instrs)
        {
            var ds = new HashSet<uint>();
            for (int i = 0; i + 1 < instrs.Count; i++)
            {
                uint word   = instrs[i].word;
                int  opcode = (int)((word >> 26) & 0x3F);
                int  funct  = (int)(word & 0x3F);

                bool isBranchOrJump =
                    opcode == 0x02 || opcode == 0x03 ||                         // J / JAL
                    (opcode == 0x00 && (funct == 0x08 || funct == 0x09)) ||    // JR / JALR
                    opcode == 0x01 ||                                            // REGIMM branches
                    (opcode >= 0x04 && opcode <= 0x07) ||                       // BEQ/BNE/BLEZ/BGTZ
                    (opcode >= 0x14 && opcode <= 0x17) ||                       // BEQL/BNEL/BLEZL/BGTZL
                    (opcode == 0x11 && ((word >> 21) & 0x1F) == 0x08);         // COP1 BC1*

                if (isBranchOrJump)
                    ds.Add(instrs[i + 1].address);
            }
            return ds;
        }

        private static void EmitDispatchTable(StreamWriter w, List<RecompFunction> functions)
        {
            w.WriteLine("// Dispatch table: maps virtual addresses to recompiled function pointers.");
            w.WriteLine("extern \"C\" Ps2FuncPtr ps2_lookup_function(uint32_t addr) {");
            w.WriteLine("    switch (addr) {");
            foreach (var fn in functions)
            {
                if (!fn.IsSkipped)
                    w.WriteLine($"        case 0x{fn.StartAddress:X8}U: return &{fn.Name};");
            }
            w.WriteLine("        default: return nullptr;");
            w.WriteLine("    }");
            w.WriteLine("}");
            w.WriteLine();
        }

        private static string LabelName(uint addr) => $"label_{addr:X8}";
    }

    /// <summary>
    /// Result returned by <see cref="Ps2StaticRecompiler.Recompile"/>.
    /// </summary>
    public sealed class RecompilerResult
    {
        /// <summary>All functions that were processed.</summary>
        public List<RecompFunction> Functions { get; init; } = new();

        /// <summary>Paths of all files written to disk.</summary>
        public List<string> WrittenFiles { get; init; } = new();
    }
}
