namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A single file entry within a PS2 HDD partition.</summary>
    public class HddFileEntryInfo
    {
        public string Name         { get; set; } = string.Empty;
        public long   Size         { get; set; }
        public bool   IsDirectory  { get; set; }
        public string Modified     { get; set; } = string.Empty;
    }
}
