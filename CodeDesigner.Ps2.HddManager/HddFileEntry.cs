namespace CodeDesigner.Ps2.HddManager;

/// <summary>Represents a file or directory within a PFS partition on the PS2 HDD.</summary>
public sealed class HddFileEntry
{
    public required string Name { get; init; }
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public ushort Attributes { get; init; }
}
