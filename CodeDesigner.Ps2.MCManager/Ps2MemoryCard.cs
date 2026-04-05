using System.Text;

namespace CodeDesigner.Ps2.MCManager;

/// <summary>
/// Reads and writes PlayStation 2 memory card images (.ps2 format, as used by PCSX2).
/// Provides the same core functionality as mymc/myMCpp: list saves, export, import,
/// delete and create blank cards.
/// </summary>
public sealed class Ps2MemoryCard : IDisposable
{
    // ── Format constants ────────────────────────────────────────────────────
    private const string Magic         = "Sony PS2 Memory Card Format ";
    private const int    SpareSize     = 16;             // ECC spare bytes per page

    // FAT values — matching the real PS2 / PCSX2 memory card format.
    //   Bit 31 (FatAllocBit) is SET on every in-use (allocated) FAT entry.
    //   Free clusters have bit 31 clear.
    //   End-of-chain is 0xFFFFFFFF (= FatAllocBit | FatClusterMask).
    private const uint FatAllocBit   = 0x8000_0000;  // allocated flag
    private const uint FatClusterMask = 0x7FFF_FFFF;  // lower-31-bit cluster index
    private const uint FatFree       = 0x7FFF_FFFF;  // free / unallocated end-of-chain
    private const uint FatEndOfChain = 0xFFFF_FFFF;  // last cluster in chain

    // Directory entry mode flags
    private const ushort DfRead    = 0x0001;
    private const ushort DfWrite   = 0x0002;
    private const ushort DfExecute = 0x0004;
    private const ushort DfFile    = 0x0010;
    private const ushort DfDir     = 0x0020;
    private const ushort Df0400    = 0x0400;
    private const ushort DfHidden  = 0x2000;
    private const ushort DfExists  = 0x8000;

    // Composite mode values used in several places
    private const ushort ModeDirNormal  = DfDir  | DfExists | DfRead | DfWrite | DfExecute | Df0400; // 0x8427
    private const ushort ModeFileNormal = DfFile | DfExists | DfRead | DfWrite | DfExecute | Df0400; // 0x8417
    private const ushort ModeDirHidden  = DfDir  | DfExists | DfWrite | DfExecute | Df0400 | DfHidden; // 0xA426

    // ── Card state ──────────────────────────────────────────────────────────
    private FileStream? _stream;
    private uint _pageSize;
    private uint _pagesPerCluster;
    private uint _rawPageSize;      // page_size (no ECC) or page_size+spare (ECC)
    private uint _clustersPerCard;
    private uint _allocOffset;      // first allocatable cluster (absolute)
    private uint _allocEnd;         // number of allocatable clusters
    private uint _rootDirCluster;   // root dir cluster index (relative to _allocOffset)
    private uint[] _fat = [];
    private readonly uint[] _ifcList = new uint[32];
    private bool _hasEcc;

    public void Dispose() => _stream?.Dispose();

    // ── Internal directory entry ─────────────────────────────────────────────
    private struct DirEntry
    {
        public ushort   Mode;
        public uint     Length;      // bytes for files, entry count for dirs
        public DateTime Created;     // UTC
        public uint     Cluster;     // relative to _allocOffset
        public uint     DirEntryIdx;
        public DateTime Modified;    // UTC
        public string   Name;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Opens an existing PS2 memory card image.</summary>
    public static Ps2MemoryCard Open(string path)
    {
        var mc = new Ps2MemoryCard();
        mc._stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        mc.ReadSuperblock();
        mc.LoadFat();
        return mc;
    }

    /// <summary>Creates a new blank 8 MB (or custom size) PS2 memory card image.</summary>
    public static void Create(string path, int sizeInMb = 8)
    {
        const uint pageSize           = 512;
        const uint pagesPerCluster    = 2;
        const uint clusterSize        = pageSize * pagesPerCluster;  // 1024
        const uint pagesPerEraseBlock = 16;
        const uint firstIfc           = 8;
        const uint entriesPerCluster  = clusterSize / 4;             // 256

        uint clustersPerCard = (uint)(sizeInMb * 1024 * 1024) / clusterSize;
        uint pagesPerCard    = clustersPerCard * pagesPerCluster;

        // FAT layout
        uint fatClusters         = (clustersPerCard + entriesPerCluster - 1) / entriesPerCluster;
        if (fatClusters > entriesPerCluster) fatClusters = entriesPerCluster;
        const uint indirectFatClusters = 1;
        uint allocOffset         = firstIfc + indirectFatClusters + fatClusters;

        uint eraseBlocksPerCard    = pagesPerCard / pagesPerEraseBlock;
        uint goodBlock1            = eraseBlocksPerCard - 1;
        uint goodBlock2            = eraseBlocksPerCard - 2;
        uint clustersPerEraseBlock = pagesPerEraseBlock / pagesPerCluster;
        uint allocEnd              = (goodBlock2 * clustersPerEraseBlock) - allocOffset;

        long totalBytes = (long)clustersPerCard * pagesPerCluster * pageSize;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        fs.SetLength(totalBytes);

        void WritePage(uint pageNum, byte[] data)
        {
            fs.Seek((long)pageNum * pageSize, SeekOrigin.Begin);
            fs.Write(data, 0, (int)pageSize);
        }

        void WriteClusterLocal(uint clusterNum, byte[] data)
        {
            for (uint i = 0; i < pagesPerCluster; i++)
            {
                var page = new byte[pageSize];
                Buffer.BlockCopy(data, (int)(i * pageSize), page, 0, (int)pageSize);
                WritePage(clusterNum * pagesPerCluster + i, page);
            }
        }

        // 1. Superblock at page 0
        var sb = new byte[pageSize];
        Encoding.ASCII.GetBytes(Magic).CopyTo(sb, 0);
        Encoding.ASCII.GetBytes("1.2.0.0").CopyTo(sb, 28);
        WriteU16(sb, 40, (ushort)pageSize);
        WriteU16(sb, 42, (ushort)pagesPerCluster);
        WriteU16(sb, 44, (ushort)pagesPerEraseBlock);
        WriteU16(sb, 46, 0xFF00);
        WriteU32(sb, 48, clustersPerCard);
        WriteU32(sb, 52, allocOffset);
        WriteU32(sb, 56, allocEnd);
        WriteU32(sb, 60, 0);          // rootdir_cluster = 0 (relative to alloc_offset)
        WriteU32(sb, 64, goodBlock1);
        WriteU32(sb, 68, goodBlock2);
        // IFC list at offset 0x50 = 80
        WriteU32(sb, 80, firstIfc);
        for (int i = 1; i < 32; i++) WriteU32(sb, 80 + i * 4, 0xFFFFFFFF);
        // Bad block list at 0xD0 = 208 — none
        for (int i = 0; i < 32; i++) WriteU32(sb, 208 + i * 4, 0xFFFFFFFF);
        sb[336] = 0x02; // card_type = PS2
        sb[337] = 0x2B; // card_flags
        WritePage(0, sb);

        // 2. Indirect FAT cluster at firstIfc (cluster 8)
        var ifcData = new byte[clusterSize];
        for (int i = 0; i < (int)fatClusters; i++)
            WriteU32(ifcData, (uint)(i * 4), firstIfc + indirectFatClusters + (uint)i);
        for (int i = (int)fatClusters; i < (int)entriesPerCluster; i++)
            WriteU32(ifcData, (uint)(i * 4), 0xFFFFFFFF);
        WriteClusterLocal(firstIfc, ifcData);

        // 3. FAT clusters — all free except cluster 0 (root dir = end-of-chain)
        var fat = new uint[allocEnd];
        for (uint i = 0; i < allocEnd; i++) fat[i] = FatFree;
        fat[0] = FatEndOfChain;

        for (uint fatClusterIdx = 0; fatClusterIdx < fatClusters; fatClusterIdx++)
        {
            var fatClusterData = new byte[clusterSize];
            uint baseEntry = fatClusterIdx * entriesPerCluster;
            for (uint j = 0; j < entriesPerCluster; j++)
            {
                uint entryIdx = baseEntry + j;
                uint val = entryIdx < (uint)fat.Length ? fat[entryIdx] : FatFree;
                WriteU32(fatClusterData, j * 4, val);
            }
            WriteClusterLocal(firstIfc + indirectFatClusters + fatClusterIdx, fatClusterData);
        }

        // 4. Root directory at absolute cluster allocOffset (relative cluster 0)
        var rootDirData = new byte[clusterSize];
        var dot    = MakeDirEntryBytes(".",  ModeDirNormal, cluster: 0, length: 2,
                                       DateTime.UtcNow, DateTime.UtcNow);
        var dotdot = MakeDirEntryBytes("..", ModeDirHidden, cluster: 0, length: 0,
                                       DateTime.UtcNow, DateTime.UtcNow);
        Buffer.BlockCopy(dot,    0, rootDirData,   0, 512);
        Buffer.BlockCopy(dotdot, 0, rootDirData, 512, 512);
        WriteClusterLocal(allocOffset, rootDirData);

        fs.Flush();
    }

    /// <summary>Lists all save-game directories on this memory card.</summary>
    public MemoryCardEntry[] ListSaves()
    {
        EnsureOpen();
        var rootEntries = ReadDirEntries(_rootDirCluster);
        var saves = new List<MemoryCardEntry>();

        foreach (var entry in rootEntries)
        {
            if ((entry.Mode & DfExists) == 0) continue;
            if ((entry.Mode & DfDir)    == 0) continue;
            if (entry.Name is "." or "..") continue;

            var files = ReadDirEntries(entry.Cluster);
            var fileList = files
                .Where(f => (f.Mode & DfExists) != 0 && (f.Mode & DfFile) != 0)
                .Select(f => new MemoryCardFile { Name = f.Name, Size = f.Length })
                .ToArray();

            saves.Add(new MemoryCardEntry
            {
                Name       = entry.Name,
                FileCount  = (uint)fileList.Length,
                Created    = entry.Created,
                Modified   = entry.Modified,
                TotalBytes = fileList.Sum(f => f.Size),
                Files      = fileList,
            });
        }

        return [.. saves];
    }

    /// <summary>Exports all files in a named save to <paramref name="outputDir"/>.</summary>
    public void ExportSave(string saveName, string outputDir)
    {
        EnsureOpen();
        var (entry, _) = FindSave(saveName);
        Directory.CreateDirectory(outputDir);

        foreach (var f in ReadDirEntries(entry.Cluster))
        {
            if ((f.Mode & DfExists) == 0 || (f.Mode & DfFile) == 0) continue;
            var data = ReadFileData(f);
            File.WriteAllBytes(Path.Combine(outputDir, f.Name), data);
        }
    }

    /// <summary>
    /// Imports a save from a host directory into the memory card.
    /// The save name is taken from the basename of <paramref name="inputDir"/>.
    /// </summary>
    public void ImportSave(string inputDir, bool overwrite = false)
    {
        EnsureOpen();
        string saveName = Path.GetFileName(inputDir.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(saveName))
            throw new ArgumentException("Could not determine save name from directory path.", nameof(inputDir));

        var rootEntries = ReadDirEntries(_rootDirCluster);
        int existingIdx = rootEntries.FindIndex(e =>
            (e.Mode & DfExists) != 0 && (e.Mode & DfDir) != 0 &&
            string.Equals(e.Name, saveName, StringComparison.OrdinalIgnoreCase));

        if (existingIdx >= 0)
        {
            if (!overwrite)
                throw new InvalidOperationException(
                    $"Save '{saveName}' already exists. Use overwrite=true to replace it.");
            DeleteSaveInternal(rootEntries[existingIdx], ref rootEntries);
            // Refresh root entries after deletion
            rootEntries = ReadDirEntries(_rootDirCluster);
        }

        var hostFiles = Directory.GetFiles(inputDir)
            .Select(p => new { Path = p, Name = Path.GetFileName(p), Data = File.ReadAllBytes(p) })
            .ToArray();

        uint saveDirCluster = AllocateSingleCluster();
        var  now            = DateTime.UtcNow;

        // Build save directory entries: ".", "..", then one per file
        var saveDirEntries = new List<DirEntry>
        {
            new() { Mode = ModeDirNormal, Name = ".",  Cluster = saveDirCluster,
                    Length = (uint)(2 + hostFiles.Length), Created = now, Modified = now },
            new() { Mode = ModeDirHidden, Name = "..", Cluster = _rootDirCluster,
                    Length = 0, Created = now, Modified = now },
        };

        foreach (var hf in hostFiles)
        {
            uint[] chain = AllocateChain(ClusterCount((uint)hf.Data.Length));
            WriteFileData(chain, hf.Data);
            saveDirEntries.Add(new DirEntry
            {
                Mode     = ModeFileNormal,
                Name     = hf.Name,
                Cluster  = chain[0],
                Length   = (uint)hf.Data.Length,
                Created  = now,
                Modified = now,
            });
        }

        WriteDirEntries(saveDirCluster, saveDirEntries);

        // Add save entry to root directory
        var newEntry = new DirEntry
        {
            Mode     = ModeDirNormal,
            Name     = saveName,
            Cluster  = saveDirCluster,
            Length   = (uint)(2 + hostFiles.Length),
            Created  = now,
            Modified = now,
        };

        int freeSlot = rootEntries.FindIndex(e =>
            (e.Mode & DfExists) == 0 && e.Name is not ("." or ".."));
        if (freeSlot >= 0)
            rootEntries[freeSlot] = newEntry;
        else
            rootEntries.Add(newEntry);

        // Keep root "." entry length in sync
        int dotIdx = rootEntries.FindIndex(e => e.Name == ".");
        if (dotIdx >= 0)
        {
            var d = rootEntries[dotIdx];
            d.Length = (uint)rootEntries.Count;
            rootEntries[dotIdx] = d;
        }

        WriteDirEntries(_rootDirCluster, rootEntries);
        WriteFatToCard();
        _stream!.Flush();
    }

    /// <summary>
    /// Reads the raw bytes of a specific file within a named save.
    /// </summary>
    /// <param name="saveName">Save directory name (e.g. <c>"BASCUS-97133BUILTIN"</c>).</param>
    /// <param name="fileName">File name within the save directory (e.g. <c>"ICON.SYS"</c>).</param>
    /// <returns>Raw file contents.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the save or file is not found on the card.
    /// </exception>
    public byte[] ReadFile(string saveName, string fileName)
    {
        EnsureOpen();
        var (entry, _) = FindSave(saveName);
        foreach (var f in ReadDirEntries(entry.Cluster))
        {
            if ((f.Mode & DfExists) == 0 || (f.Mode & DfFile) == 0) continue;
            if (string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase))
                return ReadFileData(f);
        }
        throw new KeyNotFoundException($"File '{fileName}' not found in save '{saveName}'.");
    }

    /// <summary>Deletes a named save from the memory card.</summary>
    public void DeleteSave(string saveName)
    {
        EnsureOpen();
        var rootEntries = ReadDirEntries(_rootDirCluster);
        int idx = rootEntries.FindIndex(e =>
            (e.Mode & DfExists) != 0 && (e.Mode & DfDir) != 0 &&
            string.Equals(e.Name, saveName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            throw new KeyNotFoundException($"Save '{saveName}' not found on memory card.");

        DeleteSaveInternal(rootEntries[idx], ref rootEntries);
        WriteFatToCard();
        _stream!.Flush();
    }

    // ── Superblock / FAT I/O ─────────────────────────────────────────────────

    private void ReadSuperblock()
    {
        // First pass: assume no-ECC, 512-byte pages
        _pageSize        = 512;
        _pagesPerCluster = 2;
        _rawPageSize     = 512;

        var page = ReadRawPage(0);

        var magic = Encoding.ASCII.GetString(page, 0, 28);
        if (magic != Magic)
            throw new InvalidDataException("Not a valid PS2 memory card image (bad magic).");

        _pageSize        = ReadU16(page, 40);
        _pagesPerCluster = ReadU16(page, 42);
        _clustersPerCard = ReadU32(page, 48);
        _allocOffset     = ReadU32(page, 52);
        _allocEnd        = ReadU32(page, 56);
        _rootDirCluster  = ReadU32(page, 60);

        // IFC list at offset 0x50 = 80
        for (int i = 0; i < 32; i++)
            _ifcList[i] = ReadU32(page, 80 + i * 4);

        // ECC detection: compare file size to expected sizes
        uint totalPages      = _clustersPerCard * _pagesPerCluster;
        long expectedWithEcc = (long)totalPages * (_pageSize + SpareSize);
        long fileSize        = _stream!.Length;

        if (fileSize >= expectedWithEcc)
        {
            _hasEcc      = true;
            _rawPageSize = _pageSize + SpareSize;
        }
        else
        {
            _hasEcc      = false;
            _rawPageSize = _pageSize;
        }

        if (_allocEnd == 0 || _allocEnd > 1_000_000)
            _allocEnd = _clustersPerCard;
    }

    private void LoadFat()
    {
        _fat = new uint[_allocEnd];
        for (uint i = 0; i < _allocEnd; i++) _fat[i] = FatFree;

        uint entriesPerCluster = _pageSize * _pagesPerCluster / 4;
        uint fatEntry = 0;

        for (int ifcIdx = 0; ifcIdx < 32 && fatEntry < _allocEnd; ifcIdx++)
        {
            uint ifcCluster = _ifcList[ifcIdx];
            if (ifcCluster == 0 || ifcCluster == 0xFFFF_FFFF) continue;

            var ifcData = ReadCluster(ifcCluster);
            for (uint j = 0; j < entriesPerCluster && fatEntry < _allocEnd; j++)
            {
                uint fatClusterPtr = ReadU32(ifcData, j * 4);
                if (fatClusterPtr == 0 || fatClusterPtr == 0xFFFF_FFFF) continue;

                var fatClusterData = ReadCluster(fatClusterPtr);
                for (uint k = 0; k < entriesPerCluster && fatEntry < _allocEnd; k++, fatEntry++)
                    _fat[fatEntry] = ReadU32(fatClusterData, k * 4);
            }
        }
    }

    private void WriteFatToCard()
    {
        uint entriesPerCluster = _pageSize * _pagesPerCluster / 4;

        for (int ifcIdx = 0; ifcIdx < 32; ifcIdx++)
        {
            uint ifcCluster = _ifcList[ifcIdx];
            if (ifcCluster == 0 || ifcCluster == 0xFFFF_FFFF) continue;

            var ifcData = ReadCluster(ifcCluster);
            for (uint j = 0; j < entriesPerCluster; j++)
            {
                uint fatClusterPtr = ReadU32(ifcData, j * 4);
                if (fatClusterPtr == 0 || fatClusterPtr == 0xFFFF_FFFF) continue;

                // baseEntry: which FAT entries does this FAT cluster cover?
                uint baseEntry = (uint)(ifcIdx * entriesPerCluster * entriesPerCluster
                                        + j * entriesPerCluster);
                var fatClusterData = new byte[_pageSize * _pagesPerCluster];
                for (uint k = 0; k < entriesPerCluster; k++)
                {
                    uint entryIdx = baseEntry + k;
                    uint val = entryIdx < (uint)_fat.Length ? _fat[entryIdx] : FatFree;
                    WriteU32(fatClusterData, k * 4, val);
                }
                WriteCluster(fatClusterPtr, fatClusterData);
            }
        }
    }

    // ── Raw page / cluster I/O ───────────────────────────────────────────────

    private byte[] ReadRawPage(uint pageNum)
    {
        uint rawPs  = _rawPageSize == 0 ? 512u : _rawPageSize;
        uint pagePs = _pageSize    == 0 ? 512u : _pageSize;
        _stream!.Seek((long)pageNum * rawPs, SeekOrigin.Begin);
        var buf  = new byte[pagePs];
        int read = _stream.Read(buf, 0, (int)pagePs);
        if (read != (int)pagePs) throw new EndOfStreamException("Unexpected end of memory card file.");
        return buf;
    }

    private byte[] ReadCluster(uint absClusterNum)
    {
        uint ppc    = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps     = _pageSize        == 0 ? 512u : _pageSize;
        var  result = new byte[ppc * ps];
        for (uint i = 0; i < ppc; i++)
        {
            var page = ReadRawPage(absClusterNum * ppc + i);
            Buffer.BlockCopy(page, 0, result, (int)(i * ps), (int)ps);
        }
        return result;
    }

    private void WriteCluster(uint absClusterNum, byte[] data)
    {
        uint ppc = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps  = _pageSize        == 0 ? 512u : _pageSize;
        for (uint i = 0; i < ppc; i++)
        {
            var page = new byte[ps];
            Buffer.BlockCopy(data, (int)(i * ps), page, 0, (int)ps);
            WriteRawPage(absClusterNum * ppc + i, page);
        }
    }

    private void WriteRawPage(uint pageNum, byte[] data)
    {
        uint rawPs = _rawPageSize == 0 ? 512u : _rawPageSize;
        _stream!.Seek((long)pageNum * rawPs, SeekOrigin.Begin);
        _stream.Write(data, 0, data.Length);
        if (_hasEcc && _rawPageSize > _pageSize)
        {
            // Write zero spare bytes — PCSX2 recomputes ECC and accepts zeroed spare
            _stream.Write(new byte[_rawPageSize - _pageSize], 0, (int)(_rawPageSize - _pageSize));
        }
    }

    // ── Directory entry I/O ──────────────────────────────────────────────────

    private List<DirEntry> ReadDirEntries(uint clusterRel)
    {
        uint ppc          = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps           = _pageSize        == 0 ? 512u : _pageSize;
        uint cs           = ppc * ps;
        const int EntSize = 512;
        int  perCluster   = (int)cs / EntSize;

        var  entries = new List<DirEntry>();
        uint current = clusterRel;

        while (true)
        {
            var clusterData = ReadCluster(_allocOffset + current);
            for (int i = 0; i < perCluster; i++)
            {
                var raw = new byte[EntSize];
                Buffer.BlockCopy(clusterData, i * EntSize, raw, 0, EntSize);
                entries.Add(ParseDirEntry(raw));
            }

            if (!TryNextCluster(current, out uint next)) break;
            current = next;
        }

        return entries;
    }

    private void WriteDirEntries(uint clusterRel, List<DirEntry> entries)
    {
        uint ppc          = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps           = _pageSize        == 0 ? 512u : _pageSize;
        uint cs           = ppc * ps;
        const int EntSize = 512;
        int  perCluster   = (int)cs / EntSize;
        int  clustersNeeded = Math.Max(1, (entries.Count + perCluster - 1) / perCluster);

        // Build or extend the existing cluster chain
        var chain = new List<uint> { clusterRel };
        uint current = clusterRel;
        while (true)
        {
            if (!TryNextCluster(current, out uint next)) break;
            chain.Add(next);
            current = next;
        }

        while (chain.Count < clustersNeeded)
        {
            uint newCluster = AllocateSingleCluster();
            _fat[chain[^1]] = FatAllocBit | newCluster;
            chain.Add(newCluster);
        }
        _fat[chain[^1]] = FatEndOfChain;

        for (int ci = 0; ci < chain.Count; ci++)
        {
            var clusterData = new byte[cs];
            for (int ei = 0; ei < perCluster; ei++)
            {
                int entryIdx = ci * perCluster + ei;
                if (entryIdx < entries.Count)
                {
                    var packed = PackDirEntry(entries[entryIdx]);
                    Buffer.BlockCopy(packed, 0, clusterData, ei * EntSize, EntSize);
                }
            }
            WriteCluster(_allocOffset + chain[ci], clusterData);
        }
    }

    // ── File data I/O ────────────────────────────────────────────────────────

    private byte[] ReadFileData(DirEntry entry)
    {
        uint ppc     = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps      = _pageSize        == 0 ? 512u : _pageSize;
        uint cs      = ppc * ps;
        var  allData = new List<byte>((int)(entry.Length + cs));
        uint current = entry.Cluster;

        while (true)
        {
            allData.AddRange(ReadCluster(_allocOffset + current));
            if (!TryNextCluster(current, out uint next)) break;
            current = next;
        }

        var result = allData.ToArray();
        if (entry.Length < (uint)result.Length)
            Array.Resize(ref result, (int)entry.Length);
        return result;
    }

    private void WriteFileData(uint[] chain, byte[] data)
    {
        uint ppc = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps  = _pageSize        == 0 ? 512u : _pageSize;
        uint cs  = ppc * ps;

        for (int i = 0; i < chain.Length; i++)
        {
            var clusterData = new byte[cs];
            int srcOffset   = i * (int)cs;
            int copyLen     = Math.Min((int)cs, data.Length - srcOffset);
            if (copyLen > 0) Buffer.BlockCopy(data, srcOffset, clusterData, 0, copyLen);
            WriteCluster(_allocOffset + chain[i], clusterData);
        }
    }

    // ── Cluster allocation ───────────────────────────────────────────────────

    /// <summary>
    /// Follows one step in a FAT chain.  Returns <see langword="false"/> and leaves
    /// <paramref name="next"/> undefined when <paramref name="current"/> is the last
    /// cluster in its chain (end-of-chain) or a free/unallocated cluster.
    /// Handles both the real PS2/PCSX2 format (chain links have bit 31 set) and
    /// the legacy internal format (plain cluster indices, bit 31 clear).
    /// </summary>
    private bool TryNextCluster(uint current, out uint next)
    {
        next = 0; // only meaningful when returning true; satisfies the compiler for early-exit paths
        if (current >= _allocEnd) return false;
        uint raw = _fat[current];
        // End-of-chain: 0xFFFFFFFF (real PS2 format)
        if (raw == FatEndOfChain) return false;
        // Free / unallocated: bit 31 clear (also covers legacy FatFree = 0x7FFFFFFF)
        if ((raw & FatAllocBit) == 0) return false;
        // Allocated chain link: strip bit 31 to get next cluster index
        next = raw & FatClusterMask;
        return next < _allocEnd;
    }

    private uint AllocateSingleCluster()
    {
        // A free cluster has bit 31 (FatAllocBit) clear.
        for (uint i = 0; i < _allocEnd; i++)
        {
            if ((_fat[i] & FatAllocBit) == 0)
            {
                _fat[i] = FatEndOfChain;
                return i;
            }
        }
        throw new InvalidOperationException("Memory card is full — no free clusters available.");
    }

    private uint[] AllocateChain(uint count)
    {
        if (count == 0) return [];
        var chain = new uint[count];
        for (uint i = 0; i < count; i++)
            chain[i] = AllocateSingleCluster();
        for (int i = 0; i < chain.Length - 1; i++)
            _fat[chain[i]] = FatAllocBit | chain[i + 1];
        _fat[chain[^1]] = FatEndOfChain;
        return chain;
    }

    private uint ClusterCount(uint byteSize)
    {
        uint ppc = _pagesPerCluster == 0 ? 2u : _pagesPerCluster;
        uint ps  = _pageSize        == 0 ? 512u : _pageSize;
        uint cs  = ppc * ps;
        return (byteSize + cs - 1) / cs;
    }

    private void FreeClusterChain(uint startClusterRel)
    {
        uint current = startClusterRel;
        while (true)
        {
            if (current >= _allocEnd) break;
            uint raw = _fat[current];
            _fat[current] = FatFree;
            if (raw == FatEndOfChain || (raw & FatAllocBit) == 0) break;
            uint next = raw & FatClusterMask;
            if (next >= _allocEnd) break;
            current = next;
        }
    }

    // ── Save-level helpers ───────────────────────────────────────────────────

    private void DeleteSaveInternal(DirEntry saveEntry, ref List<DirEntry> rootEntries)
    {
        foreach (var f in ReadDirEntries(saveEntry.Cluster))
        {
            if ((f.Mode & DfFile) == 0 || (f.Mode & DfExists) == 0) continue;
            FreeClusterChain(f.Cluster);
        }
        FreeClusterChain(saveEntry.Cluster);

        int idx = rootEntries.FindIndex(e =>
            (e.Mode & DfExists) != 0 &&
            string.Equals(e.Name, saveEntry.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            var e = rootEntries[idx];
            e.Mode = 0;
            rootEntries[idx] = e;
        }

        WriteDirEntries(_rootDirCluster, rootEntries);
    }

    private (DirEntry entry, int rootIdx) FindSave(string saveName)
    {
        var rootEntries = ReadDirEntries(_rootDirCluster);
        for (int i = 0; i < rootEntries.Count; i++)
        {
            var e = rootEntries[i];
            if ((e.Mode & DfExists) == 0 || (e.Mode & DfDir) == 0) continue;
            if (string.Equals(e.Name, saveName, StringComparison.OrdinalIgnoreCase))
                return (e, i);
        }
        throw new KeyNotFoundException($"Save '{saveName}' not found on memory card.");
    }

    // ── Directory entry serialisation ────────────────────────────────────────

    private static DirEntry ParseDirEntry(byte[] raw)
    {
        var e = new DirEntry
        {
            Mode         = (ushort)(raw[0] | (raw[1] << 8)),
            Length       = ReadU32(raw, 4),
            Created      = UnpackTod(raw, 8),
            Cluster      = ReadU32(raw, 16),
            DirEntryIdx  = ReadU32(raw, 20),
            Modified     = UnpackTod(raw, 24),
        };
        // Name: null-terminated ASCII starting at offset 64
        int nameEnd = 64;
        while (nameEnd < raw.Length && raw[nameEnd] != 0) nameEnd++;
        e.Name = Encoding.ASCII.GetString(raw, 64, nameEnd - 64);
        return e;
    }

    private static byte[] PackDirEntry(DirEntry e)
    {
        var raw = new byte[512];
        raw[0] = (byte)(e.Mode & 0xFF);
        raw[1] = (byte)(e.Mode >> 8);
        WriteU32(raw, 4,  e.Length);
        PackTod(e.Created,  raw, 8);
        WriteU32(raw, 16, e.Cluster);
        WriteU32(raw, 20, e.DirEntryIdx);
        PackTod(e.Modified, raw, 24);
        var nameBytes = Encoding.ASCII.GetBytes(e.Name ?? string.Empty);
        Buffer.BlockCopy(nameBytes, 0, raw, 64, Math.Min(nameBytes.Length, 447));
        return raw;
    }

    private static byte[] MakeDirEntryBytes(string name, ushort mode, uint cluster,
                                             uint length, DateTime created, DateTime modified)
        => PackDirEntry(new DirEntry
        {
            Name = name, Mode = mode, Cluster = cluster, Length = length,
            Created = created, Modified = modified,
        });

    // ── PS2McTod (time-of-day) ───────────────────────────────────────────────

    /// <summary>
    /// Parses a PS2 8-byte timestamp. The card stores time in JST (UTC+9);
    /// this returns UTC.
    /// </summary>
    private static DateTime UnpackTod(byte[] raw, int offset)
    {
        byte sec   = raw[offset + 1];
        byte min   = raw[offset + 2];
        byte hour  = raw[offset + 3];
        byte day   = raw[offset + 4];
        byte month = raw[offset + 5];
        int  year  = raw[offset + 6] | (raw[offset + 7] << 8);

        if (month < 1 || month > 12 || day < 1 || day > 31 || year < 1 || year > 9999)
            return DateTime.UnixEpoch;

        try
        {
            var jst = new DateTime(year, month, day, hour, min, sec, DateTimeKind.Unspecified);
            return DateTime.SpecifyKind(jst.AddHours(-9), DateTimeKind.Utc);
        }
        catch { return DateTime.UnixEpoch; }
    }

    /// <summary>
    /// Packs a UTC <see cref="DateTime"/> into a PS2 8-byte timestamp (JST = UTC+9).
    /// </summary>
    private static void PackTod(DateTime utc, byte[] raw, int offset)
    {
        var jst = utc.AddHours(9);
        raw[offset + 0] = 0;
        raw[offset + 1] = (byte)jst.Second;
        raw[offset + 2] = (byte)jst.Minute;
        raw[offset + 3] = (byte)jst.Hour;
        raw[offset + 4] = (byte)jst.Day;
        raw[offset + 5] = (byte)jst.Month;
        raw[offset + 6] = (byte)(jst.Year & 0xFF);
        raw[offset + 7] = (byte)((jst.Year >> 8) & 0xFF);
    }

    // ── Little-endian helpers ────────────────────────────────────────────────

    private static uint ReadU32(byte[] buf, int offset) =>
        (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

    private static uint ReadU32(byte[] buf, uint offset) => ReadU32(buf, (int)offset);

    private static ushort ReadU16(byte[] buf, int offset) =>
        (ushort)(buf[offset] | (buf[offset + 1] << 8));

    private static void WriteU32(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteU32(byte[] buf, uint offset, uint value) => WriteU32(buf, (int)offset, value);

    private static void WriteU16(byte[] buf, int offset, ushort value)
    {
        buf[offset]     = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
    }

    private void EnsureOpen()
    {
        if (_stream == null || !_stream.CanRead)
            throw new InvalidOperationException("No memory card is open.");
    }
}
