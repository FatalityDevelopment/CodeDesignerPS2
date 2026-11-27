using System;
using CodeDesigner.Ps2.Common.Interfaces;

namespace CodeDesigner.Ps2.TestProject.TestSupport
{
    /// <summary>
    /// Main RAM (32 MB) for tests, at its physical addresses and its KSEG0/KSEG1
    /// mirrors; the scratchpad at 0x70000000. Nothing else is mapped: reads
    /// elsewhere return 0 and writes are dropped.
    /// </summary>
    public sealed class TestRamBus : IPs2MemoryBus
    {
        public byte[] Ram { get; } = new byte[32 * 1024 * 1024];
        public byte[] Bios { get; } = Array.Empty<byte>();
        public byte[] Scratchpad { get; } = new byte[16 * 1024];

        private Span<byte> At(uint vaddr, int size)
        {
            if (vaddr >= 0x70000000u && vaddr - 0x70000000u + size <= (uint)Scratchpad.Length)
                return Scratchpad.AsSpan((int)(vaddr - 0x70000000u), size);
            uint phys = vaddr >= 0x80000000u && vaddr < 0xC0000000u ? vaddr & 0x1FFFFFFFu : vaddr;
            return phys + (uint)size <= (uint)Ram.Length ? Ram.AsSpan((int)phys, size) : Span<byte>.Empty;
        }

        private ulong Read(uint vaddr, int size)
        {
            var span = At(vaddr, size);
            ulong value = 0;
            for (int i = span.Length - 1; i >= 0; i--) value = (value << 8) | span[i];
            return value;
        }

        private void Write(uint vaddr, int size, ulong value)
        {
            var span = At(vaddr, size);
            for (int i = 0; i < span.Length; i++, value >>= 8) span[i] = (byte)value;
        }

        public byte ReadByte(uint vaddr) => (byte)Read(vaddr, 1);
        public void WriteByte(uint vaddr, byte value) => Write(vaddr, 1, value);
        public ushort ReadHalf(uint vaddr) => (ushort)Read(vaddr, 2);
        public void WriteHalf(uint vaddr, ushort value) => Write(vaddr, 2, value);
        public uint ReadWord(uint vaddr) => (uint)Read(vaddr, 4);
        public void WriteWord(uint vaddr, uint value) => Write(vaddr, 4, value);
        public ulong ReadDoubleWord(uint vaddr) => Read(vaddr, 8);
        public void WriteDoubleWord(uint vaddr, ulong value) => Write(vaddr, 8, value);

        public void ReadQuadWord(uint vaddr, out ulong lo, out ulong hi)
        {
            lo = Read(vaddr, 8);
            hi = Read(vaddr + 8, 8);
        }

        public void WriteQuadWord(uint vaddr, ulong lo, ulong hi)
        {
            Write(vaddr, 8, lo);
            Write(vaddr + 8, 8, hi);
        }
    }

    /// <summary>IOP RAM (2 MB) for tests, at its physical addresses.</summary>
    public sealed class TestIopRamBus : IIopMemoryBus
    {
        public byte[] Ram { get; } = new byte[2 * 1024 * 1024];

        private uint Offset(uint addr) => (addr & 0x1FFFFFFFu) % (uint)Ram.Length;

        public byte ReadByte(uint addr) => Ram[Offset(addr)];
        public void WriteByte(uint addr, byte value) => Ram[Offset(addr)] = value;
        public ushort ReadHalf(uint addr) => BitConverter.ToUInt16(Ram, (int)Offset(addr));
        public void WriteHalf(uint addr, ushort value) => BitConverter.GetBytes(value).CopyTo(Ram, (int)Offset(addr));
        public uint ReadWord(uint addr) => BitConverter.ToUInt32(Ram, (int)Offset(addr));
        public void WriteWord(uint addr, uint value) => BitConverter.GetBytes(value).CopyTo(Ram, (int)Offset(addr));
    }
}
