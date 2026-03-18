namespace CodeDesigner.Mips64LE.Common.CheatEngine
{
    /// <summary>
    /// Abstraction over raw memory read/write used by the cheat engine,
    /// so it can target emulator memory without depending on a specific backend.
    /// </summary>
    public interface IByteEditor
    {
        byte[] Read(int address, int byteLength);
        byte[] Read(string addressHex, int byteLength);
        void Write(int address, byte[] data);
        void Write(string addressHex, byte[] data);
    }
}
