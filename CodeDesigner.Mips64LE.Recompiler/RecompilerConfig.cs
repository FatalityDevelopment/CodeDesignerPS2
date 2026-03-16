using System;
using System.Collections.Generic;

namespace CodeDesigner.Mips64LE.Recompiler
{
    /// <summary>
    /// Configuration for the <see cref="Ps2StaticRecompiler"/>.
    /// </summary>
    public sealed class RecompilerConfig
    {
        /// <summary>
        /// Path to the input PS2 ELF file.
        /// </summary>
        public string InputElf { get; set; } = string.Empty;

        /// <summary>
        /// Directory where generated C++ files will be written.
        /// The directory will be created if it does not exist.
        /// </summary>
        public string OutputDirectory { get; set; } = "recomp_out";

        /// <summary>
        /// When <c>true</c>, all functions are written into a single
        /// <c>recomp_output.cpp</c> file.  When <c>false</c>, each function
        /// gets its own file named <c>func_XXXXXXXX.cpp</c>.
        /// </summary>
        public bool SingleFileOutput { get; set; } = true;

        /// <summary>
        /// Names of functions to treat as external stubs.  The recompiler emits a
        /// thin wrapper that delegates to a runtime-provided handler instead of
        /// recompiling the function body.
        ///
        /// Entries may optionally include an address binding using the syntax
        /// <c>handlerName@0xADDRESS</c> to bind a stripped function at the given
        /// address to the named runtime stub.
        /// </summary>
        public List<string> Stubs { get; set; } = new();

        /// <summary>
        /// Names of functions to skip entirely.  The recompiler emits a
        /// <c>TODO</c> comment wrapper for each skipped function.
        /// </summary>
        public List<string> Skips { get; set; } = new();

        /// <summary>
        /// Per-address instruction patches: maps virtual address → replacement
        /// 32-bit instruction word.  Applied before translation.
        /// </summary>
        public Dictionary<uint, uint> InstructionPatches { get; set; } = new();

        /// <summary>
        /// When <c>true</c>, SYSCALL instructions delegate to
        /// <c>runtime->handleSyscall()</c> rather than inlining.
        /// </summary>
        public bool PatchSyscalls { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, COP0 instructions (MTC0/MFC0 etc.) are replaced
        /// with no-ops in the generated output.
        /// </summary>
        public bool PatchCop0 { get; set; } = false;

        /// <summary>
        /// When <c>true</c>, CACHE instructions are replaced with no-ops.
        /// </summary>
        public bool PatchCache { get; set; } = true;

        /// <summary>
        /// Optional path to a function-map CSV exported from Ghidra.
        /// Each line is expected to be: <c>address,name</c> (hex address without 0x prefix).
        /// </summary>
        public string? GhidraFunctionMap { get; set; }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parse address-binding entries from <see cref="Stubs"/> and return a
        /// dictionary mapping address → handler name.
        /// </summary>
        public Dictionary<uint, string> ParseStubAddressBindings()
        {
            var result = new Dictionary<uint, string>();
            foreach (var entry in Stubs)
            {
                int at = entry.IndexOf('@');
                if (at < 0) continue;

                string handlerName = entry[..at].Trim();
                string addrStr     = entry[(at + 1)..].Trim();
                if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrStr = addrStr[2..];

                if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    result[addr] = handlerName;
            }
            return result;
        }

        /// <summary>
        /// Returns the set of stub handler names (without address bindings).
        /// </summary>
        public HashSet<string> GetStubNames()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Stubs)
            {
                int at   = entry.IndexOf('@');
                string name = at >= 0 ? entry[..at].Trim() : entry.Trim();
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }
            return result;
        }
    }
}
