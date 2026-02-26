namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// Common memory-bus interface shared by the EE (<see cref="PS2MemoryBus"/>)
    /// and the IOP (<see cref="IOP.IopMemoryBus"/>).
    ///
    /// All load/store instruction implementations use this interface so that the
    /// same instruction objects can execute correctly whether they are running on
    /// the EE or the IOP.
    /// </summary>
    public interface IMemoryBus
    {
        byte   ReadByte(uint addr);
        void   WriteByte(uint addr, byte value);

        ushort ReadHalf(uint addr);
        void   WriteHalf(uint addr, ushort value);

        uint   ReadWord(uint addr);
        void   WriteWord(uint addr, uint value);

        ulong  ReadDoubleWord(uint addr);
        void   WriteDoubleWord(uint addr, ulong value);

        void   ReadQuadWord(uint addr, out ulong lo, out ulong hi);
        void   WriteQuadWord(uint addr, ulong lo, ulong hi);
    }
}
