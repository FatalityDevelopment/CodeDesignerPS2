namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A single file entry within a PS2 save directory.</summary>
    public class McFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public long   Size { get; set; }
    }
}
