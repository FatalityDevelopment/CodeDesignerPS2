using System.Collections.Generic;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>Metadata for a single PS2 HDD partition returned by HDD_LIST.</summary>
    public class HddPartitionInfo
    {
        public string Id          { get; set; } = string.Empty;
        public string Name        { get; set; } = string.Empty;
        public uint   StartSector { get; set; }
        public long   TotalBytes  { get; set; }
        public string TypeName    { get; set; } = string.Empty;
        public uint   FileCount   { get; set; }
        public string Created     { get; set; } = string.Empty;
        public List<HddFileEntryInfo> Files { get; set; } = [];
    }
}
