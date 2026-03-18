namespace CodeDesigner.Ps2.MemoryCardManager;

/// <summary>Represents a top-level save-game directory on a PS2 memory card.</summary>
public sealed class MemoryCardEntry
{
    public required string Name { get; init; }
    public uint FileCount { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyList<MemoryCardFile> Files { get; init; } = [];
}
