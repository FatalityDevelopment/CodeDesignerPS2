using System;
using CodeDesigner.Mips64LE.Common.CheatEngine;
using CodeDesigner.Mips64LE.Common.Emulator;

namespace CodeDesigner.Mips64LE
{
    /// <summary>
    /// Implements <see cref="IByteEditor"/> backed by a live PCSX2 instance
    /// accessed via the PINE IPC socket.
    /// <para>
    /// Used by the cheat engine so that <c>APPLYCHEATS</c> can write patches
    /// directly into PCSX2's guest memory when the server is running in PINE
    /// bridge mode (i.e. <c>--pcsx2</c> flag on the command line) instead of
    /// the built-in emulator.
    /// </para>
    /// </summary>
    public sealed class PinePcsx2ByteEditor : IByteEditor
    {
        private readonly PineClient _pine;

        public PinePcsx2ByteEditor(PineClient pine)
        {
            _pine = pine ?? throw new ArgumentNullException(nameof(pine));
        }

        // ── IByteEditor ────────────────────────────────────────────────────────

        public byte[] Read(int address, int byteLength)
        {
            if (byteLength <= 0) return Array.Empty<byte>();
            return _pine.ReadBytes(TranslateEeAddress((uint)address), byteLength);
        }

        public byte[] Read(string addressHex, int byteLength)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            return Read((int)addr, byteLength);
        }

        public void Write(int address, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            _pine.WriteBytes(TranslateEeAddress((uint)address), data);
        }

        public void Write(string addressHex, byte[] data)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            Write((int)addr, data);
        }

        // ── Address translation ────────────────────────────────────────────────

        /// <summary>
        /// Translates an EE address into a PINE-friendly virtual address.
        /// The UI and cheat engine often use physical bases (0x00000000 for EE RAM),
        /// while PCSX2 PINE expects EE virtual addresses. Map physical/uncached
        /// mirrors into KSEG0, and leave already-virtual ranges intact.
        /// </summary>
        private static uint TranslateEeAddress(uint vaddr)
        {
            // Preserve IOP mirror and scratchpad (valid EE virtual ranges).
            if (vaddr >= 0x1C000000u && vaddr < 0x1E000000u)
                return vaddr;
            if (vaddr >= 0x70000000u &&
                vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                return vaddr;

            // Keep KSEG0/KSEG1 as-is.
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr;

            // Uncached user mirror → physical.
            if (vaddr >= 0x20000000u && vaddr < 0x22000000u)
                vaddr -= 0x20000000u;

            // Physical RAM/BIOS → KSEG0 cached alias for PINE.
            if (vaddr < 0x20000000u)
                return vaddr | 0x80000000u;

            return vaddr;
        }
    }
}
