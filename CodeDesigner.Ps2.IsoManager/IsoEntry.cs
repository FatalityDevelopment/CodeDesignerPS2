namespace CodeDesigner.Ps2.IsoManager;

/// <summary>
/// Represents a file or directory entry from an ISO-9660 file system.
/// </summary>
public readonly struct IsoEntry
{
    /// <summary>File or directory name (the version suffix ";1" is retained).</summary>
    public string Name { get; init; }

    /// <summary>Logical Block Address (sector number) of the data extent.</summary>
    public uint Lba { get; init; }

    /// <summary>Size of the data extent in bytes.</summary>
    public uint Size { get; init; }

    /// <summary>True when this entry is a directory (File Flags bit 1 set).</summary>
    public bool IsDirectory { get; init; }
}
