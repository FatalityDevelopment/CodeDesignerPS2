namespace CodeDesigner.Mips64LE.Common.Interfaces
{
    /// <summary>
    /// Abstraction for the PS2 Emotion Engine main memory bus.
    /// Decouples the Common instruction library from the concrete emulator implementation.
    /// </summary>
    public interface IPs2MemoryBus
    {
        /// <summary>Raw main RAM byte array (32 MB).</summary>
        byte[] Ram { get; }

        /// <summary>BIOS ROM byte array (4 MB).</summary>
        byte[] Bios { get; }

        /// <summary>Scratchpad RAM byte array (16 KB).</summary>
        byte[] Scratchpad { get; }

        byte   ReadByte(uint vaddr);
        void   WriteByte(uint vaddr, byte value);

        ushort ReadHalf(uint vaddr);
        void   WriteHalf(uint vaddr, ushort value);

        uint   ReadWord(uint vaddr);
        void   WriteWord(uint vaddr, uint value);

        ulong  ReadDoubleWord(uint vaddr);
        void   WriteDoubleWord(uint vaddr, ulong value);

        void   ReadQuadWord(uint vaddr, out ulong lo, out ulong hi);
        void   WriteQuadWord(uint vaddr, ulong lo, ulong hi);
    }
}
