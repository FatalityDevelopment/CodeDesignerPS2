using System.Collections.Generic;

namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>timer</c> / <c>endtimer</c> block that executes its body every
    /// <see cref="IntervalFrames"/> frames when consumed by <see cref="CodeDesigner.Ps2.CheatEngine.CdsCheatEngine"/>.
    /// </summary>
    public sealed class TimerSyntaxItem : CdsSyntaxItem
    {
        /// <summary>Cheat-table type: timer gate (0xB0).</summary>
        public CdsFreezeType FreezeType { get; init; } = CdsFreezeType.Timer;

        /// <summary>Frame interval for the gated body.</summary>
        public uint IntervalFrames { get; init; }

        /// <summary>Cheat entries guarded by the timer.</summary>
        public List<CdsSyntaxItem> Body { get; init; } = new();
    }
}
