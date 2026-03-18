namespace CodeDesigner.Ps2.Common.Interfaces
{
    /// <summary>
    /// Abstraction for the PS2 I/O Processor memory bus.
    /// Decouples the Common instruction library from the concrete emulator implementation.
    /// </summary>
    public interface IIopMemoryBus
    {
        byte   ReadByte(uint addr);
        void   WriteByte(uint addr, byte value);

        ushort ReadHalf(uint addr);
        void   WriteHalf(uint addr, ushort value);

        uint   ReadWord(uint addr);
        void   WriteWord(uint addr, uint value);
    }
}
