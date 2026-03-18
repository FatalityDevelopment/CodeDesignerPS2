using CodeDesigner.Ps2.CheatEngine.Enums;

namespace CodeDesigner.Ps2.CheatEngine
{
    public class Cheat
    {
        public int Position { get; set; }
        public CheatType CheatType { get; set; }
        public int Address { get; set; }
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
