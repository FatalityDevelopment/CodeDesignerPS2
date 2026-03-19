using System.Collections.Generic;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>Metadata for a single PS2 save-game directory returned by MC_LIST.</summary>
    public class McSaveInfo
    {
        public string Name       { get; set; } = string.Empty;
        public uint   FileCount  { get; set; }
        public string Created    { get; set; } = string.Empty;
        public string Modified   { get; set; } = string.Empty;
        public long   TotalBytes { get; set; }
        public List<McFileInfo> Files { get; set; } = [];
    }
}
