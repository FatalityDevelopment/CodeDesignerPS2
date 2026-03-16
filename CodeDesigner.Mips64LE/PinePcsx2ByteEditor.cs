using System;
using CodeDesigner.Mips64LE.Common.CheatEngine;

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
        /// Translates a virtual EE address for PINE.
        /// PINE addresses the same virtual address space as the EE CPU (KSEG0/KSEG1
        /// are resolved server-side by PCSX2).  We only flatten the PS2 uncached
        /// user-mirror (0x20000000–0x21FFFFFF → physical 0x00000000–0x01FFFFFF) since
        /// PCSX2's PINE server may not map that window.
        /// </summary>
        private static uint TranslateEeAddress(uint vaddr)
        {
            if (vaddr >= 0x20000000u && vaddr < 0x22000000u)
                return vaddr - 0x20000000u;
            return vaddr;
        }
    }
}
