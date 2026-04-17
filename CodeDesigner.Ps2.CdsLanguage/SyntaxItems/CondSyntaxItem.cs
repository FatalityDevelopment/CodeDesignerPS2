using System.Collections.Generic;

namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>cond</c> / <c>endcond</c> block that guards a set of freeze entries
    /// with a memory-equality test — mapped to the cheat engine's conditional type 0xC0.
    ///
    /// <para>Syntax:</para>
    /// <code>
    /// cond mem[$20347A50] == 0x0001
    ///   freeze word $20347A54 = 0xFF
    /// endcond
    /// </code>
    ///
    /// <para>
    /// The engine only applies the <see cref="Body"/> freeze entries when the 32-bit value
    /// at <see cref="Address"/> equals <see cref="ConditionValue"/>.
    /// </para>
    ///
    /// <para>
    /// Like <see cref="FreezeSyntaxItem"/>, this is a <em>sidecar</em> item.
    /// Use <see cref="CdsCompiler.GetCheatTableEntries"/> to flatten the tree.
    /// </para>
    /// </summary>
    public sealed class CondSyntaxItem : CdsSyntaxItem
    {
        /// <summary>
        /// Condition cheat type: <see cref="CdsFreezeType.Condition32"/> (0xC0).
        /// </summary>
        public CdsFreezeType FreezeType { get; init; } = CdsFreezeType.Condition32;

        /// <summary>PS2 virtual address whose value is compared.</summary>
        public uint Address { get; init; }

        /// <summary>Value that <see cref="Address"/> must equal for the body to execute.</summary>
        public uint ConditionValue { get; init; }

        /// <summary>
        /// Freeze (and nested cond) entries to apply when the condition is true.
        /// </summary>
        public List<CdsSyntaxItem> Body { get; init; } = new();
    }
}
