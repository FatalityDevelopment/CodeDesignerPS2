namespace CodeDesigner.Ps2.HddManager;

/// <summary>Represents a partition on a PS2 HDD image (APA partition table entry).</summary>
public sealed class HddPartitionEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public uint StartSector { get; init; }
    public uint LengthSectors { get; init; }
    public ushort Type { get; init; }
    public ushort Flags { get; init; }
    public uint SubPartitionCount { get; init; }
    public DateTime Created { get; init; }
    public long TotalBytes => (long)LengthSectors * Ps2HddImage.SectorSize;
    public uint FileCount { get; init; }
    public string TypeName => Type switch
    {
        0x0001 => "MBR",
        0x0082 => "SWAP",
        0x0083 => "LINUX",
        0x0100 => "GAME",
        0x1337 => "MBR-SPECIAL",
        _ => $"0x{Type:X4}",
    };
}
