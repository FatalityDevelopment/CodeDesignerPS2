using System;
using CodeDesigner.Mips64LE.Common.Emulator;

namespace CodeDesigner.Mips64LE.Common.CheatEngine
{
    /// <summary>
    /// Implements <see cref="IByteEditor"/> backed by the PS2 EE main RAM.
    /// Address translation mirrors the logic in SocketServer.HandleReadMem /
    /// HandleWriteMem so that cheat addresses (typically with a 0x20000000 offset)
    /// resolve to the correct physical RAM location.
    /// </summary>
    public sealed class PS2ByteEditor : IByteEditor
    {
        private readonly DualCpuScheduler _scheduler;

        public PS2ByteEditor(DualCpuScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        // ── IByteEditor ────────────────────────────────────────────────────────

        public byte[] Read(int address, int byteLength)
        {
            if (byteLength <= 0) return Array.Empty<byte>();
            var data = new byte[byteLength];
            for (int i = 0; i < byteLength; i++)
                data[i] = ReadByte((uint)(address + i));
            return data;
        }

        public byte[] Read(string addressHex, int byteLength)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            return Read((int)addr, byteLength);
        }

        public void Write(int address, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            for (int i = 0; i < data.Length; i++)
                WriteByte((uint)(address + i), data[i]);
        }

        public void Write(string addressHex, byte[] data)
        {
            uint addr = Convert.ToUInt32(addressHex.TrimStart('0', 'x'), 16);
            Write((int)addr, data);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private byte ReadByte(uint vaddr)
        {
            uint physAddr = TranslateEeAddress(vaddr);

            if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                return _scheduler.EE.Bus.Ram[physAddr];

            if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                return _scheduler.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];

            if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                return _scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u];

            return 0;
        }

        private void WriteByte(uint vaddr, byte value)
        {
            uint physAddr = TranslateEeAddress(vaddr);

            if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                _scheduler.EE.Bus.Ram[physAddr] = value;
            else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                _scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u] = value;
        }

        /// <summary>
        /// Translate a virtual EE address to a physical address using the same
        /// windows as SocketServer.HandleReadMem / HandleWriteMem.
        /// </summary>
        private static uint TranslateEeAddress(uint vaddr)
        {
            // KSEG0 / KSEG1 (0x80000000 – 0xBFFFFFFF)
            if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                return vaddr & 0x1FFFFFFFu;

            // Uncached user RAM mirror (0x20000000 – 0x21FFFFFF)
            if (vaddr >= 0x20000000u && vaddr < 0x22000000u)
                return vaddr - 0x20000000u;

            return vaddr;
        }
    }
}
