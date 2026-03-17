using System;

namespace CodeDesigner.Mips64LE.Common.Core
{
    /// <summary>
    /// Abstraction for a GIF data consumer that accepts packed GIF packets.
    /// Decouples the VU1 executor from the concrete GIF hardware implementation.
    /// </summary>
    public interface IGifDataSink
    {
        /// <summary>Feeds a sequence of 128-bit GIF words into the GIF unit.</summary>
        void Feed(ReadOnlySpan<uint> words);
    }
}
