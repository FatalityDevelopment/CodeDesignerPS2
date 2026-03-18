using System;
using System.Collections.Generic;
using System.Text;

namespace CodeDesigner.Ps2.Recompiler
{
    /// <summary>
    /// Represents a single function that will be (or has been) statically recompiled.
    /// </summary>
    public sealed class RecompFunction
    {
        /// <summary>
        /// C-safe name used as the C++ function identifier.
        /// Derived from the ELF symbol name when available, otherwise
        /// synthesised as <c>func_XXXXXXXX</c>.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Virtual start address of the function.</summary>
        public uint StartAddress { get; init; }

        /// <summary>
        /// Exclusive end address (i.e., the first byte beyond the function).
        /// A value of 0 means the size was not known at construction time.
        /// </summary>
        public uint EndAddress { get; init; }

        /// <summary>
        /// Decoded instruction words in order, paired with their virtual addresses.
        /// </summary>
        public IReadOnlyList<(uint address, uint word)> Instructions { get; init; } =
            Array.Empty<(uint, uint)>();

        /// <summary>Number of instruction words in the function.</summary>
        public int InstructionCount => Instructions.Count;

        /// <summary>
        /// Indicates that this function should be treated as an external stub:
        /// the recompiler emits a thin wrapper that calls a runtime handler
        /// instead of recompiling the function body.
        /// </summary>
        public bool IsStub { get; init; }

        /// <summary>
        /// The runtime handler name for a stub function (e.g. <c>sceCdRead</c>).
        /// Only meaningful when <see cref="IsStub"/> is <c>true</c>.
        /// </summary>
        public string StubHandlerName { get; init; } = string.Empty;

        /// <summary>
        /// Indicates that this function should be skipped (emits a TODO wrapper).
        /// </summary>
        public bool IsSkipped { get; init; }

        public override string ToString() =>
            $"{Name} @ 0x{StartAddress:X8} ({InstructionCount} instructions)";

        // ── Factory helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Build a C-safe function name from an ELF symbol name.
        /// Replaces characters that are invalid in a C++ identifier with '_'.
        /// </summary>
        public static string MakeCppName(string symbolName, uint address)
        {
            if (string.IsNullOrWhiteSpace(symbolName))
                return $"func_{address:X8}";

            var sb = new StringBuilder(symbolName.Length + 1);
            foreach (char c in symbolName)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            // C++ identifier must not start with a digit
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');

            return sb.Length > 0 ? sb.ToString() : $"func_{address:X8}";
        }
    }
}
