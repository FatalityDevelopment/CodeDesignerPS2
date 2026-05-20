using System.Buffers.Binary;
using System.Text;

namespace CodeDesigner.Ps2.HddManager;

/// <summary>
/// Reads and writes PlayStation 2 HDD raw images (HDD.raw format, as used by PCSX2).
/// Supports the APA (Adaptec Partition Allocation) partition table and PFS
/// (PlayStation File System) used within each partition.
/// </summary>
public sealed class Ps2HddImage : IDisposable
{
    // ── APA constants ───────────────────────────────────────────────────────
    public const int SectorSize = 512;
    private const uint ApaMagic = 0x00415041;               // "APA\0" LE
    private const int ApaHeaderSize = 1024;                  // full APA header = 2 sectors
    private const int PartitionNameOffset = 0x7F;
    private const int PartitionIdOffset = 0x10;
    private const int ApaSubPartitionsOffset = 0x200;        // apa_header_t.subs

    // ── PFS constants ───────────────────────────────────────────────────────
    private const uint PfsMagic = 0x50465300;               // "PFS\0" LE
    private const uint PfsSegdMagic = 0x53454744;           // "SEGD"
    private const int PfsDefaultZoneSize = 0x2000;           // 8192 bytes
    private const int PfsMetadataSize = 1024;                // pfsMetaSize in ps2sdk
    private const int PfsSuperblockSector = 8192;            // PFS_SUPER_SECTOR in ps2sdk
    private const int PfsSuperblockOffset = PfsSuperblockSector * SectorSize;
    private const int LegacyPfsSuperblockOffset = 0x2000;    // pre-fix CodeDesigner images
    private const int InodeSize = PfsMetadataSize;           // 1 inode = 1024 bytes (pfs_inode_t)
    private const int MaxInodeDataBlocks = 114;              // direct block pointers per inode

    // PFS inode mode and attribute flags
    private const ushort FioModeMask      = 0xF000;
    private const ushort FioModeDirectory = 0x1000;
    private const ushort PfsAttrReadable   = 0x0001;
    private const ushort PfsAttrWritable   = 0x0002;
    private const ushort PfsAttrExecutable = 0x0004;
    private const ushort PfsAttrClosed     = 0x0080;
    private const ushort PfsAttrHidden     = 0x4000;
    private const ushort PfsDentryLengthMask = 0x0FFF;

    // ── Image state ─────────────────────────────────────────────────────────
    private FileStream? _stream;
    private long _totalSectors;

    // Cached partition list
    private List<ApaPartition>? _partitions;

    public void Dispose() => _stream?.Dispose();

    // ── Internal structures ─────────────────────────────────────────────────

    private struct ApaPartition
    {
        public string Id;
        public string Name;
        public uint Start;          // sector
        public uint Length;         // sectors
        public ushort Type;
        public ushort Flags;
        public uint NSub;
        public DateTime Created;
        public uint Next;
        public uint Prev;
        public uint[] SubStarts;    // sub-partition start sectors
    }

    private struct BlockInfo
    {
        public uint Number;         // block/sector number
        public ushort SubPart;      // sub-partition index
        public ushort Count;        // block count
    }

    private struct PfsSuperblock
    {
        public uint Magic;
        public uint Version;
        public uint ZoneSize;       // allocation zone size in sectors
        public uint NumSubs;
        public BlockInfo Log;
        public BlockInfo Root;
    }

    private struct PfsInode
    {
        public uint Checksum;
        public uint Magic;
        public BlockInfo InodeBlock;
        public BlockInfo NextSegment;
        public BlockInfo LastSegment;
        public BlockInfo Unused;
        public BlockInfo[] Data;    // up to 114 direct block pointers
        public ushort Mode;
        public ushort Attr;
        public ushort Uid;
        public ushort Gid;
        public DateTime ATime;
        public DateTime CTime;
        public DateTime MTime;
        public long Size;
        public uint NumberBlocks;
        public uint NumberData;
        public uint NumberSegdesg;
        public uint SubPart;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Opens an existing PS2 HDD raw image.</summary>
    public static Ps2HddImage Open(string path)
    {
        var hdd = new Ps2HddImage();
        hdd._stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        hdd._totalSectors = hdd._stream.Length / SectorSize;
        hdd.ReadPartitionTable();
        return hdd;
    }

    /// <summary>Creates a new blank PS2 HDD raw image with the given size.</summary>
    public static void Create(string path, int sizeInGb = 40)
    {
        long totalBytes = (long)sizeInGb * 1024 * 1024 * 1024;
        long totalSectors = totalBytes / SectorSize;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Set file length (sparse on NTFS / ext4)
        fs.SetLength(totalBytes);

        // Write MBR partition header at sector 0
        var header = new byte[ApaHeaderSize];
        WriteMbrPartition(header, (uint)totalSectors);
        fs.Position = 0;
        fs.Write(header);

        // Create default system partitions
        uint nextSector = 0x00040000; // 128 MB boundary = 262144 sectors
        string[] defaultPartitions = ["__common", "__net", "__system", "__sysconf", "__contents"];

        uint mbrNext = 0u;
        uint prevSector = 0u;
        Span<byte> linkBytes = stackalloc byte[4];

        foreach (var partId in defaultPartitions)
        {
            var partHeader = new byte[ApaHeaderSize];
            WritePartitionHeader(partHeader, partId, nextSector, 0x00040000, 0x0100, prevSector);

            fs.Position = (long)nextSector * SectorSize;
            fs.Write(partHeader);

            // Initialize a minimal PFS superblock inside the partition
            WritePfsSuperblock(fs, nextSector, 0x00040000);

            if (mbrNext == 0)
            {
                mbrNext = nextSector;
            }
            else
            {
                // Update previous partition's next pointer
                fs.Position = (long)prevSector * SectorSize + 0x08;
                BinaryPrimitives.WriteUInt32LittleEndian(linkBytes, nextSector);
                fs.Write(linkBytes);
            }

            prevSector = nextSector;
            nextSector += 0x00040000; // each partition = 128 MB
        }

        // Update MBR next pointer to first partition
        fs.Position = 0x08;
        BinaryPrimitives.WriteUInt32LittleEndian(linkBytes, mbrNext);
        fs.Write(linkBytes);

        // Recalculate and write MBR checksum
        fs.Position = 0;
        var mbrData = new byte[ApaHeaderSize];
        fs.Read(mbrData);
        uint checksum = CalculateApaChecksum(mbrData);
        BinaryPrimitives.WriteUInt32LittleEndian(mbrData.AsSpan(0, 4), checksum);
        fs.Position = 0;
        fs.Write(mbrData.AsSpan(0, 4));

        // Recalculate checksums for all partition headers
        foreach (var partId in defaultPartitions)
        {
            uint sector = mbrNext;
            // Find this partition by reading the chain
            while (sector != 0)
            {
                fs.Position = (long)sector * SectorSize;
                var ph = new byte[ApaHeaderSize];
                fs.Read(ph);

                var id = ReadAsciiString(ph, PartitionIdOffset, 32);
                if (id == partId)
                {
                    checksum = CalculateApaChecksum(ph);
                    BinaryPrimitives.WriteUInt32LittleEndian(ph.AsSpan(0, 4), checksum);
                    fs.Position = (long)sector * SectorSize;
                    fs.Write(ph.AsSpan(0, 4));
                    break;
                }

                uint nextPtr = BinaryPrimitives.ReadUInt32LittleEndian(ph.AsSpan(0x08));
                sector = nextPtr;
            }
        }

        fs.Flush();
    }

    /// <summary>Lists all APA partitions on the HDD image.</summary>
    public HddPartitionEntry[] ListPartitions()
    {
        EnsureOpen();
        var partitions = GetPartitions();
        return partitions.Select(p =>
        {
            uint fileCount = 0;
            try
            {
                var files = ListFilesInPartition(p);
                fileCount = (uint)files.Length;
            }
            catch { /* partition may not have PFS */ }

            return new HddPartitionEntry
            {
                Id = p.Id,
                Name = string.IsNullOrEmpty(p.Name) ? p.Id : p.Name,
                StartSector = p.Start,
                LengthSectors = p.Length,
                Type = p.Type,
                Flags = p.Flags,
                SubPartitionCount = p.NSub,
                Created = p.Created,
                FileCount = fileCount,
            };
        }).ToArray();
    }

    /// <summary>Lists files in a specific partition (by partition ID).</summary>
    public HddFileEntry[] ListFiles(string partitionId)
    {
        EnsureOpen();
        var part = FindPartition(partitionId);
        return ListFilesInPartition(part);
    }

    /// <summary>Exports a file from a partition to a host directory.</summary>
    public void ExportFile(string partitionId, string fileName, string outputPath)
    {
        EnsureOpen();
        var part = FindPartition(partitionId);
        ExportFileFromPartition(part, fileName, outputPath);
    }

    /// <summary>
    /// Imports a file from the host into a partition.
    /// <para>
    /// <b>Not yet implemented.</b> PFS write support (allocation bitmap management,
    /// inode creation, directory entry creation) is not yet available. Use PCSX2's
    /// built-in HDD tools or the pfsshell utility for write operations.
    /// </para>
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown — PFS write support is pending.</exception>
    public void ImportFile(string partitionId, string inputPath, bool overwrite = false)
    {
        EnsureOpen();
        var part = FindPartition(partitionId);
        ImportFileToPartition(part, inputPath, overwrite);
    }

    /// <summary>Deletes a partition by ID (marks it as free by clearing its magic).</summary>
    public void DeletePartition(string partitionId)
    {
        EnsureOpen();
        var part = FindPartition(partitionId);

        // Don't allow deleting the MBR
        if (part.Start == 0)
            throw new InvalidOperationException("Cannot delete the MBR partition.");

        // Clear the APA magic to mark as free
        _stream!.Position = (long)part.Start * SectorSize + 4; // magic offset
        Span<byte> zeroes = stackalloc byte[4];
        _stream.Write(zeroes);
        _stream.Flush();

        // Relink the partition chain (connect prev to next)
        if (part.Prev != 0)
        {
            _stream.Position = (long)part.Prev * SectorSize + 0x08; // next pointer of prev
            Span<byte> nextBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nextBytes, part.Next);
            _stream.Write(nextBytes);

            // Recalculate prev partition checksum
            RecalculatePartitionChecksum(part.Prev);
        }
        else
        {
            // This was the first partition after MBR, update MBR's next
            _stream.Position = 0x08;
            Span<byte> nextBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nextBytes, part.Next);
            _stream.Write(nextBytes);

            RecalculatePartitionChecksum(0);
        }

        if (part.Next != 0)
        {
            _stream.Position = (long)part.Next * SectorSize + 0x0C; // prev pointer of next
            Span<byte> prevBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(prevBytes, part.Prev);
            _stream.Write(prevBytes);

            RecalculatePartitionChecksum(part.Next);
        }

        _stream.Flush();
        _partitions = null; // Invalidate cache
    }

    // ── Partition table reading ─────────────────────────────────────────────

    private void ReadPartitionTable()
    {
        _partitions = [];

        // Read MBR at sector 0
        var header = ReadSectors(0, 2); // APA header = 2 sectors
        if (header.Length < ApaHeaderSize)
            throw new InvalidDataException("HDD image too small to contain MBR.");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (magic != ApaMagic)
            throw new InvalidDataException($"Invalid APA magic: 0x{magic:X8} (expected 0x{ApaMagic:X8})");

        var mbr = ParsePartitionHeader(header, 0);
        _partitions.Add(mbr);

        // Follow linked list of partitions
        uint nextSector = mbr.Next;
        var visited = new HashSet<uint> { 0 };

        while (nextSector != 0 && !visited.Contains(nextSector))
        {
            visited.Add(nextSector);

            if ((long)nextSector * SectorSize + ApaHeaderSize > _stream!.Length)
                break;

            var partHeader = ReadSectors(nextSector, 2);
            uint partMagic = BinaryPrimitives.ReadUInt32LittleEndian(partHeader.AsSpan(4));
            if (partMagic != ApaMagic)
                break;

            var part = ParsePartitionHeader(partHeader, nextSector);
            _partitions.Add(part);
            nextSector = part.Next;
        }
    }

    private ApaPartition ParsePartitionHeader(byte[] data, uint sectorBase)
    {
        var span = data.AsSpan();

        uint next = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x08));
        uint prev = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x0C));

        string id = ReadAsciiString(data, PartitionIdOffset, 32);
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x40));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x44));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x48));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x4A));
        uint nsub = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x4C));

        // Created timestamp at offset 0x50 (7 bytes: reserved, sec, min, hr, day, mon, year LE16)
        DateTime created = ReadApaTimestamp(span.Slice(0x50, 8));

        string name = id;

        // Sub-partition table at apa_header_t.subs (64 entries, 8 bytes each).
        var subStarts = new uint[Math.Min(nsub, 64)];
        for (int i = 0; i < subStarts.Length && ApaSubPartitionsOffset + i * 8 + 4 <= data.Length; i++)
        {
            subStarts[i] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(ApaSubPartitionsOffset + i * 8));
        }

        // Use sector base if start field is 0 and we know where we read from
        if (start == 0 && sectorBase != 0) start = sectorBase;

        return new ApaPartition
        {
            Id = id,
            Name = name,
            Start = start,
            Length = length,
            Type = type,
            Flags = flags,
            NSub = nsub,
            Created = created,
            Next = next,
            Prev = prev,
            SubStarts = subStarts,
        };
    }

    // ── PFS filesystem reading ──────────────────────────────────────────────

    private PfsSuperblock ReadPfsSuperblock(ApaPartition part)
    {
        var buf = TryReadPfsSuperblock(part, PfsSuperblockOffset)
            ?? TryReadPfsSuperblock(part, LegacyPfsSuperblockOffset);
        if (buf == null)
            throw new InvalidDataException($"Invalid PFS magic in partition '{part.Id}'");

        var span = buf.AsSpan();
        return new PfsSuperblock
        {
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(span),
            Version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)),
            ZoneSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16)),
            NumSubs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20)),
            Log = ReadBlockInfo(span.Slice(24)),
            Root = ReadBlockInfo(span.Slice(32)),
        };
    }

    private byte[]? TryReadPfsSuperblock(ApaPartition part, int offset)
    {
        long absOffset = (long)part.Start * SectorSize + offset;
        if (absOffset < 0 || absOffset + SectorSize > _stream!.Length)
            return null;

        _stream.Position = absOffset;

        var buf = new byte[SectorSize];
        int read = _stream.Read(buf);
        if (read < SectorSize)
            return null;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buf);
        return magic == PfsMagic ? buf : null;
    }

    private PfsInode ReadInode(ApaPartition part, BlockInfo blockRef, uint zoneSize)
    {
        long absOffset = GetAbsoluteOffset(part, blockRef, zoneSize);
        _stream!.Position = absOffset;

        var buf = new byte[InodeSize];
        int read = _stream.Read(buf);
        if (read < InodeSize)
            throw new InvalidDataException("Failed to read PFS inode.");

        return ParseInode(buf);
    }

    private PfsInode ParseInode(byte[] data)
    {
        var span = data.AsSpan();

        var inode = new PfsInode
        {
            Checksum = BinaryPrimitives.ReadUInt32LittleEndian(span),
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)),
            InodeBlock = ReadBlockInfo(span.Slice(8)),
            NextSegment = ReadBlockInfo(span.Slice(16)),
            LastSegment = ReadBlockInfo(span.Slice(24)),
            Unused = ReadBlockInfo(span.Slice(32)),
        };

        // Direct block pointers at offset 40, each 8 bytes
        inode.Data = new BlockInfo[MaxInodeDataBlocks];
        for (int i = 0; i < MaxInodeDataBlocks && 40 + i * 8 + 8 <= data.Length; i++)
        {
            inode.Data[i] = ReadBlockInfo(span.Slice(40 + i * 8));
        }

        // PS2 PFS inode metadata layout (from ps2sdk pfs_inode_t, 1024 bytes total):
        // offset 0x00: checksum (4)
        // offset 0x04: magic (4)
        // offset 0x08: inode_block (8)
        // offset 0x10: next_segment (8)
        // offset 0x18: last_segment (8)
        // offset 0x20: unused (8)
        // offset 0x28: data[0..113] (114 * 8 = 912 bytes)
        // offset 0x3B8: mode (2)
        // offset 0x3BA: attr (2)
        // offset 0x3BC: uid (2)
        // offset 0x3BE: gid (2)
        // offset 0x3C0: atime (8)
        // offset 0x3C8: ctime (8)
        // offset 0x3D0: mtime (8)
        // offset 0x3D8: size (8)
        // offset 0x3E0: number_blocks (4)
        // offset 0x3E4: number_data (4)
        // offset 0x3E8: number_segdesg (4)
        // offset 0x3EC: subpart (4)
        // offset 0x3F0: reserved[4] (16)
        // Total: 0x400 = 1024 bytes

        if (data.Length >= 0x3F0)
        {
            inode.Mode = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x3B8));
            inode.Attr = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x3BA));
            inode.Uid = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x3BC));
            inode.Gid = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x3BE));
            inode.ATime = ReadPfsTimestamp(span.Slice(0x3C0, 8));
            inode.CTime = ReadPfsTimestamp(span.Slice(0x3C8, 8));
            inode.MTime = ReadPfsTimestamp(span.Slice(0x3D0, 8));
            inode.Size = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(0x3D8));
            inode.NumberBlocks = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x3E0));
            inode.NumberData = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x3E4));
            inode.NumberSegdesg = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x3E8));
            inode.SubPart = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x3EC));
        }

        return inode;
    }

    private HddFileEntry[] ListFilesInPartition(ApaPartition part)
    {
        PfsSuperblock sb;
        try
        {
            sb = ReadPfsSuperblock(part);
        }
        catch
        {
            return []; // No valid PFS filesystem
        }

        if (sb.Root.Number == 0 && sb.Root.SubPart == 0 && sb.Root.Count == 0)
            return [];

        var rootInode = ReadInode(part, sb.Root, sb.ZoneSize);
        return ReadDirectoryEntries(part, sb, rootInode);
    }

    private HddFileEntry[] ReadDirectoryEntries(ApaPartition part, PfsSuperblock sb, PfsInode dirInode)
    {
        var entries = new List<HddFileEntry>();

        // Read all data blocks of the directory inode
        var dirData = ReadInodeData(part, sb, dirInode);
        if (dirData.Length == 0) return [];

        int offset = 0;
        while (offset + 8 < dirData.Length)
        {
            uint inodeRef = BinaryPrimitives.ReadUInt32LittleEndian(dirData.AsSpan(offset));
            byte sub = dirData[offset + 4];
            byte pLen = dirData[offset + 5];
            ushort rawALen = BinaryPrimitives.ReadUInt16LittleEndian(dirData.AsSpan(offset + 6));
            int aLen = rawALen & PfsDentryLengthMask;

            if (aLen == 0) break;
            if ((aLen & 3) != 0 || offset + aLen > dirData.Length) break;

            if (pLen > 0 && offset + 8 + pLen <= dirData.Length)
            {
                string name = Encoding.ASCII.GetString(dirData, offset + 8, pLen).TrimEnd('\0');

                // Skip "." and ".." entries
                if (name != "." && name != "..")
                {
                    bool isDir = false;
                    long size = 0;
                    DateTime created = DateTime.MinValue;
                    DateTime modified = DateTime.MinValue;
                    ushort attr = 0;

                    // Try to read the file's inode to get metadata
                    try
                    {
                        var fileBlockRef = new BlockInfo { Number = inodeRef, SubPart = sub, Count = 1 };
                        var fileInode = ReadInode(part, fileBlockRef, sb.ZoneSize);
                        isDir = IsDirectory(fileInode.Mode);
                        size = fileInode.Size;
                        created = fileInode.CTime;
                        modified = fileInode.MTime;
                        attr = fileInode.Attr;
                    }
                    catch { /* best effort */ }

                    entries.Add(new HddFileEntry
                    {
                        Name = name,
                        Size = size,
                        IsDirectory = isDir,
                        Created = created,
                        Modified = modified,
                        Attributes = attr,
                    });
                }
            }

            offset += aLen;
        }

        return entries.ToArray();
    }

    private byte[] ReadInodeData(ApaPartition part, PfsSuperblock sb, PfsInode inode)
    {
        uint zoneSize = NormalizeZoneSize(sb.ZoneSize);
        var allData = new List<byte>();
        long remaining = inode.Size > 0
            ? inode.Size
            : Math.Max(0, (long)(inode.NumberBlocks - inode.NumberSegdesg) * zoneSize);
        if (remaining <= 0) remaining = Math.Max(0, (long)(inode.NumberData - 1) * zoneSize);
        if (remaining <= 0) return [];

        var descriptor = inode;
        uint globalIndex = 1; // data[0] is the segment descriptor itself.
        var visitedSegments = new HashSet<(uint Number, ushort SubPart)>();

        while (globalIndex < inode.NumberData && allData.Count < remaining)
        {
            int localIndex = PfsFixIndex(globalIndex);
            if (localIndex == 0)
            {
                if (descriptor.NextSegment.Number == 0)
                    break;

                var key = (descriptor.NextSegment.Number, descriptor.NextSegment.SubPart);
                if (!visitedSegments.Add(key))
                    break;

                descriptor = ReadInode(part, descriptor.NextSegment, zoneSize);
                globalIndex++;
                continue;
            }

            if (localIndex >= descriptor.Data.Length)
                break;

            var block = descriptor.Data[localIndex];
            if (block.Count == 0)
            {
                globalIndex++;
                continue;
            }

            long absOffset = GetAbsoluteOffset(part, block, zoneSize);
            int bytesToRead = (int)Math.Min((long)block.Count * zoneSize, remaining - allData.Count);
            if (bytesToRead <= 0) break;

            _stream!.Position = absOffset;
            var buf = new byte[bytesToRead];
            _stream.Read(buf);
            allData.AddRange(buf);

            globalIndex++;
        }

        return allData.ToArray();
    }

    private void ExportFileFromPartition(ApaPartition part, string fileName, string outputPath)
    {
        PfsSuperblock sb = ReadPfsSuperblock(part);
        var rootInode = ReadInode(part, sb.Root, sb.ZoneSize);
        ExportEntryFromDir(part, sb, rootInode, fileName, outputPath, partId: part.Id);
    }

    /// <summary>
    /// Finds <paramref name="entryName"/> inside <paramref name="dirInode"/> and exports it
    /// (recursively for directories) into <paramref name="outputDir"/>.
    /// </summary>
    private void ExportEntryFromDir(ApaPartition part, PfsSuperblock sb, PfsInode dirInode, string entryName, string outputDir, string partId = "")
    {
        var dirData = ReadInodeData(part, sb, dirInode);

        int offset = 0;
        while (offset + 8 < dirData.Length)
        {
            uint inodeRef = BinaryPrimitives.ReadUInt32LittleEndian(dirData.AsSpan(offset));
            byte sub = dirData[offset + 4];
            byte pLen = dirData[offset + 5];
            ushort rawALen = BinaryPrimitives.ReadUInt16LittleEndian(dirData.AsSpan(offset + 6));
            int aLen = rawALen & PfsDentryLengthMask;

            if (aLen == 0) break;
            if ((aLen & 3) != 0 || offset + aLen > dirData.Length) break;

            if (pLen > 0 && offset + 8 + pLen <= dirData.Length)
            {
                string name = Encoding.ASCII.GetString(dirData, offset + 8, pLen).TrimEnd('\0');
                if (name == entryName)
                {
                    var fileBlockRef = new BlockInfo { Number = inodeRef, SubPart = sub, Count = 1 };
                    var fileInode = ReadInode(part, fileBlockRef, sb.ZoneSize);

                    bool isDir = IsDirectory(fileInode.Mode);
                    if (isDir)
                    {
                        string dirPath = Path.Combine(outputDir, entryName);
                        Directory.CreateDirectory(dirPath);
                        // Recursively export all entries inside this directory
                        var subEntries = ReadDirectoryEntries(part, sb, fileInode);
                        foreach (var entry in subEntries)
                        {
                            ExportEntryFromDir(part, sb, fileInode, entry.Name, dirPath);
                        }
                    }
                    else
                    {
                        var fileData = ReadInodeData(part, sb, fileInode);
                        string outFile = Path.Combine(outputDir, entryName);
                        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                        File.WriteAllBytes(outFile, fileData.AsSpan(0, (int)Math.Min(fileInode.Size, fileData.Length)).ToArray());
                    }
                    return;
                }
            }

            offset += aLen;
        }

        throw new FileNotFoundException(string.IsNullOrEmpty(partId)
            ? $"Entry '{entryName}' not found."
            : $"File '{entryName}' not found in partition '{partId}'.");
    }

    private void ImportFileToPartition(ApaPartition part, string inputPath, bool overwrite)
    {
        // For now, importing into PFS is complex. We'll provide a basic implementation
        // that writes file data to a new allocation zone.
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            throw new FileNotFoundException($"Import path not found: {inputPath}");

        // This is a simplified import - for full PFS write support, we'd need:
        // 1. Allocation bitmap management
        // 2. Free zone finding
        // 3. Inode creation
        // 4. Directory entry creation
        // For now, throw a descriptive message
        throw new NotSupportedException(
            "Direct file import to PFS partitions is not yet supported. " +
            "Use PCSX2's built-in HDD tools or pfsshell for write operations.");
    }

    // ── Helper methods ──────────────────────────────────────────────────────

    private long GetAbsoluteOffset(ApaPartition part, BlockInfo block, uint zoneSize)
    {
        zoneSize = NormalizeZoneSize(zoneSize);

        // For sub-partition 0, block number is relative to the partition start.
        if (block.SubPart == 0)
        {
            return (long)part.Start * SectorSize + (long)block.Number * zoneSize;
        }

        // For sub-partitions, look up the sub-partition start sector
        if (block.SubPart > 0 && block.SubPart <= part.SubStarts.Length)
        {
            uint subStart = part.SubStarts[block.SubPart - 1];
            if (subStart != 0)
                return (long)subStart * SectorSize + (long)block.Number * zoneSize;
        }

        // Fallback: treat as partition-relative
        return (long)part.Start * SectorSize + (long)block.Number * zoneSize;
    }

    private static bool IsDirectory(ushort mode) => (mode & FioModeMask) == FioModeDirectory;

    private static int PfsFixIndex(uint index)
    {
        if (index < MaxInodeDataBlocks)
            return (int)index;

        index -= MaxInodeDataBlocks;
        return (int)(index % 123);
    }

    private static uint NormalizeZoneSize(uint zoneSize)
    {
        return zoneSize == 0 ? PfsDefaultZoneSize : zoneSize;
    }

    private byte[] ReadSectors(uint startSector, uint count)
    {
        _stream!.Position = (long)startSector * SectorSize;
        var buf = new byte[count * SectorSize];
        _stream.Read(buf);
        return buf;
    }

    private List<ApaPartition> GetPartitions()
    {
        if (_partitions == null)
            ReadPartitionTable();
        return _partitions!;
    }

    private ApaPartition FindPartition(string partitionId)
    {
        var partitions = GetPartitions();
        var found = partitions.FirstOrDefault(p =>
            string.Equals(p.Id, partitionId, StringComparison.OrdinalIgnoreCase));
        if (found.Id == null)
            throw new FileNotFoundException($"Partition '{partitionId}' not found on HDD image.");
        return found;
    }

    private void EnsureOpen()
    {
        if (_stream == null)
            throw new InvalidOperationException("HDD image is not open.");
    }

    private void RecalculatePartitionChecksum(uint sector)
    {
        _stream!.Position = (long)sector * SectorSize;
        var header = new byte[ApaHeaderSize];
        _stream.Read(header);

        uint checksum = CalculateApaChecksum(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), checksum);
        _stream.Position = (long)sector * SectorSize;
        _stream.Write(header.AsSpan(0, 4));
        _stream.Flush();
    }

    private static BlockInfo ReadBlockInfo(ReadOnlySpan<byte> span)
    {
        return new BlockInfo
        {
            Number = BinaryPrimitives.ReadUInt32LittleEndian(span),
            SubPart = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4)),
            Count = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6)),
        };
    }

    private static DateTime ReadApaTimestamp(ReadOnlySpan<byte> span)
    {
        try
        {
            // APA timestamp: byte[0]=reserved, [1]=sec, [2]=min, [3]=hour, [4]=day, [5]=month, [6..7]=year(LE16)
            byte sec = span[1];
            byte min = span[2];
            byte hour = span[3];
            byte day = span[4];
            byte month = span[5];
            ushort year = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6));

            if (year == 0 || month == 0 || day == 0) return DateTime.MinValue;
            if (month > 12) month = 12;
            if (day > 31) day = 31;
            if (hour > 23) hour = 23;
            if (min > 59) min = 59;
            if (sec > 59) sec = 59;

            return new DateTime(year, month, day, hour, min, sec, DateTimeKind.Utc);
        }
        catch { return DateTime.MinValue; }
    }

    private static DateTime ReadPfsTimestamp(ReadOnlySpan<byte> span)
    {
        try
        {
            // PFS timestamp: similar to APA
            byte sec = span[1];
            byte min = span[2];
            byte hour = span[3];
            byte day = span[4];
            byte month = span[5];
            ushort year = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6));

            if (year == 0 || month == 0 || day == 0) return DateTime.MinValue;
            if (month > 12) month = 12;
            if (day > 31) day = 31;
            if (hour > 23) hour = 23;
            if (min > 59) min = 59;
            if (sec > 59) sec = 59;

            return new DateTime(year, month, day, hour, min, sec, DateTimeKind.Utc);
        }
        catch { return DateTime.MinValue; }
    }

    private static uint CalculateApaChecksum(byte[] data)
    {
        uint sum = 0;
        // Sum all 256 32-bit words, with word 0 (checksum) treated as 0
        for (int i = 1; i < 256 && i * 4 + 4 <= data.Length; i++)
        {
            sum += BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4));
        }
        return sum;
    }

    private static string ReadAsciiString(byte[] data, int offset, int maxLen)
    {
        int end = offset;
        int limit = Math.Min(offset + maxLen, data.Length);
        while (end < limit && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    // ── HDD creation helpers ────────────────────────────────────────────────

    private static void WriteMbrPartition(byte[] header, uint totalSectors)
    {
        // APA magic at offset 4
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), ApaMagic);

        // next/prev = 0 initially (will be updated)
        // Partition ID = "__mbr"
        WriteAsciiString(header, PartitionIdOffset, "__mbr", 32);

        // start = 0
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x40), 0);
        // length = 0x00040000 (128 MB in sectors)
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x44), 0x00040000);
        // type = 0x1337 (MBR special)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x48), 0x1337);
        // flags = 0
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x4A), 0);
        // nsub = 0
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x4C), 0);

        // Created timestamp (now)
        WriteApaTimestamp(header.AsSpan(0x50), DateTime.UtcNow);

        // Name = "__mbr"
        WriteAsciiString(header, PartitionNameOffset, "__mbr", 32);

        // MBR copyright string at offset 0x9F
        string copyright = "PS2 HDD Created by CodeDesigner";
        WriteAsciiString(header, 0x9F, copyright, Math.Min(copyright.Length + 1, 80));

        // Checksum
        uint checksum = CalculateApaChecksum(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), checksum);
    }

    private static void WritePartitionHeader(byte[] header, string id, uint start, uint length, ushort type, uint prev)
    {
        // APA magic
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), ApaMagic);

        // next = 0 (will be updated), prev
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x08), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x0C), prev);

        // ID
        WriteAsciiString(header, PartitionIdOffset, id, 32);

        // start, length, type
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x40), start);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x44), length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x48), type);

        // Timestamp
        WriteApaTimestamp(header.AsSpan(0x50), DateTime.UtcNow);

        // Name
        WriteAsciiString(header, PartitionNameOffset, id, 32);

        // Checksum
        uint checksum = CalculateApaChecksum(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), checksum);
    }

    private static void WritePfsSuperblock(FileStream fs, uint partitionStart, uint partitionLength)
    {
        long absOffset = (long)partitionStart * SectorSize + PfsSuperblockOffset;
        fs.Position = absOffset;

        var sb = new byte[SectorSize];

        // Magic
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(0), PfsMagic);
        // Version = 3
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(4), 3);
        // modver = 0
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(8), 0);
        // pfsFsckStat = 0
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(12), 0);
        // zone_size is in bytes.
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(16), PfsDefaultZoneSize);
        // num_subs = 0
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(20), 0);

        // Log block info at offset 24 (block 2, subpart 0, count 1)
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(24), 2);  // number
        BinaryPrimitives.WriteUInt16LittleEndian(sb.AsSpan(28), 0);  // subpart
        BinaryPrimitives.WriteUInt16LittleEndian(sb.AsSpan(30), 1);  // count

        // Root inode block info at offset 32 (block 3, subpart 0, count 1)
        BinaryPrimitives.WriteUInt32LittleEndian(sb.AsSpan(32), 3);  // number
        BinaryPrimitives.WriteUInt16LittleEndian(sb.AsSpan(36), 0);  // subpart
        BinaryPrimitives.WriteUInt16LittleEndian(sb.AsSpan(38), 1);  // count

        fs.Write(sb);

        // Write a minimal root directory inode at block 3
        long rootInodeOffset = (long)partitionStart * SectorSize + 3L * PfsDefaultZoneSize;
        fs.Position = rootInodeOffset;

        var rootInode = new byte[InodeSize];
        // magic (not the same as PFS superblock magic — inode magic varies)
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(4), PfsSegdMagic);
        // inode_block = block 3
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(14), 1);

        // last_segment = block 3
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(24), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(28), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(30), 1);

        // data[0] = this segment descriptor, data[1] = root directory data block.
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(40), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(44), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(46), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(48), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(52), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(54), 1);

        // Mode = directory + readable + writable + executable
        BinaryPrimitives.WriteUInt16LittleEndian(rootInode.AsSpan(0x3B8),
            (ushort)(FioModeDirectory | PfsAttrReadable | PfsAttrWritable | PfsAttrExecutable));
        // number_data = 2 (segment descriptor + directory data)
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(0x3E4), 2);
        // number_blocks = 2, number_segdesg = 1
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(0x3E0), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(0x3E8), 1);
        // Size = block size (one block of directory entries)
        BinaryPrimitives.WriteInt64LittleEndian(rootInode.AsSpan(0x3D8), PfsDefaultZoneSize);
        // Timestamp
        var now = DateTime.UtcNow;
        WritePfsTimestamp(rootInode.AsSpan(0x3C0), now);
        WritePfsTimestamp(rootInode.AsSpan(0x3C8), now);
        WritePfsTimestamp(rootInode.AsSpan(0x3D0), now);

        // Checksum = sum of all 32-bit words except the first (checksum word itself)
        uint cksum = 0;
        for (int i = 1; i < 256 && i * 4 + 4 <= rootInode.Length; i++)
            cksum += BinaryPrimitives.ReadUInt32LittleEndian(rootInode.AsSpan(i * 4));
        BinaryPrimitives.WriteUInt32LittleEndian(rootInode.AsSpan(0), cksum);

        fs.Write(rootInode);

        // Write empty root directory data block at block 4
        // Contains "." and ".." entries
        long rootDirDataOffset = (long)partitionStart * SectorSize + 4L * PfsDefaultZoneSize;
        fs.Position = rootDirDataOffset;

        var dirBlock = new byte[PfsDefaultZoneSize];
        int dOff = 0;

        // "." entry
        BinaryPrimitives.WriteUInt32LittleEndian(dirBlock.AsSpan(dOff), 3); // inode = root
        dirBlock[dOff + 4] = 0; // sub
        dirBlock[dOff + 5] = 1; // pLen
        BinaryPrimitives.WriteUInt16LittleEndian(dirBlock.AsSpan(dOff + 6), FioModeDirectory | 12); // aLen
        dirBlock[dOff + 8] = (byte)'.';
        dOff += 12;

        // ".." entry
        BinaryPrimitives.WriteUInt32LittleEndian(dirBlock.AsSpan(dOff), 3); // inode = root (parent)
        dirBlock[dOff + 4] = 0; // sub
        dirBlock[dOff + 5] = 2; // pLen
        BinaryPrimitives.WriteUInt16LittleEndian(dirBlock.AsSpan(dOff + 6), FioModeDirectory | 12); // aLen
        dirBlock[dOff + 8] = (byte)'.';
        dirBlock[dOff + 9] = (byte)'.';

        fs.Write(dirBlock);
    }

    private static void WriteAsciiString(byte[] data, int offset, string value, int maxLen)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        int len = Math.Min(bytes.Length, maxLen - 1);
        Array.Copy(bytes, 0, data, offset, len);
        if (offset + len < data.Length)
            data[offset + len] = 0; // null terminator
    }

    private static void WriteApaTimestamp(Span<byte> span, DateTime dt)
    {
        span[0] = 0; // reserved
        span[1] = (byte)dt.Second;
        span[2] = (byte)dt.Minute;
        span[3] = (byte)dt.Hour;
        span[4] = (byte)dt.Day;
        span[5] = (byte)dt.Month;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), (ushort)dt.Year);
    }

    private static void WritePfsTimestamp(Span<byte> span, DateTime dt)
    {
        span[0] = 0; // reserved
        span[1] = (byte)dt.Second;
        span[2] = (byte)dt.Minute;
        span[3] = (byte)dt.Hour;
        span[4] = (byte)dt.Day;
        span[5] = (byte)dt.Month;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), (ushort)dt.Year);
    }
}
