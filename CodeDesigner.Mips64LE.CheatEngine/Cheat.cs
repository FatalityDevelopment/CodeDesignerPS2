using CodeDesigner.Mips64LE.Common.CheatEngine.Enums;

namespace CodeDesigner.Mips64LE.Common.CheatEngine
{
    public class Cheat
    {
        public int Position { get; set; }
        public CheatType CheatType { get; set; }
        public int Address { get; set; }
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
