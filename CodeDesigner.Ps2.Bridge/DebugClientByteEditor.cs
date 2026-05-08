using System;
using CodeDesigner.Ps2.CheatEngine;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// <see cref="IByteEditor"/> backed by any <see cref="IDebugClient"/>.
    /// Replaces <c>PinePcsx2ByteEditor</c> — works equally well for PCSX2 PINE
    /// and the built-in emulator's InternalPineSocket since both implement
    /// <see cref="IDebugClient"/>.
    /// <para>
    /// Used by the cheat engine so APPLYCHEATS can write patches directly
    /// into guest memory regardless of which runtime is active.
    /// </para>
    /// </summary>
    public sealed class DebugClientByteEditor : IByteEditor
    {
        private readonly IDebugClient _client;

        public DebugClientByteEditor(IDebugClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public byte[] Read(int address, int byteLength)
        {
            if (byteLength <= 0) return Array.Empty<byte>();
            return _client.ReadBytes(TranslateEeAddress((uint)address), byteLength);
        }

        public byte[] Read(string addressHex, int byteLength)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            return Read((int)addr, byteLength);
        }

        public void Write(int address, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            _client.WriteBytes(TranslateEeAddress((uint)address), data);
        }

        public void Write(string addressHex, byte[] data)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            Write((int)addr, data);
        }

        // ── Address translation ──────────────────────────────────────────────
        // The UI and cheat engine often send physical EE bases (0x00000000 for
        // EE RAM); both PCSX2 PINE and the built-in InternalPineSocket expect
        // EE virtual addresses. Map physical/uncached mirrors into KSEG0 and
        // leave already-virtual ranges intact.

        // EE scratchpad fixed VTLB page (4 KB).
        private const uint ScratchpadBase = 0x70000000u;
        private const uint ScratchpadSize = 0x00004000u;

        private static uint TranslateEeAddress(uint vaddr)
        {
            if (vaddr >= 0x1C000000u && vaddr < 0x1E000000u) return vaddr; // IOP mirror
            if (vaddr >= ScratchpadBase && vaddr < ScratchpadBase + ScratchpadSize) return vaddr;
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu) return vaddr; // KSEG0/1

            // Uncached user mirror → physical.
            if (vaddr >= 0x20000000u && vaddr < 0x22000000u)
                vaddr -= 0x20000000u;

            // Physical RAM/BIOS → KSEG0 cached alias.
            if (vaddr < 0x20000000u)
                return vaddr | 0x80000000u;

            return vaddr;
        }
    }
}
