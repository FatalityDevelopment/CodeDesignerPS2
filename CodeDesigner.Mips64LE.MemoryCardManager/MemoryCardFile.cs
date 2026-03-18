namespace CodeDesigner.Mips64LE.MemoryCardManager;

/// <summary>Represents a single file within a PS2 save-game directory.</summary>
public sealed class MemoryCardFile
{
    public required string Name { get; init; }
    public long Size { get; init; }
}
