using CodeDesigner.Ps2.ZDBManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeDesigner.Ps2.Compression
{
    // =========================================================================
    // Public entry type
    // =========================================================================

    /// <summary>
    /// Represents a single file stored inside a ZDB archive.
    /// </summary>
    public sealed class ZdbEntry
    {
        /// <summary>File name without path (e.g. "texture.bin").</summary>
        public string Name { get; internal set; } = string.Empty;

        /// <summary>Full internal archive path as stored on disk (backslash-separated).</summary>
        public string FullPath { get; internal set; } = string.Empty;

        /// <summary>Raw file data.</summary>
        public byte[] Data { get; internal set; } = Array.Empty<byte>();

        /// <summary>File size in bytes.</summary>
        public uint Size => (uint)Data.Length;

        // Type-1 only
        internal uint VarName { get; set; }
        internal uint DbId { get; set; }
        internal uint Checksum { get; set; }

        // Type-2 only — preserved for lossless round-trip.
        // The 0x40-byte name field contains non-zero garbage after the null terminator
        // in real archives (confirmed in M51.ZDB). The word at entry+0x58 is also
        // non-zero. We store both raw so Save() writes back exactly what was read.
        internal byte[]? RawNameField { get; set; }   // 0x40 bytes verbatim from file
        internal uint TrailingWord { get; set; }   // word at entry+0x58
    }

    // =========================================================================
    // Archive
    // =========================================================================

    /// <summary>
    /// Reads, writes and manipulates ZDB archives (PS2 game data format).
    /// Supports Type-1 (magic 0xC8) and Type-2 (magic 0xFC) variants.
    /// </summary>
    public sealed class ZdbArchive : IDisposable
    {
        // ------------------------------------------------------------------
        // Private header structs
        // ------------------------------------------------------------------

        private struct Type1Header
        {
            public uint MainHeaderSize;    // 0x00  always 0xC8
            public uint FileHeaderSize;    // 0x04  always 0x58
            public uint Version;           // 0x08
            public uint ArchiveHeaderSize; // 0x0C  main + all file headers
            public uint BodySize;          // 0x10
            public uint UnusedId;          // 0x14  sentinel 0x4532ABCD
            public uint Compression;       // 0x18
            public ushort Errors;            // 0x1C
            public ushort Warnings;          // 0x1E
            // 0x20-0xC3  padding
            // 0xC4  FileCount
        }

        private struct Type2Header
        {
            public uint MainHeaderSize; // 0x00  always 0xFC
            public uint Unknown1;       // 0x04
            public uint UnknownId;      // 0x08  sentinel 0x12345678
            public uint Unknown2;       // 0x0C
            public uint Unknown3;       // 0x10
            public string Version;        // 0x14-0x97  null-terminated
            // 0x98  FileCount
            // 0x9C  FileHeaderSize  always 0x5C
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private int _archiveType; // 0 = none, 1, 2
        private Type1Header _head1;
        private Type2Header _head2;

        private readonly List<ZdbEntry> _entries = new List<ZdbEntry>();

        // ------------------------------------------------------------------
        // Factory methods
        // ------------------------------------------------------------------

        /// <summary>Opens a ZDB archive from disk.</summary>
        /// <exception cref="FileNotFoundException"/>
        /// <exception cref="InvalidDataException"/>
        public static ZdbArchive Open(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Archive file not found.", filePath);

            var archive = new ZdbArchive();
            archive.Load(File.ReadAllBytes(filePath));
            return archive;
        }

        /// <summary>Opens a ZDB archive from a raw byte array.</summary>
        /// <exception cref="InvalidDataException"/>
        public static ZdbArchive FromBytes(byte[] data)
        {
            var archive = new ZdbArchive();
            archive.Load(data);
            return archive;
        }

        /// <summary>Creates a new, empty archive of type 1 or 2.</summary>
        /// <exception cref="ArgumentOutOfRangeException"/>
        public static ZdbArchive CreateNew(int type)
        {
            byte[] blank = type switch
            {
                1 => BuildBlankType1(),
                2 => BuildBlankType2(),
                _ => throw new ArgumentOutOfRangeException(nameof(type), "Must be 1 or 2.")
            };
            return FromBytes(blank);
        }

        private ZdbArchive() { }

        // ------------------------------------------------------------------
        // Properties
        // ------------------------------------------------------------------

        /// <summary>Archive variant (1 or 2). Returns 0 when nothing is loaded.</summary>
        public int ArchiveType => _archiveType;

        /// <summary>All entries in the archive.</summary>
        public IReadOnlyList<ZdbEntry> Entries => _entries;

        /// <summary>Number of entries.</summary>
        public int EntryCount => _entries.Count;

        // ------------------------------------------------------------------
        // Entry access
        // ------------------------------------------------------------------

        /// <summary>Returns the entry at <paramref name="index"/>.</summary>
        public ZdbEntry GetEntry(int index) => _entries[index];

        /// <summary>
        /// Finds an entry by exact full path (case-insensitive). Returns null if not found.
        /// </summary>
        public ZdbEntry? FindEntry(string fullPath) =>
            _entries.FirstOrDefault(e =>
                string.Equals(
                    NormalizeForComparison(e.FullPath),
                    NormalizeForComparison(fullPath),
                    StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------
        // Mutation
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds a file from disk into the archive at the given internal path.
        /// <paramref name="varName"/> is only used by Type-1 archives.
        /// </summary>
        public ZdbEntry AddFile(string diskFilePath, string archivePath, uint varName = 0) =>
            AddEntry(archivePath, File.ReadAllBytes(diskFilePath), varName);

        /// <summary>Adds raw bytes into the archive at the given internal path.</summary>
        public ZdbEntry AddEntry(string archivePath, byte[] data, uint varName = 0)
        {
            EnsureOpen();

            string normalized = NormalizeArchivePath(archivePath);
            var entry = new ZdbEntry
            {
                FullPath = normalized,
                Name = PathFileName(normalized.Replace('\\', '/')),
                Data = (byte[])data.Clone(),
            };

            if (_archiveType == 1)
            {
                entry.VarName = varName;
                entry.Checksum = ZdbChecksum.Compute(data);
            }

            _entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Replaces the data of an existing entry by index.
        /// Automatically recalculates the checksum for Type-1 archives.
        /// </summary>
        public void ReplaceEntryData(int index, byte[] newData)
        {
            EnsureOpen();
            ZdbEntry entry = _entries[index];
            entry.Data = (byte[])newData.Clone();
            if (_archiveType == 1)
                entry.Checksum = ZdbChecksum.Compute(newData);
        }

        /// <summary>Removes the entry at <paramref name="index"/>.</summary>
        public void RemoveEntry(int index)
        {
            EnsureOpen();
            _entries.RemoveAt(index);
        }

        /// <summary>
        /// Removes all entries whose full path matches (case-insensitive).
        /// Returns the number of entries removed.
        /// </summary>
        public int RemoveEntry(string fullPath)
        {
            EnsureOpen();
            string target = NormalizeForComparison(fullPath);
            return _entries.RemoveAll(e =>
                string.Equals(NormalizeForComparison(e.FullPath), target,
                    StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------
        // Extraction
        // ------------------------------------------------------------------

        /// <summary>Writes an entry's data to <paramref name="outputPath"/> on disk.</summary>
        public void ExtractEntry(int index, string outputPath)
        {
            EnsureOpen();
            File.WriteAllBytes(outputPath, _entries[index].Data);
        }

        /// <summary>
        /// Extracts every entry, recreating the internal directory structure
        /// under <paramref name="outputDirectory"/>.
        /// </summary>
        public void ExtractAll(string outputDirectory)
        {
            EnsureOpen();
            foreach (ZdbEntry entry in _entries)
            {
                string dest = Path.Combine(
                    outputDirectory,
                    entry.FullPath.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, entry.Data);
            }
        }

        // ------------------------------------------------------------------
        // Checksum helpers (Type-1 only)
        // ------------------------------------------------------------------

        /// <summary>Recalculates and updates checksums for all Type-1 entries. No-op on Type-2.</summary>
        public void FixAllChecksums()
        {
            if (_archiveType != 1) return;
            foreach (ZdbEntry entry in _entries)
                entry.Checksum = ZdbChecksum.Compute(entry.Data);
        }

        // ------------------------------------------------------------------
        // Save
        // ------------------------------------------------------------------

        /// <summary>Serialises the archive to disk.</summary>
        public void Save(string filePath)
        {
            EnsureOpen();
            File.WriteAllBytes(filePath, ToBytes());
        }

        /// <summary>Serialises the archive to a byte array.</summary>
        public byte[] ToBytes()
        {
            EnsureOpen();
            return _archiveType == 1 ? SerializeType1() : SerializeType2();
        }

        // ------------------------------------------------------------------
        // Directory listing
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a shallow listing of the directory at <paramref name="dirPath"/>
        /// (immediate children only). Works at any nesting depth.
        /// Pass <c>"z:"</c> or <c>""</c> for the archive root.
        ///
        /// <code>
        ///   [DIR] .
        ///   [DIR] ..
        ///   [DIR] subfolderName
        ///   [FILE] 0002 filename.ext
        /// </code>
        /// </summary>
        public string GetDirectoryListing(string dirPath) =>
            BuildListing(dirPath, recursive: false);

        /// <summary>
        /// Returns a full recursive listing of everything under <paramref name="dirPath"/>,
        /// indented by depth. Pass <c>"z:"</c> or <c>""</c> for the entire archive.
        ///
        /// <code>
        ///   [DIR] .
        ///   [DIR] ..
        ///   [DIR]   levels
        ///   [FILE]    0001 world1.bin
        ///   [DIR]   textures
        ///   [FILE]    0003 sky.tex
        /// </code>
        /// </summary>
        public string GetDirectoryListingRecursive(string dirPath) =>
            BuildListing(dirPath, recursive: true);

        private string BuildListing(string dirPath, bool recursive)
        {
            EnsureOpen();

            string prefix = NormalizeForComparison(dirPath);

            // Collect every entry that lives somewhere under this prefix.
            // For each one we record its index and the path relative to prefix.
            var hits = new List<(int Index, ZdbEntry Entry, string Relative)>();

            for (int i = 0; i < _entries.Count; i++)
            {
                ZdbEntry e = _entries[i];
                string entryDir = NormalizeForComparison(GetParentDirectory(e.FullPath));

                if (!IsUnderOrEqual(entryDir, prefix)) continue;

                string relative = entryDir.Length > prefix.Length
                    ? entryDir.Substring(prefix.Length).TrimStart('/')
                    : string.Empty;

                hits.Add((i, e, relative));
            }

            var sb = new StringBuilder();

            if (!recursive)
            {
                // Shallow: files directly here + names of immediate subdirectories.
                var subdirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var children = new List<(int Index, ZdbEntry Entry)>();

                foreach ((int idx, ZdbEntry e, string rel) in hits)
                {
                    string[] parts = rel.Split(
                        new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) children.Add((idx, e));
                    else subdirs.Add(parts[0]);
                }

                sb.AppendLine("[DIR] .");
                sb.AppendLine("[DIR] ..");
                foreach (string sub in subdirs)
                    sb.AppendLine($"[DIR] {sub}");
                foreach ((int idx, ZdbEntry entry) in children)
                    sb.AppendLine($"[FILE] {idx:X4} {entry.Name}");
            }
            else
            {
                // Recursive: build a sorted tree and print it depth-first.
                sb.AppendLine("[DIR] .");
                sb.AppendLine("[DIR] ..");
                AppendRecursive(sb, prefix, hits, depth: 1);
            }

            return sb.ToString();
        }

        // Recursively appends one level of the tree to sb.
        private void AppendRecursive(
            StringBuilder sb,
            string currentPrefix,
            List<(int Index, ZdbEntry Entry, string Relative)> hits,
            int depth)
        {
            string indent = new string(' ', depth * 2);

            // Immediate subdirectory names at this level.
            var subdirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            // Files directly in this directory.
            var files = new List<(int Index, ZdbEntry Entry)>();

            foreach ((int idx, ZdbEntry e, string rel) in hits)
            {
                // rel is relative to the *original* prefix, so subtract currentPrefix portion.
                string normCurrent = NormalizeForComparison(currentPrefix);
                string entryDir = NormalizeForComparison(GetParentDirectory(e.FullPath));

                if (!IsUnderOrEqual(entryDir, normCurrent)) continue;

                string localRel = entryDir.Length > normCurrent.Length
                    ? entryDir.Substring(normCurrent.Length).TrimStart('/')
                    : string.Empty;

                string[] parts = localRel.Split(
                    new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) files.Add((idx, e));
                else subdirs.Add(parts[0]);
            }

            foreach (string sub in subdirs)
            {
                sb.AppendLine($"{indent}[DIR] {sub}");
                AppendRecursive(sb, currentPrefix + "/" + sub, hits, depth + 1);
            }

            foreach ((int idx, ZdbEntry entry) in files)
                sb.AppendLine($"{indent}[FILE] {idx:X4} {entry.Name}");
        }

        // ------------------------------------------------------------------
        // Wildcard search  (name AND path)
        // ------------------------------------------------------------------

        /// <summary>
        /// Searches for entries matching <paramref name="pattern"/> against
        /// <em>both</em> the file name and the full archive path.
        ///
        /// Wildcard tokens:
        /// <list type="bullet">
        ///   <item><c>?</c>  – any single character (not a path separator).</item>
        ///   <item><c>*</c>  – any sequence of characters within a single directory segment.</item>
        ///   <item><c>**</c> – any sequence of characters including path separators.</item>
        /// </list>
        ///
        /// <example><code>
        /// // By name
        /// archive.FindEntries("*.bin")            // any .bin file
        /// archive.FindEntries("level??.dat")      // level01.dat, level99.dat, …
        ///
        /// // By path  (single directory)
        /// archive.FindEntries("data/levels/*.bin")
        ///
        /// // By path  (any depth)
        /// archive.FindEntries("data/**/*.bin")
        /// archive.FindEntries("z:/textures/**")
        /// </code></example>
        /// </summary>
        public IReadOnlyList<ZdbEntry> FindEntries(string pattern)
        {
            EnsureOpen();
            string normPattern = NormalizeForComparison(pattern);
            return _entries
                .Where(e =>
                    WildcardMatch(e.Name, pattern) ||
                    WildcardMatch(NormalizeForComparison(e.FullPath), normPattern))
                .ToList();
        }

        /// <summary>
        /// Searches by file name only.
        /// <example><code>
        /// archive.FindByName("*.bin")
        /// archive.FindByName("level??.dat")
        /// </code></example>
        /// </summary>
        public IReadOnlyList<ZdbEntry> FindByName(string pattern)
        {
            EnsureOpen();
            return _entries
                .Where(e => WildcardMatch(e.Name, pattern))
                .ToList();
        }

        /// <summary>
        /// Searches by full archive path only. Use <c>**</c> to cross directory boundaries.
        /// <example><code>
        /// archive.FindByPath("data/levels/*.bin")   // files directly in data/levels/
        /// archive.FindByPath("data/**/*.bin")        // .bin files at any depth under data/
        /// archive.FindByPath("z:/textures/**")       // everything under textures/
        /// </code></example>
        /// </summary>
        public IReadOnlyList<ZdbEntry> FindByPath(string pattern)
        {
            EnsureOpen();
            string normPattern = NormalizeForComparison(pattern);
            return _entries
                .Where(e => WildcardMatch(NormalizeForComparison(e.FullPath), normPattern))
                .ToList();
        }

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------

        public void Dispose()
        {
            _entries.Clear();
            _archiveType = 0;
        }

        // ====================================================================
        // Private – loading
        // ====================================================================

        private void Load(byte[] data)
        {
            if (data.Length < 4)
                throw new InvalidDataException("Data too short to be a ZDB archive.");

            uint magic = BitConverter.ToUInt32(data, 0);
            switch (magic)
            {
                case 0xC8: LoadType1(data); break;
                case 0xFC: LoadType2(data); break;
                default:
                    throw new InvalidDataException(
                        $"Unrecognised ZDB magic 0x{magic:X8}. Expected 0xC8 or 0xFC.");
            }
        }

        private void LoadType1(byte[] data)
        {
            _archiveType = 1;
            _entries.Clear();

            _head1 = new Type1Header
            {
                MainHeaderSize = R32(data, 0x00),
                FileHeaderSize = R32(data, 0x04),
                Version = R32(data, 0x08),
                ArchiveHeaderSize = R32(data, 0x0C),
                BodySize = R32(data, 0x10),
                UnusedId = R32(data, 0x14),
                Compression = R32(data, 0x18),
                Errors = R16(data, 0x1C),
                Warnings = R16(data, 0x1E),
            };

            uint fileCount = R32(data, 0xC4);
            uint cursor = _head1.MainHeaderSize;

            for (uint i = 0; i < fileCount; i++)
            {
                string fullPath = ReadCString(data, cursor);
                string name = PathFileName(fullPath.Replace('\\', '/'));

                uint varName = R32(data, cursor + 0x40);
                uint dbId = R32(data, cursor + 0x44);
                uint entry = R32(data, cursor + 0x48);
                uint size = R32(data, cursor + 0x4C);
                uint checksum = R32(data, cursor + 0x50);

                _entries.Add(new ZdbEntry
                {
                    FullPath = fullPath,
                    Name = name,
                    Data = ReadBlock(data, entry + _head1.ArchiveHeaderSize, size),
                    VarName = varName,
                    DbId = dbId,
                    Checksum = checksum,
                });

                cursor += _head1.FileHeaderSize;
            }
        }

        private void LoadType2(byte[] data)
        {
            _archiveType = 2;
            _entries.Clear();

            _head2 = new Type2Header
            {
                MainHeaderSize = R32(data, 0x00),
                Unknown1 = R32(data, 0x04),
                UnknownId = R32(data, 0x08),   // preserved as-is (e.g. 0x3F8898BE in M51.ZDB)
                Unknown2 = R32(data, 0x0C),
                Unknown3 = R32(data, 0x10),
                Version = ReadCString(data, 0x14),
            };

            uint fileCount = R32(data, 0x98);
            uint cursor = 0xA0;

            for (uint i = 0; i < fileCount; i++)
            {
                uint headSize = R32(data, cursor);

                // Preserve the raw 0x40-byte name field verbatim — bytes after the null
                // terminator are non-zero garbage in real archives (confirmed M51.ZDB).
                byte[] rawName = ReadBlock(data, cursor + 4, 0x40);
                string fullPath = ReadCString(data, cursor + 4);
                string name = PathFileName(fullPath.Replace('\\', '/'));

                uint entry = R32(data, cursor + 0x44);
                uint size = R32(data, cursor + 0x48);
                // +0x4C..+0x57 = three zero padding words (confirmed zero in M51.ZDB)
                uint trailingWord = R32(data, cursor + 0x58); // non-zero in real archives

                _entries.Add(new ZdbEntry
                {
                    FullPath = fullPath,
                    Name = name,
                    Data = ReadBlock(data, entry, size),
                    RawNameField = rawName,
                    TrailingWord = trailingWord,
                });

                cursor += headSize;
            }
        }

        // ====================================================================
        // Private – serialisation
        // ====================================================================

        private byte[] SerializeType1()
        {
            int fileCount = _entries.Count;

            // Pre-calculate 16-byte-aligned body offsets.
            var offsets = new uint[fileCount];
            uint bodySize = 0;
            for (int i = 0; i < fileCount; i++)
            {
                offsets[i] = bodySize;
                bodySize = Align16(bodySize + (uint)_entries[i].Data.Length);
            }

            uint archiveHeaderSize = 0xC8u + (uint)(fileCount * 0x58);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Main header
            bw.Write(0x000000C8u);       // MainHeaderSize
            bw.Write(0x00000058u);       // FileHeaderSize
            bw.Write(0x00000006u);       // Version
            bw.Write(archiveHeaderSize); // ArchiveHeaderSize
            bw.Write(bodySize);          // BodySize
            bw.Write(0x4532ABCDu);       // UnusedId sentinel
            bw.Write(0x00000000u);       // Compression
            bw.Write((ushort)0);         // Errors
            bw.Write((ushort)0);         // Warnings
            for (int i = 0x20; i < 0xC4; i += 4)
                bw.Write(0u);
            bw.Write((uint)fileCount);   // FileCount @ 0xC4

            // Per-file headers
            for (int i = 0; i < fileCount; i++)
            {
                ZdbEntry e = _entries[i];
                WritePaddedString(bw, e.FullPath, 0x40);
                bw.Write(e.VarName);
                bw.Write(e.DbId);
                bw.Write(offsets[i]);
                bw.Write(e.Size);
                bw.Write(e.Checksum);
                bw.Write(0u); // reserved
            }

            // Body (16-byte aligned)
            for (int i = 0; i < fileCount; i++)
            {
                bw.Write(_entries[i].Data);
                uint written = (uint)_entries[i].Data.Length;
                while ((written & 0xF) != 0) { bw.Write(0u); written += 4; }
            }

            return ms.ToArray();
        }

        private byte[] SerializeType2()
        {
            int fileCount = _entries.Count;
            uint dataStart = 0xA0u + (uint)(fileCount * 0x5C);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Main header — write UnknownId as read (not hardcoded 0x12345678,
            // which is only the APACHE00.ZDB sentinel; M51.ZDB has 0x3F8898BE).
            bw.Write(0x000000FCu);
            bw.Write(_head2.Unknown1);
            bw.Write(_head2.UnknownId);   // preserved verbatim from load
            bw.Write(_head2.Unknown2);
            bw.Write(_head2.Unknown3);

            string ver = _head2.Version ?? string.Empty;
            if (ver.Length > 0)
            {
                bw.Write(ver.ToCharArray());
                for (int i = 0x14 + ver.Length; i < 0x98; i++) bw.Write((byte)0);
            }
            else
            {
                for (int i = 0x14; i < 0x98; i++) bw.Write((byte)0);
            }

            bw.Write((uint)fileCount);  // @ 0x98
            bw.Write(0x0000005Cu);      // FileHeaderSize @ 0x9C

            // Per-file headers
            uint running = dataStart;
            foreach (ZdbEntry e in _entries)
            {
                bw.Write(0x0000005Cu);  // FileHeadSize

                // Write the name field: use the preserved raw bytes when available
                // (they contain non-zero garbage after the null in real archives),
                // otherwise fall back to writing the string with zero padding.
                if (e.RawNameField != null && e.RawNameField.Length == 0x40)
                {
                    bw.Write(e.RawNameField);
                }
                else
                {
                    WritePaddedString(bw, e.FullPath, 0x40);
                }

                bw.Write(running);      // absolute data offset @ +0x44
                bw.Write(e.Size);       // @ +0x48
                bw.Write(0u);           // padding +0x4C (confirmed zero)
                bw.Write(0u);           // padding +0x50 (confirmed zero)
                bw.Write(0u);           // padding +0x54 (confirmed zero)
                bw.Write(e.TrailingWord); // preserved @ +0x58 (non-zero in real archives)

                running += e.Size;
            }

            // Body
            foreach (ZdbEntry e in _entries)
                bw.Write(e.Data);

            return ms.ToArray();
        }

        // ====================================================================
        // Private – blank archive builders
        // ====================================================================

        private static byte[] BuildBlankType1()
        {
            var b = new byte[0xC8];
            b[0x00] = 0xC8; b[0x04] = 0x58; b[0x08] = 0x06;
            // Sentinel 0x12345678 little-endian @ 0x14
            b[0x14] = 0x78; b[0x15] = 0x56; b[0x16] = 0x34; b[0x17] = 0x12;
            return b;
        }

        private static byte[] BuildBlankType2()
        {
            var b = new byte[0xFC];
            b[0x00] = 0xFC; b[0x04] = 0x01;
            // Sentinel 0x12345678 little-endian @ 0x08
            b[0x08] = 0x78; b[0x09] = 0x56; b[0x0A] = 0x34; b[0x0B] = 0x12;
            b[0x0C] = 0x02;
            return b;
        }

        // ====================================================================
        // Private – binary helpers
        // ====================================================================

        private static uint R32(byte[] d, uint o) => BitConverter.ToUInt32(d, (int)o);
        private static ushort R16(byte[] d, uint o) => BitConverter.ToUInt16(d, (int)o);

        private static byte[] ReadBlock(byte[] data, uint offset, uint length)
        {
            var buf = new byte[length];
            Buffer.BlockCopy(data, (int)offset, buf, 0, (int)length);
            return buf;
        }

        private static string ReadCString(byte[] data, uint offset)
        {
            var sb = new StringBuilder();
            while (offset < data.Length && data[offset] != 0)
                sb.Append((char)data[offset++]);
            return sb.ToString();
        }

        private static void WritePaddedString(BinaryWriter bw, string value, int width)
        {
            int written = 0;
            foreach (char c in value)
            {
                if (written == width) return;
                bw.Write(c);
                written++;
            }
            for (; written < width; written++)
                bw.Write((byte)0);
        }

        private static uint Align16(uint v) => (v + 0xFu) & ~0xFu;

        // ====================================================================
        // Private – path helpers
        // ====================================================================

        /// <summary>
        /// Stores in the on-disk format: strips "z:/", converts slashes to backslash,
        /// collapses duplicate separators.
        /// </summary>
        private static string NormalizeArchivePath(string path)
        {
            if (path.StartsWith("z:/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(3);
            else if (path.StartsWith("z:", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(2);

            path = path.Replace('/', '\\');

            while (path.Contains("\\\\"))
                path = path.Replace("\\\\", "\\");

            return path;
        }

        /// <summary>
        /// Comparison form: lowercase, forward slashes, no leading "z:", no trailing slash.
        /// </summary>
        private static string NormalizeForComparison(string path)
        {
            string p = path.Replace('\\', '/').ToLowerInvariant();

            if (p.StartsWith("z:/")) p = p.Substring(3);
            else if (p.StartsWith("z:")) p = p.Substring(2);

            while (p.Contains("//"))
                p = p.Replace("//", "/");

            return p.TrimEnd('/');
        }

        /// <summary>
        /// Returns the parent directory of a path (everything before the last separator).
        /// </summary>
        private static string GetParentDirectory(string fullPath)
        {
            string p = fullPath.Replace('\\', '/');
            int lastSlash = p.LastIndexOf('/');
            return lastSlash < 0 ? string.Empty : p.Substring(0, lastSlash);
        }

        /// <summary>
        /// True when <paramref name="entryDir"/> equals <paramref name="prefix"/>
        /// or is a subdirectory of it. An empty prefix matches everything (archive root).
        /// </summary>
        private static bool IsUnderOrEqual(string entryDir, string prefix)
        {
            if (prefix.Length == 0) return true;
            return string.Equals(entryDir, prefix, StringComparison.OrdinalIgnoreCase)
                || entryDir.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns the file-name component of a forward-slash path.</summary>
        private static string PathFileName(string path)
        {
            string tmp = (path + "/").Replace("//", "/");
            string[] parts = tmp.Split('/');
            return parts.Length >= 2 ? parts[^2] : path;
        }

        // ====================================================================
        // Private – wildcard matching
        // ====================================================================

        /// <summary>
        /// Case-insensitive wildcard match.
        /// <list type="bullet">
        ///   <item><c>?</c>  – matches exactly one character that is not a path separator.</item>
        ///   <item><c>*</c>  – matches any sequence of non-separator characters (same directory).</item>
        ///   <item><c>**</c> – matches any sequence of characters including path separators.</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// Uses an iterative backtracking algorithm to avoid recursion overhead on long paths.
        /// </remarks>
        private static bool WildcardMatch(string input, string pattern)
        {
            // ip  = current position in input
            // pp  = current position in pattern
            // lastStarIp / lastStarPp = position saved when we last hit a '*' or '**'
            int ip = 0, pp = 0;
            int lastStarIp = -1, lastStarPp = -1;
            bool lastStarWasDouble = false; // whether the saved star was **

            while (ip < input.Length)
            {
                if (pp < pattern.Length)
                {
                    char pc = pattern[pp];
                    char ic = input[ip];

                    // Double-star (**): crosses directory separators unconditionally.
                    if (pc == '*' && pp + 1 < pattern.Length && pattern[pp + 1] == '*')
                    {
                        lastStarIp = ip;
                        lastStarPp = pp;
                        lastStarWasDouble = true;
                        pp += 2; // skip both *
                        continue;
                    }

                    // Single-star (*): does NOT cross a '/' separator.
                    if (pc == '*')
                    {
                        lastStarIp = ip;
                        lastStarPp = pp;
                        lastStarWasDouble = false;
                        pp++;
                        continue;
                    }

                    // Question mark (?): matches any single char except '/'.
                    bool charOk = pc == '?'
                        ? ic != '/'
                        : char.ToLowerInvariant(pc) == char.ToLowerInvariant(ic);

                    if (charOk) { ip++; pp++; continue; }
                }

                // Mismatch – backtrack to the most recent star.
                if (lastStarIp >= 0)
                {
                    // A single * cannot consume a '/' separator.
                    if (!lastStarWasDouble && input[lastStarIp] == '/')
                        return false;

                    // Let the star consume one more character and retry.
                    lastStarIp++;
                    ip = lastStarIp;
                    pp = lastStarPp + (lastStarWasDouble ? 2 : 1);
                    continue;
                }

                return false;
            }

            // Consume any trailing '*' or '**' at the end of the pattern.
            while (pp < pattern.Length && pattern[pp] == '*')
                pp++;

            return pp == pattern.Length;
        }

        private void EnsureOpen()
        {
            if (_archiveType == 0)
                throw new InvalidOperationException("No archive is currently loaded.");
        }
    }

    // =========================================================================

    // =========================================================================
    //  SOCOM II — APACHE00.ZDB crypto layer
    //
    //  Research findings (April 2026 live EE capture, SOCOM II BASCUS-97275):
    //
    //  File layout (Type-2 archive, two sections):
    //    0x000–0x09F   ZDB main header (0xFC bytes)
    //    0x0A0–0x0FB   Section-0 directory entry  ("ftscore",  0x5C bytes)
    //    0x0FC–0x157   Section-1 directory entry  ("zsealetc", 0x5C bytes)
    //    0x158–0x457   DNAS session context blob  (0x300 bytes, opaque encrypted)
    //    0x458–0x467   AES-128 KEY (16 bytes, embedded by Sony DNAS server at download time)
    //    0x468–0x477   AES-128 IV  (16 bytes)
    //    0x478+        per-section: RSA-2048 prefix (0x100 bytes) + AES-CBC encrypted payload
    //
    //  Two-layer encryption pipeline (decrypt direction):
    //    ZDB raw bytes
    //      Layer 2: outer vtable transforms (EE 0x00562198, 16 functions)
    //               Per 32-bit word, apply vtable[idx_0..count-1] from dispatch word
    //               Word exclusion list: certain word indices are skipped
    //      Layer 1: AES-128-CBC
    //               Key @ 0x458 (EE gMediusSessionKey 0x01E7EF30)
    //               IV  @ 0x468 (EE gMediusSessionIV  0x01E7EF40)
    //               IV resets to session IV per 0x80-byte chunk (NOT chained across chunks)
    //      Plaintext: MWo3-wrapped MIPS patch code
    //
    //  AES confirmed call chain:
    //    0x00534848 ZDBSectionParser (self-decrypting stub)
    //      -> 0x0053F8F0 BlockCipherOrchestrator
    //        -> 0x0053FC60 AES_CBC_Core
    //          -> 0x005153F0 AES_CBC_Wrapper
    //            -> 0x00515F00 AES block primitive
    //               0x00531730 CBC chaining
    //
    //  Dispatch word format (ZDBDispatchWordEncoder EE 0x005408D0):
    //    bits[31:28] = transform count N
    //    bits[i*4+3:i*4] = vtable index for pass i  (0-F)
    //  Known dispatch words:
    //    Chunk  0:  0x20332127 -> count=2,  indices=[7, 2]
    //    Chunk 26:  0xA03BA1AF -> count=10, indices=[F,A,1,A,B,3,0,...]
    //    Sec-block: 0xBAFA4B78 -> encoded 0x32724370, count=3, indices=[0,7,3]
    // =========================================================================

    // -------------------------------------------------------------------------
    //  MWo3 header (decrypted payload wrapper)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Header of a decrypted ZDB section payload (MWo3 format).
    /// Confirmed live at EE 0x001E7000 after FTScore.bin decryption.
    ///
    /// <code>
    ///   +0x00  uint  Magic = 0x4D576F33 "MWo3"
    ///   +0x04  uint  SegmentCount (1 for FTScore)
    ///   +0x08  uint  LoadAddress  (0x001E7000 for FTScore)
    ///   +0x0C  uint  TextSize     (0x00220158 for FTScore = 2,228,568 bytes)
    ///   +0x10  uint  DataSize     (0x0002DBB0 for FTScore = 187,312 bytes)
    ///   +0x14  uint  BssSize      (0x00084480 for FTScore = 541,824 bytes)
    ///   +0x18  uint  OverlayCallbacksStart  (0x004315A0)
    ///   +0x1C  uint  OverlayCallbacksEnd    (0x00431798)
    ///   +0x20  char[]  FileName null-terminated, padded to 0x40
    /// </code>
    /// </summary>
    public sealed class MWo3Header
    {
        public const uint ExpectedMagic = 0x4D576F33; // "MWo3"

        public uint Magic { get; init; }
        public uint SegmentCount { get; init; }
        public uint LoadAddress { get; init; }
        public uint TextSize { get; init; }
        public uint DataSize { get; init; }
        public uint BssSize { get; init; }
        public uint OverlayCallbacksStart { get; init; }
        public uint OverlayCallbacksEnd { get; init; }
        public string FileName { get; init; } = string.Empty;

        /// <summary>Total on-disk bytes = TextSize + DataSize = 0x24DD48 for FTScore.</summary>
        public uint TotalSize => TextSize + DataSize;

        public bool IsValid => Magic == ExpectedMagic;

        /// <summary>Parses an MWo3 header from the start of a decrypted payload.</summary>
        /// <exception cref="InvalidDataException"/>
        public static MWo3Header Parse(byte[] data)
        {
            if (data.Length < 0x40)
                throw new InvalidDataException("Buffer too short for MWo3 header.");

            uint magic = BitConverter.ToUInt32(data, 0x00);
            if (magic != ExpectedMagic)
                throw new InvalidDataException(
                    $"Bad MWo3 magic 0x{magic:X8} (expected 0x{ExpectedMagic:X8}).");

            var sb = new StringBuilder();
            for (int i = 0x20; i < Math.Min(data.Length, 0x40); i++)
            {
                if (data[i] == 0) break;
                sb.Append((char)data[i]);
            }

            return new MWo3Header
            {
                Magic = magic,
                SegmentCount = BitConverter.ToUInt32(data, 0x04),
                LoadAddress = BitConverter.ToUInt32(data, 0x08),
                TextSize = BitConverter.ToUInt32(data, 0x0C),
                DataSize = BitConverter.ToUInt32(data, 0x10),
                BssSize = BitConverter.ToUInt32(data, 0x14),
                OverlayCallbacksStart = BitConverter.ToUInt32(data, 0x18),
                OverlayCallbacksEnd = BitConverter.ToUInt32(data, 0x1C),
                FileName = sb.ToString(),
            };
        }
    }

    // -------------------------------------------------------------------------
    //  Confirmed file offsets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Confirmed byte offsets within APACHE00.ZDB (SOCOM II BASCUS-97275SOCOMII).
    /// All offsets verified from live EE RAM capture and raw file analysis.
    /// </summary>
    public static class ApacheZdbOffsets
    {
        // ZDB structural offsets
        public const int MainHeader = 0x000;   // ZDB Type-2 main header, 0xFC bytes
        public const int Section0Entry = 0x0A0;   // "ftscore"  directory entry, 0x5C bytes
        public const int Section1Entry = 0x0FC;   // "zsealetc" directory entry, 0x5C bytes

        // Session context
        /// <summary>DNAS session context blob (0x300 bytes, opaque). data_offset in section table = 0x158.</summary>
        public const int SessionCtxBlob = 0x158;

        /// <summary>
        /// AES-128 KEY offset from the START OF THE FTSCORE SECTION DATA.
        /// = session_ctx + 0x300. For standard APACHE00.ZDB: file offset 0x458.
        /// EE gMediusSessionKey @ 0x01E7EF30.
        /// </summary>
        public const int SessionKeyOffset = 0x300;

        /// <summary>
        /// AES-128 IV offset from the START OF THE FTSCORE SECTION DATA.
        /// = session_ctx + 0x310. For standard APACHE00.ZDB: file offset 0x468.
        /// EE gMediusSessionIV @ 0x01E7EF40.
        /// </summary>
        public const int SessionIvOffset = 0x310;

        // Absolute file offsets for standard 2-entry APACHE00.ZDB layout (informational).
        // Use SessionKeyOffset / SessionIvOffset relative to ftscore data_offset instead.
        /// <summary>AES key absolute file offset (standard layout: ftscore_offset=0x158, key=0x158+0x300).</summary>
        public const int AesKey = 0x458;
        /// <summary>AES IV  absolute file offset (standard layout: ftscore_offset=0x158, iv=0x158+0x310).</summary>
        public const int AesIv = 0x468;

        // Section payloads
        /// <summary>Section-0 ("ftscore") data_offset in file = 0x158 for standard layout (entries end at 0x158).</summary>
        public const int FTScorePayload = 0x158;

        /// <summary>Section-1 ("zsealetc") data_offset in file = 0x0D10C8.</summary>
        public const int ZSealEtcPayload = 0x0D10C8;

        /// <summary>RSA-2048 Sony signature prefix size preceding each encrypted payload (256 bytes).</summary>
        public const int RsaSigSize = 0x100;

        /// <summary>AES chunk stride: 8 blocks x 16 bytes. IV resets per chunk (not standard CBC).</summary>
        public const int AesChunkStride = 0x080;
    }

    /// <summary>
    /// Practical dispatch/exclusion profiles for APACHE00 section crypto.
    ///
    /// These defaults come from mapped FTScore behavior and let callers decrypt
    /// without manually constructing per-chunk delegates.
    /// </summary>
    public static class ApacheZdbProfiles
    {
        /// <summary>
        /// FTScore output base address in EE RAM (gMWo3FTScoreOutput).
        /// Used for address-derived dispatch words.
        /// </summary>
        public const uint FTScoreLoadAddress = 0x001E7000;

        /// <summary>
        /// Known word exclusions for FTScore chunk processing from mapped stream behavior.
        /// Word indices are 0-based within a 0x80-byte chunk.
        /// </summary>
        public static readonly int[] FTScoreWordExclusions = { 8, 10, 11 };

        /// <summary>
        /// ZSealEtc code load base used by existing live-disasm tooling.
        /// This is used for address-derived dispatch words for zsealetc.
        /// </summary>
        public const uint ZSealEtcLoadAddress = 0x00188938;

        /// <summary>
        /// No confirmed fixed exclusion words for zsealetc yet.
        /// Keep this empty unless stream-table mapping proves otherwise.
        /// </summary>
        public static readonly int[] ZSealEtcWordExclusions = Array.Empty<int>();

        /// <summary>
        /// Builds an encoded dispatch word for a given chunk by using the destination
        /// EE address (loadAddress + chunkIndex * 0x80).
        /// </summary>
        public static uint GetAddressDerivedDispatchWord(uint loadAddress, int chunkIndex)
        {
            if (chunkIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));

            uint dest = loadAddress + (uint)(chunkIndex * ApacheZdbOffsets.AesChunkStride);
            return ZdbDispatch.EncodeFromAddress(dest);
        }

        /// <summary>
        /// Creates a per-chunk dispatch provider using address-derived encoding.
        /// </summary>
        public static Func<int, uint> CreateAddressDerivedDispatchProvider(uint loadAddress)
            => chunkIndex => GetAddressDerivedDispatchWord(loadAddress, chunkIndex);

        /// <summary>
        /// Creates a fixed exclusion provider (same exclusion set for every chunk).
        /// A cloned set is returned per call so callers cannot mutate shared state.
        /// </summary>
        public static Func<int, ISet<int>?> CreateFixedExclusionProvider(params int[] excludedWordIndices)
            => _ => new HashSet<int>(excludedWordIndices);
    }

    // -------------------------------------------------------------------------
    //  Outer vtable transforms (EE 0x00562198, all 16 confirmed)
    // -------------------------------------------------------------------------

    /// <summary>
    /// All 16 outer transform functions from the ZDB vtable at EE 0x00562198 (post-patch).
    ///
    /// Byte permutation notation (little-endian word, b0=LSB):
    ///   bpermc([b3,b2,b1,b0]) -> [b2,b0,b3,b1]  = (b2 LSL 24)|(b0 LSL 16)|(b3 LSL 8)|b1
    ///   bperm7([b3,b2,b1,b0]) -> [b1,b3,b0,b2]  = (b1 LSL 24)|(b3 LSL 16)|(b0 LSL 8)|b2
    ///   bpermc and bperm7 are mutual inverses.
    ///
    /// XOR1 = 0x09F8ED17,  XOR2 = 0xA95128C3
    /// </summary>
    public static class ZdbVtable
    {
        public const uint XOR1 = 0x09F8ED17;
        public const uint XOR2 = 0xA95128C3;

        // ------------------------------------------------------------------
        // Bit-rotation and byte-permutation helpers
        // ------------------------------------------------------------------

        public static uint Rol32(uint w, int n) => (w << n) | (w >> (32 - n));

        /// <summary>bpermc: [b3,b2,b1,b0]->[b2,b0,b3,b1]. Vtable[C] EE 0x005419D0.</summary>
        public static uint Bpermc(uint w)
        {
            byte b0 = (byte)w, b1 = (byte)(w >> 8), b2 = (byte)(w >> 16), b3 = (byte)(w >> 24);
            return ((uint)b2 << 24) | ((uint)b0 << 16) | ((uint)b3 << 8) | b1;
        }

        /// <summary>bperm7: [b3,b2,b1,b0]->[b1,b3,b0,b2]. Used inside Vtable[7] EE 0x00534370.</summary>
        public static uint Bperm7(uint w)
        {
            byte b0 = (byte)w, b1 = (byte)(w >> 8), b2 = (byte)(w >> 16), b3 = (byte)(w >> 24);
            return ((uint)b1 << 24) | ((uint)b3 << 16) | ((uint)b0 << 8) | b2;
        }

        // ------------------------------------------------------------------
        // Forward transforms (decrypt direction: ZDB raw -> AES input)
        // ------------------------------------------------------------------

        public static uint Vt0(uint w) => w ^ XOR1;                                   // EE 0x00534498
        public static uint Vt1(uint w) => w ^ XOR2;                                   // EE 0x00534538
        public static uint Vt2(uint w) => Rol32(w, 19);                               // EE 0x005345D8
        public static uint Vt3(uint w) => Rol32(w, 24);                               // EE 0x00534698  (= ROR8)
        public static uint Vt4(uint w) => Rol32(w, 16);                               // EE 0x00534758
        public static uint Vt5(uint w) => Rol32(w ^ XOR1, 19);                        // EE 0x00534188
        public static uint Vt6(uint w) => Rol32(w ^ XOR2, 16);                        // EE 0x00534260
        public static uint Vt7(uint w) => Bperm7(Rol32(w ^ XOR1, 11));                // EE 0x00534370  ROL24+ROL19=ROL11
        public static uint Vt8(uint w) => w ^ XOR1;                                   // EE 0x00536D20  alias of Vt0
        public static uint Vt9(uint w) => w ^ XOR2;                                   // EE 0x00536DC0  alias of Vt1
        public static uint VtA(uint w) => Rol32(w, 13);                               // EE 0x00541870
        public static uint VtB(uint w) => Rol32(w, 8);                                // EE 0x00541920
        public static uint VtC(uint w) => Bpermc(w);                                  // EE 0x005419D0
        public static uint VtD(uint w) => Rol32(w, 13) ^ XOR1;                        // EE 0x00536A48
        public static uint VtE(uint w) => Bpermc(Rol32(w, 8) ^ XOR2);                // EE 0x00536B08
        public static uint VtF(uint w) => Rol32(Bpermc(w), 21) ^ XOR1;               // EE 0x00536C08  bpermc->ROL13->ROL8=ROL21->XOR1

        // ------------------------------------------------------------------
        // Inverse transforms (encrypt direction: plaintext -> ZDB raw)
        // ------------------------------------------------------------------

        public static uint Iv0(uint w) => w ^ XOR1;                                   // XOR self-inverse
        public static uint Iv1(uint w) => w ^ XOR2;
        public static uint Iv2(uint w) => Rol32(w, 13);                               // ROR19 = ROL13
        public static uint Iv3(uint w) => Rol32(w, 8);                                // ROR24 = ROL8
        public static uint Iv4(uint w) => Rol32(w, 16);                               // ROL16 self-inverse
        public static uint Iv5(uint w) => Rol32(w, 13) ^ XOR1;                        // ROR19->XOR1
        public static uint Iv6(uint w) => Rol32(w, 16) ^ XOR2;                        // ROL16->XOR2
        public static uint Iv7(uint w) => Rol32(Bperm7(w), 21) ^ XOR1;               // inv(bperm7(ROL11(w^XOR1))) = XOR1->ROR11->bpermc = XOR1->ROL21->bpermc. Verify: iv7==vtF
        public static uint Iv8(uint w) => w ^ XOR1;
        public static uint Iv9(uint w) => w ^ XOR2;
        public static uint IvA(uint w) => Rol32(w, 19);                               // ROR13 = ROL19
        public static uint IvB(uint w) => Rol32(w, 24);                               // ROR8  = ROL24
        public static uint IvC(uint w) => Bperm7(w);                                  // inv_bpermc = bperm7
        public static uint IvD(uint w) => Rol32(w ^ XOR1, 19);                        // XOR1->ROR13 = XOR1->ROL19
        public static uint IvE(uint w) => Rol32(Bperm7(w) ^ XOR2, 24);               // inv_bpermc->XOR2->ROR8 = bperm7->XOR2->ROL24
        public static uint IvF(uint w) => Bperm7(Rol32(w ^ XOR1, 11));               // XOR1->ROR21->inv_bpermc = XOR1->ROL11->bperm7. Verify: ivF==vt7

        // ------------------------------------------------------------------
        // Dispatch tables (0x0-0xF)
        // ------------------------------------------------------------------

        private static readonly Func<uint, uint>[] _fwd =
        {
            Vt0, Vt1, Vt2, Vt3, Vt4, Vt5, Vt6, Vt7,
            Vt8, Vt9, VtA, VtB, VtC, VtD, VtE, VtF,
        };

        private static readonly Func<uint, uint>[] _inv =
        {
            Iv0, Iv1, Iv2, Iv3, Iv4, Iv5, Iv6, Iv7,
            Iv8, Iv9, IvA, IvB, IvC, IvD, IvE, IvF,
        };

        /// <summary>Applies forward transform index (0–F) to w.</summary>
        public static uint ApplyForward(int idx, uint w) => _fwd[idx & 0xF](w);

        /// <summary>Applies inverse transform index (0–F) to w.</summary>
        public static uint ApplyInverse(int idx, uint w) => _inv[idx & 0xF](w);
    }

    // -------------------------------------------------------------------------
    //  Dispatch word codec (ZDBDispatchWordEncoder EE 0x005408D0)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encodes and decodes ZDB chunk dispatch words.
    /// Format: bits[31:28] = count N, bits[i*4+3:i*4] = vtable index for pass i.
    /// </summary>
    public static class ZdbDispatch
    {
        /// <summary>Decodes a dispatch word into an ordered array of vtable indices.</summary>
        public static int[] DecodeIndices(uint dispatchWord)
        {
            int count = (int)((dispatchWord >> 28) & 0xF);
            var indices = new int[count];
            for (int i = 0; i < count; i++)
                indices[i] = (int)((dispatchWord >> (i * 4)) & 0xF);
            return indices;
        }

        /// <summary>Encodes an ordered array of vtable indices into a dispatch word.</summary>
        public static uint EncodeIndices(int[] indices)
        {
            if (indices.Length > 15)
                throw new ArgumentException("Maximum 15 transform indices per dispatch word.");
            uint word = (uint)indices.Length << 28;
            for (int i = 0; i < indices.Length; i++)
                word |= ((uint)indices[i] & 0xF) << (i * 4);
            return word;
        }

        /// <summary>
        /// Address-derived dispatch encoding (default for most chunks).
        /// Uses the destination EE address nibbles to produce 3-bit packed indices (0–7 only).
        /// Full 4-bit indices (8–F) only appear in stream-table overrides.
        /// </summary>
        public static uint EncodeFromAddress(uint destAddr)
        {
            uint val = destAddr & 0xFFFFFFFF;
            uint top4 = (val >> 28) & 0xF;
            uint low3 = top4 & 7;
            int count = (low3 == 0) ? 5 : (int)(top4 - (top4 & 8));
            uint packed = 0;
            for (int i = 0; i < 7; i++)
                packed |= ((val >> (i * 4)) & 7u) << (i * 4);
            return ((uint)(count & 0xF) << 28) | (packed & 0x0FFFFFFFu);
        }
    }

    // -------------------------------------------------------------------------
    //  ZDB chunk crypto processor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the full SOCOM II two-layer encrypt/decrypt pipeline to ZDB section payloads.
    ///
    /// Uses System.Security.Cryptography.Aes — no NuGet packages required.
    ///
    /// Quick usage:
    /// <code>
    ///   (byte[] key, byte[] iv) = ZdbCryptoProcessor.ExtractKeyIv(File.ReadAllBytes("APACHE00.ZDB"));
    ///   byte[] plain = ZdbCryptoProcessor.DecryptSection(sectionBytes, key, iv,
    ///       chunkIdx => ZdbDispatch.EncodeFromAddress(destAddr[chunkIdx]));
    /// </code>
    /// </summary>
    public static class ZdbCryptoProcessor
    {
        // ------------------------------------------------------------------
        // Key / IV extraction
        // ------------------------------------------------------------------

        /// <summary>
        /// Extracts the embedded AES key and IV from a raw APACHE00.ZDB byte array.
        ///
        /// The key and IV are embedded inside the "ftscore" section payload:
        ///   key = ftscore_data_offset + 0x300  (EE gMediusSessionKey 0x01E7EF30)
        ///   IV  = ftscore_data_offset + 0x310  (EE gMediusSessionIV  0x01E7EF40)
        ///
        /// For a standard 2-entry APACHE00.ZDB the ftscore offset is always 0x158,
        /// so key = 0x458 and IV = 0x468. But this method derives the offset
        /// dynamically from the actual ftscore entry, making it robust to any layout.
        /// </summary>
        public static (byte[] Key, byte[] Iv) ExtractKeyIv(byte[] zdbBytes)
        {
            if (zdbBytes.Length < 0xA0 + 0x5C)
                throw new InvalidDataException("ZDB file too short to locate ftscore entry.");

            // Walk entries starting at 0xA0 to find the ftscore section data offset.
            uint fileCount = BitConverter.ToUInt32(zdbBytes, 0x98);
            uint cursor = 0xA0;
            uint ftscoreDataOffset = 0;
            bool found = false;

            for (uint i = 0; i < fileCount && cursor + 0x5C <= zdbBytes.Length; i++)
            {
                uint headSize = BitConverter.ToUInt32(zdbBytes, (int)cursor);

                // Read null-terminated name at cursor+4
                string name = string.Empty;
                for (uint j = cursor + 4; j < cursor + 0x44 && zdbBytes[j] != 0; j++)
                    name += (char)zdbBytes[j];

                if (string.Equals(name, ApacheZdb.FTScore, StringComparison.OrdinalIgnoreCase))
                {
                    ftscoreDataOffset = BitConverter.ToUInt32(zdbBytes, (int)(cursor + 0x44));
                    found = true;
                    break;
                }
                cursor += headSize;
            }

            if (!found)
                throw new InvalidDataException(
                    $"Could not find '{ApacheZdb.FTScore}' section in ZDB — cannot extract key/IV.");

            // Key at session_ctx + 0x300, IV at session_ctx + 0x310
            int keyOff = (int)ftscoreDataOffset + ApacheZdbOffsets.SessionKeyOffset;
            int ivOff = (int)ftscoreDataOffset + ApacheZdbOffsets.SessionIvOffset;

            if (zdbBytes.Length < ivOff + 16)
                throw new InvalidDataException("ZDB file too short to contain embedded key/IV.");

            byte[] key = new byte[16];
            byte[] iv = new byte[16];
            Buffer.BlockCopy(zdbBytes, keyOff, key, 0, 16);
            Buffer.BlockCopy(zdbBytes, ivOff, iv, 0, 16);
            return (key, iv);
        }

        // ------------------------------------------------------------------
        // Single chunk  (0x80 bytes)
        // ------------------------------------------------------------------

        /// <summary>
        /// Decrypts one 0x80-byte ZDB chunk.
        /// Pipeline: ciphertext -> two-layer inverse outer transforms -> AES-128-CBC -> plaintext.
        /// Layer 1 (ZDBBlockProcessor): inverse vtable[0-7] with dispatch indices
        /// Layer 2 (ZDBPostBlockMerge): inverse vtable[8-F] with same indices
        /// The IV resets to the session IV for every chunk (confirmed from live capture).
        /// </summary>
        /// <param name="chunkCipher">Raw 0x80 bytes from ZDB file.</param>
        /// <param name="key">16-byte AES key from file offset 0x458.</param>
        /// <param name="iv">16-byte AES IV from file offset 0x468.</param>
        /// <param name="dispatchWord">Dispatch word for this chunk.</param>
        /// <param name="exclusionSet">
        /// Optional: 0-based word indices excluded from outer transforms.
        /// e.g. {8, 10, 11} for the embedded security block (confirmed from stream table).
        /// </param>
        public static byte[] DecryptChunk(
            byte[] chunkCipher,
            byte[] key,
            byte[] iv,
            uint dispatchWord,
            ISet<int>? exclusionSet = null)
        {
            byte[] stage1 = ApplyInverseOuterTransforms(chunkCipher, dispatchWord, exclusionSet, vtableOffset: 0);
            byte[] stage2 = ApplyInverseOuterTransforms(stage1, dispatchWord, exclusionSet, vtableOffset: 8);
            return AesCbcDecrypt(stage2, key, iv);
        }

        /// <summary>
        /// Encrypts one 0x80-byte plaintext chunk.
        /// Pipeline: plaintext -> AES-128-CBC -> two-layer forward outer transforms -> ciphertext.
        /// Layer 1 (ZDBBlockProcessor): forward vtable[0-7] with dispatch indices
        /// Layer 2 (ZDBPostBlockMerge): forward vtable[8-F] with same indices
        /// </summary>
        public static byte[] EncryptChunk(
            byte[] chunkPlain,
            byte[] key,
            byte[] iv,
            uint dispatchWord,
            ISet<int>? exclusionSet = null)
        {
            byte[] encrypted = AesCbcEncrypt(chunkPlain, key, iv);
            byte[] stage1 = ApplyForwardOuterTransforms(encrypted, dispatchWord, exclusionSet, vtableOffset: 0);
            byte[] stage2 = ApplyForwardOuterTransforms(stage1, dispatchWord, exclusionSet, vtableOffset: 8);
            return stage2;
        }

        // ------------------------------------------------------------------
        // Full section  (many chunks)
        // ------------------------------------------------------------------

        /// <summary>
        /// Decrypts a complete ZDB section payload (multiple 0x80-byte chunks).
        /// Automatically skips the leading 0x100-byte RSA signature prefix.
        /// </summary>
        /// <param name="sectionPayload">Raw section bytes from ZDB (includes RSA prefix).</param>
        /// <param name="getDispatchWord">Per-chunk dispatch word provider.</param>
        /// <param name="getExclusionSet">Optional per-chunk exclusion set provider.</param>
        public static byte[] DecryptSection(
            byte[] sectionPayload,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            int dataStart = ApacheZdbOffsets.RsaSigSize;
            int dataLen = sectionPayload.Length - dataStart;
            if (dataLen <= 0)
                throw new InvalidDataException("Section payload shorter than RSA prefix.");

            return ProcessSection(sectionPayload, dataStart, dataLen, key, iv,
                getDispatchWord, getExclusionSet, encrypt: false);
        }

        /// <summary>
        /// Encrypts a plaintext MWo3 payload into a ZDB section.
        /// Prepends a zeroed 0x100-byte RSA placeholder.
        /// NOP the sig-check in the game binary or RSA-resign with a custom keypair.
        /// </summary>
        public static byte[] EncryptSection(
            byte[] plaintext,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            int stride = ApacheZdbOffsets.AesChunkStride;
            int total = ApacheZdbOffsets.RsaSigSize + plaintext.Length;
            int pad = (stride - (plaintext.Length % stride)) % stride;
            total += pad;

            using var ms = new MemoryStream(total);
            ms.Write(new byte[ApacheZdbOffsets.RsaSigSize], 0, ApacheZdbOffsets.RsaSigSize);

            byte[] paddedPlain = new byte[plaintext.Length + pad];
            Buffer.BlockCopy(plaintext, 0, paddedPlain, 0, plaintext.Length);

            byte[] body = ProcessSection(paddedPlain, 0, paddedPlain.Length, key, iv,
                getDispatchWord, getExclusionSet, encrypt: true);

            ms.Write(body, 0, body.Length);
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // Private — section loop
        // ------------------------------------------------------------------

        private static byte[] ProcessSection(
            byte[] data,
            int dataStart,
            int dataLen,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet,
            bool encrypt)
        {
            int stride = ApacheZdbOffsets.AesChunkStride;
            int chunkCount = dataLen / stride;
            int remainder = dataLen % stride;

            using var output = new MemoryStream(dataLen);

            for (int i = 0; i < chunkCount; i++)
            {
                int off = dataStart + (i * stride);
                byte[] chunk = new byte[stride];
                Buffer.BlockCopy(data, off, chunk, 0, stride);

                uint dw = getDispatchWord(i);
                ISet<int>? exc = getExclusionSet?.Invoke(i);

                byte[] result = encrypt
                    ? EncryptChunk(chunk, key, iv, dw, exc)
                    : DecryptChunk(chunk, key, iv, dw, exc);

                output.Write(result, 0, result.Length);
            }

            // Partial final chunk — pad to 16-byte AES boundary
            if (remainder > 0)
            {
                int off = dataStart + (chunkCount * stride);
                int padded = ((remainder + 15) / 16) * 16;
                byte[] chunk = new byte[padded];
                Buffer.BlockCopy(data, off, chunk, 0, remainder);

                uint dw = getDispatchWord(chunkCount);
                ISet<int>? exc = getExclusionSet?.Invoke(chunkCount);

                byte[] result = encrypt
                    ? EncryptChunk(chunk, key, iv, dw, exc)
                    : DecryptChunk(chunk, key, iv, dw, exc);

                output.Write(result, 0, encrypt ? result.Length : remainder);
            }

            return output.ToArray();
        }

        // ------------------------------------------------------------------
        // Private — outer transform application
        // ------------------------------------------------------------------

        private static byte[] ApplyForwardOuterTransforms(
            byte[] data, uint dispatchWord, ISet<int>? exclusionSet, int vtableOffset = 0)
        {
            int[] indices = ZdbDispatch.DecodeIndices(dispatchWord);
            var result = (byte[])data.Clone();
            int wcount = result.Length / 4;

            for (int wi = 0; wi < wcount; wi++)
            {
                if (exclusionSet != null && exclusionSet.Contains(wi)) continue;

                uint w = BitConverter.ToUInt32(result, wi * 4);
                foreach (int idx in indices)
                    w = ZdbVtable.ApplyForward(vtableOffset + idx, w);
                WriteWordLE(result, wi * 4, w);
            }
            return result;
        }

        private static byte[] ApplyInverseOuterTransforms(
            byte[] data, uint dispatchWord, ISet<int>? exclusionSet, int vtableOffset = 0)
        {
            int[] indices = ZdbDispatch.DecodeIndices(dispatchWord);
            var result = (byte[])data.Clone();
            int wcount = result.Length / 4;

            for (int wi = 0; wi < wcount; wi++)
            {
                if (exclusionSet != null && exclusionSet.Contains(wi)) continue;

                uint w = BitConverter.ToUInt32(result, wi * 4);
                // Apply inverses in reverse order
                for (int i = indices.Length - 1; i >= 0; i--)
                    w = ZdbVtable.ApplyInverse(vtableOffset + indices[i], w);
                WriteWordLE(result, wi * 4, w);
            }
            return result;
        }

        private static void WriteWordLE(byte[] buf, int off, uint w)
        {
            buf[off] = (byte)w;
            buf[off + 1] = (byte)(w >> 8);
            buf[off + 2] = (byte)(w >> 16);
            buf[off + 3] = (byte)(w >> 24);
        }

        // ------------------------------------------------------------------
        // Private — AES-128-CBC (System.Security.Cryptography, no NuGet)
        // ------------------------------------------------------------------

        private static byte[] AesCbcDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            using var ms = new MemoryStream(data);
            using var cs = new System.Security.Cryptography.CryptoStream(
                                ms, dec, System.Security.Cryptography.CryptoStreamMode.Read);
            using var out_ = new MemoryStream();
            cs.CopyTo(out_);
            return out_.ToArray();
        }

        private static byte[] AesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;
            using var enc = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using var cs = new System.Security.Cryptography.CryptoStream(
                                ms, enc, System.Security.Cryptography.CryptoStreamMode.Write);
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }
    }

    // -------------------------------------------------------------------------
    //  High-level APACHE00.ZDB helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// High-level helper that combines ZdbArchive + ZdbCryptoProcessor for one-call
    /// decrypt/encrypt of SOCOM II APACHE00.ZDB sections.
    ///
    /// Decrypt FTScore:
    /// <code>
    ///   using var apache = ApacheZdb.Open("APACHE00.ZDB");
    ///   byte[] plain = apache.DecryptSection(ApacheZdb.FTScore,
    ///       chunk => ZdbDispatch.EncodeFromAddress(destAddrs[chunk]));
    ///   var header = MWo3Header.Parse(plain);
    ///   Console.WriteLine(header.FileName); // "FTScore.bin"
    /// </code>
    ///
    /// Encrypt patched FTScore for revival server:
    /// <code>
    ///   using var apache = ApacheZdb.Open("APACHE00.ZDB");
    ///   byte[] patched  = File.ReadAllBytes("FTScore_patched.bin");
    ///   byte[] newBytes = apache.EncryptSection(ApacheZdb.FTScore, patched,
    ///       chunk => ZdbDispatch.EncodeFromAddress(destAddrs[chunk]));
    ///   apache.SetSectionPayload(ApacheZdb.FTScore, newBytes);
    ///   apache.Archive.Save("APACHE00_revival.ZDB");
    /// </code>
    /// </summary>
    public sealed class ApacheZdb : IDisposable
    {
        // Section name constants (lowercase, as stored in ZDB directory)
        public const string FTScore = "ftscore";
        public const string ZSealEtc = "zsealetc";

        private readonly byte[] _rawBytes;
        private readonly byte[] _key;
        private readonly byte[] _iv;

        /// <summary>Underlying archive for direct entry access, save, etc.</summary>
        public ZdbArchive Archive { get; }

        private ApacheZdb(byte[] raw, ZdbArchive archive, byte[] key, byte[] iv)
        {
            _rawBytes = raw;
            Archive = archive;
            _key = key;
            _iv = iv;
        }

        // ------------------------------------------------------------------
        // Factory
        // ------------------------------------------------------------------

        public static ApacheZdb Open(string filePath)
        {
            byte[] raw = File.ReadAllBytes(filePath);
            var archive = ZdbArchive.FromBytes(raw);
            var (key, iv) = ZdbCryptoProcessor.ExtractKeyIv(raw);
            return new ApacheZdb(raw, archive, key, iv);
        }

        public static ApacheZdb FromBytes(byte[] data)
        {
            var archive = ZdbArchive.FromBytes(data);
            var (key, iv) = ZdbCryptoProcessor.ExtractKeyIv(data);
            return new ApacheZdb((byte[])data.Clone(), archive, key, iv);
        }

        // ------------------------------------------------------------------
        // Key / IV
        // ------------------------------------------------------------------

        /// <summary>AES-128 key from file offset 0x458 (gMediusSessionKey).</summary>
        public ReadOnlySpan<byte> Key => _key;

        /// <summary>AES-128 IV from file offset 0x468 (gMediusSessionIV).</summary>
        public ReadOnlySpan<byte> Iv => _iv;

        // ------------------------------------------------------------------
        // Decrypt / Encrypt (section name = FTScore or ZSealEtc constant)
        // ------------------------------------------------------------------

        /// <summary>Decrypts a named section and returns MWo3 plaintext.</summary>
        public byte[] DecryptSection(
            string sectionName,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            ZdbEntry entry = RequireEntry(sectionName);
            return ZdbCryptoProcessor.DecryptSection(
                entry.Data, _key, _iv, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// Decrypts a named section using address-derived dispatch words.
        /// Optional exclusion words can be applied to every chunk.
        /// </summary>
        public byte[] DecryptSectionAddressDerived(
            string sectionName,
            uint sectionLoadAddress,
            int[]? fixedExclusionWordIndices = null)
        {
            Func<int, uint> getDispatchWord =
                ApacheZdbProfiles.CreateAddressDerivedDispatchProvider(sectionLoadAddress);

            Func<int, ISet<int>?>? getExclusionSet = null;
            if (fixedExclusionWordIndices != null && fixedExclusionWordIndices.Length > 0)
            {
                getExclusionSet = ApacheZdbProfiles.CreateFixedExclusionProvider(fixedExclusionWordIndices);
            }

            return DecryptSection(sectionName, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// One-call FTScore decrypt using mapped defaults:
        /// - load address: 0x001E7000
        /// - exclusions: {8, 10, 11}
        ///
        /// Throws if the output does not parse as MWo3 (useful for quick validation).
        /// </summary>
        public byte[] DecryptFTScorePatch(bool validateMwo3 = true)
        {
            byte[] plain = DecryptSectionAddressDerived(
                FTScore,
                ApacheZdbProfiles.FTScoreLoadAddress,
                ApacheZdbProfiles.FTScoreWordExclusions);

            if (validateMwo3)
                _ = MWo3Header.Parse(plain);

            return plain;
        }

        /// <summary>
        /// One-call zsealetc decrypt using mapped defaults:
        /// - load address: 0x00188938
        /// - exclusions: none (current best-known profile)
        ///
        /// This section is typically raw code/data, not MWo3.
        /// </summary>
        public byte[] DecryptZSealEtcPatch()
        {
            return DecryptSectionAddressDerived(
                ZSealEtc,
                ApacheZdbProfiles.ZSealEtcLoadAddress,
                ApacheZdbProfiles.ZSealEtcWordExclusions);
        }

        /// <summary>
        /// Decrypts all known APACHE00 payload sections with mapped defaults.
        /// Returns a dictionary keyed by section name (ftscore, zsealetc).
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> DecryptAllKnownPatchSections(bool validateFtScoreMwo3 = true)
        {
            byte[] ft = DecryptFTScorePatch(validateFtScoreMwo3);
            byte[] zs = DecryptZSealEtcPatch();

            return new Dictionary<string, byte[]>
            {
                [FTScore] = ft,
                [ZSealEtc] = zs,
            };
        }

        /// <summary>Encrypts a plaintext MWo3 payload for the named section.</summary>
        public byte[] EncryptSection(
            string sectionName,
            byte[] plaintext,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            // Validate MWo3 magic before encrypting to catch mistakes early.
            _ = MWo3Header.Parse(plaintext);

            return ZdbCryptoProcessor.EncryptSection(
                plaintext, _key, _iv, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// Encrypts a named section using address-derived dispatch words.
        /// Optional exclusion words can be applied to every chunk.
        /// </summary>
        public byte[] EncryptSectionAddressDerived(
            string sectionName,
            byte[] plaintext,
            uint sectionLoadAddress,
            int[]? fixedExclusionWordIndices = null)
        {
            Func<int, uint> getDispatchWord =
                ApacheZdbProfiles.CreateAddressDerivedDispatchProvider(sectionLoadAddress);

            Func<int, ISet<int>?>? getExclusionSet = null;
            if (fixedExclusionWordIndices != null && fixedExclusionWordIndices.Length > 0)
            {
                getExclusionSet = ApacheZdbProfiles.CreateFixedExclusionProvider(fixedExclusionWordIndices);
            }

            return EncryptSection(sectionName, plaintext, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// One-call FTScore encrypt using mapped defaults:
        /// - load address: 0x001E7000
        /// - exclusions: {8, 10, 11}
        /// </summary>
        public byte[] EncryptFTScorePatch(byte[] plaintext)
        {
            return EncryptSectionAddressDerived(
                FTScore,
                plaintext,
                ApacheZdbProfiles.FTScoreLoadAddress,
                ApacheZdbProfiles.FTScoreWordExclusions);
        }

        /// <summary>
        /// One-call zsealetc encrypt using mapped defaults:
        /// - load address: 0x00188938
        /// - exclusions: none
        /// </summary>
        public byte[] EncryptZSealEtcPatch(byte[] plaintext)
        {
            return EncryptSectionAddressDerived(
                ZSealEtc,
                plaintext,
                ApacheZdbProfiles.ZSealEtcLoadAddress,
                ApacheZdbProfiles.ZSealEtcWordExclusions);
        }

        /// <summary>
        /// Replaces the payload of a named section with already-encrypted bytes.
        /// Call Archive.Save() afterwards to write the updated ZDB to disk.
        /// </summary>
        public void SetSectionPayload(string sectionName, byte[] encryptedPayload)
        {
            ZdbEntry entry = RequireEntry(sectionName);
            int idx = IndexOfEntry(entry);
            Archive.ReplaceEntryData(idx, encryptedPayload);
        }

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------

        public void Dispose() => Archive.Dispose();

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private ZdbEntry RequireEntry(string name)
        {
            ZdbEntry? e = Archive.FindEntry(name)
                       ?? Archive.FindByName(name).FirstOrDefault();
            if (e is null)
                throw new KeyNotFoundException(
                    $"Section '{name}' not found in archive.");
            return e;
        }

        /// <summary>
        /// Returns the 0-based index of <paramref name="entry"/> in the archive entry list.
        /// Needed because <see cref="IReadOnlyList{T}"/> does not expose IndexOf.
        /// </summary>
        private int IndexOfEntry(ZdbEntry entry)
        {
            IReadOnlyList<ZdbEntry> entries = Archive.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i], entry)) return i;
            throw new InvalidOperationException("Entry not found in archive.");
        }
    }
}
