namespace CodeDesigner.Ps2.Bridge
{
    public class IsoFileEntryInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DiscPath { get; set; } = string.Empty;
        public uint Lba { get; set; }
        public uint Size { get; set; }
        public bool IsDirectory { get; set; }
    }
}
