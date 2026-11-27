namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Describes one memory region of a snapshot (a .bin file in its folder).
    /// </summary>
    public class SnapshotRegion
    {
        public string Label { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public uint BaseAddress { get; set; }
        public int Size { get; set; }
    }
}
