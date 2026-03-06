using System;

namespace CodeDesigner.Mips64LE.Common.CPU.COP0
{
    /// <summary>
    /// Cache state and CACHE-instruction dispatch for the Emotion Engine R5900.
    ///
    /// The CACHE instruction (opcode 0x2F) uses the rt field directly as an operation
    /// code; the mapping below mirrors the cases in PCSX2 pcsx2/Cache.cpp exactly.
    ///
    /// I-cache:
    ///   The R5900 has a 16 KB, direct-mapped i-cache (64-byte lines, 256 sets).
    ///   I-cache invalidation is delegated to <see cref="ICacheInvalidateLine"/>
    ///   and <see cref="ICacheInvalidateAll"/> so the caller (PS2Emulator) can plug
    ///   in its own i-cache arrays without this class needing a memory-bus reference.
    ///
    /// D-cache:
    ///   The R5900 has an 8 KB, 2-way set-associative d-cache (64-byte lines,
    ///   64 sets).  Full d-cache emulation is not implemented here (matching PCSX2
    ///   interpreter behaviour); writeback/invalidate ops are accepted but are
    ///   effective no-ops for the data side.
    ///
    /// Reference: PCSX2 pcsx2/Cache.cpp (OpcodeImpl::CACHE), R5900 User's Manual.
    /// </summary>
    public sealed class CacheState
    {
        // ── R5900 CACHE instruction rt-field op codes (PCSX2 naming) ─────────
        // I-cache ops
        public const uint IXIN   = 0x07; // Index Invalidate I
        public const uint BFH    = 0x0C; // BTAC Flush (branch target address cache)
        // D-cache ops (index-based)
        public const uint DXLTG  = 0x10; // Index Load Tag D
        public const uint DXLDT  = 0x11; // Index Load Data D
        public const uint DXSTG  = 0x12; // Index Store Tag D
        public const uint DXSDT  = 0x13; // Index Store Data D
        public const uint DXWBIN = 0x14; // Index Writeback Invalidate D
        public const uint DXIN   = 0x16; // Index Invalidate D
        // D-cache ops (hit-based)
        public const uint DHWBIN = 0x18; // Hit Writeback Invalidate D
        public const uint DHIN   = 0x1A; // Hit Invalidate D
        public const uint DHWOIN = 0x1C; // Hit Writeback D (without invalidate)

        // ── I-cache invalidation callbacks ───────────────────────────────────
        // Wired by PS2Emulator to forward invalidations to its i-cache arrays.

        /// <summary>
        /// Called when a CACHE IXIN instruction targets a specific address.
        /// The argument is the virtual address computed by the CACHE instruction;
        /// the handler should invalidate the i-cache line that covers it.
        /// </summary>
        public Action<uint>? ICacheInvalidateLine { get; set; }

        /// <summary>
        /// Called when the entire i-cache should be flushed (e.g. reset).
        /// </summary>
        public Action? ICacheInvalidateAll { get; set; }

        /// <summary>
        /// Dispatch a CACHE instruction.
        /// </summary>
        /// <param name="op">The rt field from the CACHE encoding (operation code).</param>
        /// <param name="address">Effective address (base + sign-extended offset).</param>
        public void PerformOperation(uint op, uint address)
        {
            switch (op)
            {
                // ── Instruction cache ─────────────────────────────────────
                case IXIN:
                    // I-cache Index Invalidate: invalidate the i-cache line
                    // whose index is derived from bits [13:6] of the address.
                    ICacheInvalidateLine?.Invoke(address);
                    break;

                case BFH:
                    // BTAC Flush — we don't model the branch-target address cache.
                    // (PCSX2: "Not Implemented as we do not cache Branch Target Addresses")
                    break;

                // ── Data cache (index-based) ──────────────────────────────
                // These all target the d-cache by set/way index.  We don't implement
                // a full d-cache, so these are accepted without effect (matching
                // PCSX2's interpreter which commits d-cache writes directly to RAM).
                case DXLTG:
                case DXLDT:
                case DXSTG:
                case DXSDT:
                case DXWBIN:
                case DXIN:
                    break;

                // ── Data cache (hit-based) ────────────────────────────────
                case DHWBIN: // Hit Writeback Invalidate D
                case DHIN:   // Hit Invalidate D
                case DHWOIN: // Hit Writeback D
                    break;

                default:
                    // Unrecognised / reserved cache op — silently ignore.
                    break;
            }
        }
    }
}