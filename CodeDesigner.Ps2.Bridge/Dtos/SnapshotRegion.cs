namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Describes one memory region written by a SNAPSHOT command.
    /// </summary>
    public class SnapshotRegion
    {
        public string Label { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public uint BaseAddress { get; set; }
        public int Size { get; set; }
    }
}
