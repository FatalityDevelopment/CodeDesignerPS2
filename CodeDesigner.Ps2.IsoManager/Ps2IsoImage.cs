using System.Text;

namespace CodeDesigner.Ps2.IsoManager;

/// <summary>
/// Reads PlayStation 2 ISO-9660 disc images (cooked ISO and raw BIN/CUE formats).
///
/// Call <see cref="LoadImage"/> or <see cref="LoadFromStream"/> to attach a disc
/// image, then use <see cref="FindFile"/>, <see cref="ReadFile"/>,
/// <see cref="ReadSectors"/>, and <see cref="EnumerateFiles"/> to access its
/// contents.
///
/// Images may be either "cooked" ISO-9660 (2048 bytes/sector) or raw BIN/CUE
/// (2352 bytes/sector with a 24-byte sector header). The format is auto-detected
/// from the sync pattern in the first sector.
/// </summary>
public sealed class Ps2IsoImage
{
    // ── ISO-9660 constants ────────────────────────────────────────────────────

    /// <summary>Logical sector size for ISO-9660 (2048 bytes).</summary>
    public const int SectorSize = 2048;

    /// <summary>Raw sector size for BIN/CUE images (2352 bytes).</summary>
    public const int RawSectorSize = 2352;

    /// <summary>Byte offset of user data within a raw Mode-1/Mode-2 sector.</summary>
    public const int RawSectorHeaderSize = 24;

    /// <summary>Logical Block Address of the Primary Volume Descriptor.</summary>
    private const int PvdLba = 16;

    // ── State ─────────────────────────────────────────────────────────────────

    private Stream? _isoStream;
    private long    _isoLength;
    private bool    _isRawImage;

    // ── Public properties ──────────────────────────────────────────────────────

    /// <summary>True when a disc image is loaded.</summary>
    public bool HasDisc => _isoLength > 0;

    /// <summary>True when the loaded image uses 2352-byte raw sectors (BIN/CUE format).</summary>
    public bool IsRawImage => _isRawImage;

    /// <summary>
    /// Total number of logical sectors in the disc image (using the sector size
    /// appropriate for the image format: 2048 bytes for cooked, 2352 for raw).
    /// </summary>
    public long SectorCount =>
        _isoLength / (_isRawImage ? RawSectorSize : SectorSize);

    // ── Image loading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Load a disc image from a byte array. The image may be a cooked ISO-9660
    /// image (2048 bytes/sector) or a raw BIN/CUE image (2352 bytes/sector with a
    /// 24-byte sector header). The format is detected automatically from the
    /// sector sync pattern.
    ///
    /// Pass <c>null</c> or an empty array to eject the disc.
    /// </summary>
    public void LoadImage(byte[]? iso)
    {
        _isoStream  = iso != null && iso.Length > 0
                          ? new MemoryStream(iso, writable: false)
                          : null;
        _isoLength  = iso?.Length ?? 0;
        _isRawImage = DetectRawImage();
    }

    /// <summary>
    /// Load a disc image from a <see cref="Stream"/>. The stream must support
    /// seeking (<see cref="Stream.CanSeek"/> must be <c>true</c>) and remain open
    /// for the lifetime of this <see cref="Ps2IsoImage"/> instance. The format is
    /// detected automatically from the sector sync pattern.
    ///
    /// Use this overload when the disc image cannot be loaded into a byte array,
    /// for example when the image file is larger than 2 GB.
    /// </summary>
    public void LoadFromStream(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        _isoStream  = stream;
        _isoLength  = stream.Length;
        _isRawImage = DetectRawImage();
    }

    // ── ISO-9660 file-system access ────────────────────────────────────────────

    /// <summary>
    /// Locate a file or directory in the ISO-9660 file system by path.
    ///
    /// Path components are separated by <c>'/'</c> or <c>'\'</c>; a leading
    /// separator is optional. Component names are matched case-insensitively
    /// and the ISO-9660 version suffix <c>";1"</c> is stripped before comparison.
    /// </summary>
    /// <param name="path">
    /// Path such as <c>"SYSTEM.CNF"</c>, <c>"/SLPM_550.01;1"</c>, or
    /// <c>"/PSP_GAME/PARAM.SFO"</c>.
    /// </param>
    /// <returns>
    /// The matching <see cref="IsoEntry"/>, or <c>null</c> when the entry is
    /// not found or no disc image is loaded.
    /// </returns>
    public IsoEntry? FindFile(string path)
    {
        if (!HasDisc || string.IsNullOrEmpty(path))
            return null;

        if (!TryReadPvd(out uint rootLba, out uint rootSize))
            return null;

        string[] parts = path.Split(new[] { '/', '\\' },
                                    StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        uint dirLba  = rootLba;
        uint dirSize = rootSize;

        for (int p = 0; p < parts.Length; p++)
        {
            string component = StripVersionSuffix(parts[p]).ToUpperInvariant();
            bool   isLast    = p == parts.Length - 1;
            bool   found     = false;

            foreach (IsoEntry entry in ListDirectory(dirLba, dirSize))
            {
                string entryName = StripVersionSuffix(entry.Name).ToUpperInvariant();
                if (entryName != component)
                    continue;

                if (isLast)
                    return entry;

                if (!entry.IsDirectory)
                    return null; // path component is not a directory

                dirLba  = entry.Lba;
                dirSize = entry.Size;
                found   = true;
                break;
            }

            if (!found)
                return null;
        }

        return null;
    }

    /// <summary>
    /// Read the raw bytes of the file described by <paramref name="entry"/>
    /// into a new array. Returns an empty array when the extent is out of
    /// range or no disc image is loaded.
    /// </summary>
    public byte[] ReadFile(IsoEntry entry)
    {
        if (!HasDisc || entry.Size == 0)
            return Array.Empty<byte>();

        long srcOffset = (long)entry.Lba * SectorSize;
        if (srcOffset < 0 || srcOffset + (long)entry.Size > _isoLength)
            return Array.Empty<byte>();

        byte[] data = new byte[entry.Size];
        ReadIsoBytes(srcOffset, data, 0, (int)entry.Size);
        return data;
    }

    /// <summary>
    /// Copy sector user-data from the disc image into <paramref name="buffer"/>.
    /// For raw BIN/CUE images the 24-byte sector header is stripped automatically.
    /// </summary>
    public void ReadSectors(uint lba, uint count, byte[] buffer, int bufferOffset = 0)
    {
        for (uint i = 0; i < count; i++)
        {
            long srcOffset = _isRawImage
                ? (long)(lba + i) * RawSectorSize + RawSectorHeaderSize
                : (long)(lba + i) * SectorSize;
            int dstOffset = bufferOffset + (int)(i * SectorSize);
            if (srcOffset >= 0 && srcOffset + SectorSize <= _isoLength
                && dstOffset + SectorSize <= buffer.Length)
            {
                ReadIsoBytes(srcOffset, buffer, dstOffset, SectorSize);
            }
        }
    }

    /// <summary>
    /// Copy full raw sectors (2352 bytes each) from the disc image into
    /// <paramref name="buffer"/> without stripping the sector header.
    /// Used for reading CD audio (CDDA) tracks from raw BIN/CUE images.
    /// When the image is a cooked ISO this method does nothing; the caller
    /// is responsible for zero-filling the buffer (silence).
    /// </summary>
    public void ReadRawSectors(uint lba, uint count, byte[] buffer, int bufferOffset = 0)
    {
        if (!_isRawImage) return;

        for (uint i = 0; i < count; i++)
        {
            long srcOffset = (long)(lba + i) * RawSectorSize;
            int  dstOffset = bufferOffset + (int)(i * RawSectorSize);
            if (srcOffset >= 0 && srcOffset + RawSectorSize <= _isoLength
                && dstOffset + RawSectorSize <= buffer.Length)
            {
                ReadIsoBytes(srcOffset, buffer, dstOffset, RawSectorSize);
            }
        }
    }

    /// <summary>
    /// Recursively enumerate all non-directory entries in the ISO-9660 file
    /// system, starting from the root directory.
    /// </summary>
    /// <returns>
    /// A sequence of <c>(string DiscPath, IsoEntry Entry)</c> tuples where
    /// <c>DiscPath</c> is the absolute disc path such as <c>/SCUS_971.98;1</c>
    /// or <c>/MODULES/CDVD.IRX;1</c>.
    /// </returns>
    public IEnumerable<(string DiscPath, IsoEntry Entry)> EnumerateFiles()
    {
        if (!HasDisc) yield break;
        if (!TryReadPvd(out uint rootLba, out uint rootSize)) yield break;

        foreach (var item in WalkDirectory("/", rootLba, rootSize))
            yield return item;
    }

    // ── ISO-9660 internal helpers ──────────────────────────────────────────────

    private bool TryReadPvd(out uint rootLba, out uint rootSize)
    {
        rootLba  = 0;
        rootSize = 0;

        long pvdOffset = _isRawImage
            ? (long)PvdLba * RawSectorSize + RawSectorHeaderSize
            : (long)PvdLba * SectorSize;
        if (pvdOffset + SectorSize > _isoLength)
            return false;

        byte[] pvd = new byte[SectorSize];
        if (ReadIsoBytes(pvdOffset, pvd, 0, SectorSize) < SectorSize)
            return false;

        // Verify PVD signature: type=1, identifier="CD001", version=1
        if (pvd[0] != 1)
            return false;
        if (pvd[1] != 'C' || pvd[2] != 'D'
         || pvd[3] != '0' || pvd[4] != '0'
         || pvd[5] != '1')
            return false;

        // Root Directory Record at PVD byte offset 156
        rootLba  = ReadU32LE(pvd, 156 + 2);
        rootSize = ReadU32LE(pvd, 156 + 10);
        return true;
    }

    private IEnumerable<IsoEntry> ListDirectory(uint dirLba, uint dirSize)
    {
        if (dirSize == 0) yield break;

        uint sectorCount = (dirSize + (uint)SectorSize - 1) / (uint)SectorSize;
        byte[] dirData   = new byte[sectorCount * SectorSize];
        ReadSectors(dirLba, sectorCount, dirData);

        int pos = 0;
        int end = (int)Math.Min(dirSize, (uint)dirData.Length);

        while (pos < end)
        {
            int recordLen = dirData[pos];
            if (recordLen == 0)
            {
                // Zero padding at sector boundary — skip to next sector
                int nextSector = ((pos / SectorSize) + 1) * SectorSize;
                if (nextSector >= end) break;
                pos = nextSector;
                continue;
            }

            if (recordLen < 34 || pos + recordLen > end)
                break;

            int nameLen = dirData[pos + 32];

            // Skip the special "." (id=0x00) and ".." (id=0x01) entries
            if (nameLen == 1 && dirData[pos + 33] <= 0x01)
            {
                pos += recordLen;
                continue;
            }

            if (nameLen > 0)
            {
                uint   entryLba  = ReadU32LE(dirData, pos + 2);
                uint   entrySize = ReadU32LE(dirData, pos + 10);
                byte   flags     = dirData[pos + 25];
                string name      = Encoding.ASCII.GetString(dirData, pos + 33, nameLen);

                yield return new IsoEntry
                {
                    Name        = name,
                    Lba         = entryLba,
                    Size        = entrySize,
                    IsDirectory = (flags & 0x02) != 0
                };
            }

            pos += recordLen;
        }
    }

    private IEnumerable<(string DiscPath, IsoEntry Entry)> WalkDirectory(
        string dirPath, uint dirLba, uint dirSize)
    {
        foreach (var entry in ListDirectory(dirLba, dirSize))
        {
            string fullPath = dirPath.TrimEnd('/') + "/" + entry.Name;
            if (entry.IsDirectory)
            {
                foreach (var sub in WalkDirectory(fullPath, entry.Lba, entry.Size))
                    yield return sub;
            }
            else
            {
                yield return (fullPath, entry);
            }
        }
    }

    private static string StripVersionSuffix(string name)
    {
        int semi = name.IndexOf(';');
        return semi >= 0 ? name[..semi] : name;
    }

    private static uint ReadU32LE(byte[] buf, int offset)
    {
        if (offset + 4 > buf.Length) return 0u;
        return (uint)(buf[offset]
                    | (buf[offset + 1] << 8)
                    | (buf[offset + 2] << 16)
                    | (buf[offset + 3] << 24));
    }

    // ── Raw-image detection ────────────────────────────────────────────────────

    /// <summary>
    /// Return true when the loaded disc image appears to be a raw 2352-byte-sector
    /// BIN/CUE image. Detection uses the CD-ROM sync pattern at byte 0 of the first
    /// sector (0x00 0xFF×11 0x00).
    /// </summary>
    private bool DetectRawImage()
    {
        if (_isoStream == null || _isoLength < RawSectorSize) return false;

        byte[] header = new byte[12];
        if (ReadIsoBytes(0, header, 0, 12) < 12) return false;

        // CD-ROM sync header: byte 0 = 0x00, bytes 1-10 = 0xFF, byte 11 = 0x00
        if (header[0] != 0x00 || header[11] != 0x00) return false;
        for (int i = 1; i <= 10; i++)
            if (header[i] != 0xFF) return false;
        return true;
    }

    // ── Stream read helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Read up to <paramref name="count"/> bytes from the disc image at
    /// <paramref name="offset"/> into <paramref name="buf"/> starting at
    /// <paramref name="bufOffset"/>. Returns the number of bytes actually read.
    /// </summary>
    private int ReadIsoBytes(long offset, byte[] buf, int bufOffset, int count)
    {
        if (_isoStream == null || offset < 0 || offset >= _isoLength)
            return 0;

        long available = _isoLength - offset;
        int toRead = (int)Math.Min(count, available);
        if (toRead <= 0) return 0;

        _isoStream.Position = offset;
        int read = 0;
        while (read < toRead)
        {
            int n = _isoStream.Read(buf, bufOffset + read, toRead - read);
            if (n == 0) break;
            read += n;
        }
        return read;
    }
}
