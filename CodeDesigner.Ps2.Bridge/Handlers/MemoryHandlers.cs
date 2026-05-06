using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private const int MaxReadMemLength = 65536;

        // Thresholds used by SCANFLOATS / GETFLOATS to filter plausible game values.
        private const float FloatScanMinAbs = 0.001f;
        private const float FloatScanMaxAbs = 1_000_000f;

        // ── Address-translation constants for HandleReadMem / scan helpers ────

        // EE: uncached direct-mapped user window (PS2 kernel TLB maps RAM here).
        // 0x20000000–0x21FFFFFF → physical EE RAM 0x00000000–0x01FFFFFF.
        private const uint EeUncachedRamBase = 0x20000000u;
        private const uint EeUncachedRamEnd  = 0x22000000u;   // exclusive

        // IOP: the EE VTLB exposes IOP RAM at 0x1C000000–0x1DFFFFFF.
        // 0x1C000000–0x1DFFFFFF → physical IOP RAM 0x00000000–0x001FFFFF.
        private const uint EeIopMirrorBase = 0x1C000000u;
        private const uint EeIopMirrorEnd  = 0x1E000000u;     // exclusive

        // IOP RAM size — 2 MB. Hard-coded to avoid an EmulatorCore dependency.
        private const uint IopRamSize = 2 * 1024 * 1024;

        /// <summary>
        /// Translate addresses from the UI/bridge into a debug-client-friendly
        /// virtual address. Both PCSX2 PINE and the built-in emulator's
        /// InternalPineSocket expect EE virtual addresses (KSEG0/KSEG1); we
        /// fold physical and uncached mirrors into KSEG0 here. IOP-mirror
        /// addresses go to KSEG1 to bypass any TLB walk.
        /// </summary>
        private static uint TranslateClientAddress(uint address, bool useIop)
        {
            if (useIop || (address >= EeIopMirrorBase && address < EeIopMirrorEnd))
            {
                uint iopOffset = address >= EeIopMirrorBase ? address - EeIopMirrorBase : address;
                return 0xBC000000u + iopOffset;
            }

            // Scratchpad — hardware-fixed page, send through unchanged.
            if (address >= 0x70000000u && address < 0x70000000u + 0x4000u)
                return address;

            // Already KSEG0/KSEG1 — pass through.
            if (address >= 0x80000000u && address <= 0xBFFFFFFFu)
                return address;

            // Uncached user mirror → physical.
            if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                address -= EeUncachedRamBase;

            // Physical RAM/BIOS → KSEG0 cached alias.
            if (address < 0x20000000u)
                return address | 0x80000000u;

            return address;
        }

        /// <summary>
        /// Handle READMEM — reads raw bytes from the live emulator memory via
        /// the active <see cref="IDebugClient"/> (PCSX2 PINE or built-in
        /// InternalPineSocket). IOP reads are clamped to IOP RAM size to avoid
        /// reading past valid memory.
        /// </summary>
        private string HandleReadMem(uint address, int length, string? region)
        {
            if (Client == null)
                return CreateErrorResponse("No live memory backend — start with --pcsx2 or STARTEMULATOR");

            if (length <= 0 || length > MaxReadMemLength)
                return CreateErrorResponse($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            try
            {
                byte[] bytes;
                if (useIop)
                {
                    uint iopOffset = address < EeIopMirrorBase
                        ? address
                        : address - EeIopMirrorBase;
                    if (iopOffset >= IopRamSize)
                        return CreateErrorResponse($"IOP address 0x{address:X8} is beyond IOP RAM ({IopRamSize / (1024 * 1024)} MB)");
                    int safeLen = (int)Math.Min((uint)length, IopRamSize - iopOffset);
                    uint clientAddr = 0xBC000000u + iopOffset;
                    bytes = Client.ReadBytes(clientAddr, safeLen);
                }
                else
                {
                    uint clientAddr = TranslateClientAddress(address, useIop: false);
                    bytes = Client.ReadBytes(clientAddr, length);
                }

                var response = new CommandResponse
                {
                    Success = true,
                    Command = "READMEM",
                    Data    = Convert.ToBase64String(bytes)
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ReadMem error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle WRITEMEM — write bytes to live EE or IOP memory through the
        /// active debug client. The bytes are supplied as a hex string
        /// (spaces, commas and "0x" prefixes are ignored).
        /// </summary>
        private string HandleWriteMem(uint address, string? hexData, string? region)
        {
            if (Client == null)
                return CreateErrorResponse("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(hexData))
                return CreateErrorResponse("No data provided");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            byte[] bytes;
            try
            {
                var cleaned = hexData
                    .Replace(" ", "").Replace(",", "")
                    .Replace("0x", "").Replace("0X", "");
                if (cleaned.Length % 2 != 0) cleaned = "0" + cleaned;
                bytes = Convert.FromHexString(cleaned);
            }
            catch (FormatException)
            {
                return CreateErrorResponse("Invalid hex data — expected pairs of hex digits");
            }

            try
            {
                uint clientAddr = TranslateClientAddress(address, useIop);
                Client.WriteBytes(clientAddr, bytes);
                return CreateSuccessResponse($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
            }
            catch (Exception ex) { return CreateErrorResponse($"WriteMem error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle LOADBIN — read a binary file from disk and copy it into live
        /// emulator memory at the specified address. More efficient than
        /// WRITEMEM for large files because the file data is read locally;
        /// only the bytes themselves traverse the debug socket.
        /// </summary>
        private string HandleLoadBin(string? filePath, uint address, string? region)
        {
            if (Client == null)
                return CreateErrorResponse("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("InputPath is required for LOADBIN");
            if (!System.IO.File.Exists(filePath))
                return CreateErrorResponse($"File not found: {filePath}");

            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(filePath); }
            catch (Exception ex) { return CreateErrorResponse($"Could not read file: {ex.Message}"); }

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            try
            {
                uint clientAddr = TranslateClientAddress(address, useIop);
                Client.WriteBytes(clientAddr, bytes);
                return CreateSuccessResponse(
                    $"Loaded {bytes.Length} byte(s) from {System.IO.Path.GetFileName(filePath)} to 0x{address:X8}");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"LoadBin error: {ex.Message}");
            }
        }

        /// <summary>
        /// Read <paramref name="length"/> bytes starting at
        /// <paramref name="startAddress"/> through the active debug client.
        /// Used by SCANSTRINGS, SCANFUNCTIONS, SCANPATTERN and SEARCH so they
        /// can scan whole regions without the 64 KB cap that READMEM enforces.
        /// </summary>
        private byte[] ReadLiveBytes(uint startAddress, int length, bool useIop)
        {
            if (Client == null) return Array.Empty<byte>();
            uint clientAddr = useIop
                ? 0xBC000000u + (startAddress < EeIopMirrorBase ? startAddress : startAddress - EeIopMirrorBase)
                : TranslateClientAddress(startAddress, useIop: false);
            return Client.ReadBytes(clientAddr, length);
        }

        // Maximum region size for READMEMREGION (32 MB — covers all of EE RAM).
        private const int MaxReadMemRegionSize = 32 * 1024 * 1024;

        // Maximum pattern-only scan size (also 32 MB for full EE RAM coverage).
        private const int MaxScanPatternSize = 32 * 1024 * 1024;

        // Chunk size when reading large regions over the debug socket
        // (avoids one huge request).
        private const int ScanChunkSize = 1 * 1024 * 1024; // 1 MB per chunk

        /// <summary>
        /// Handle READMEMREGION — reads a contiguous address range and
        /// optionally searches for a byte pattern within the result. Returns
        /// the region as a hex string and a list of absolute addresses where
        /// the pattern was found.
        /// </summary>
        private string HandleReadMemRegion(uint start, uint end, IList<int>? searchPattern, string? region)
        {
            if (Client == null)
                return CreateErrorResponse("No live memory backend — start with --pcsx2 or STARTEMULATOR");

            if (end <= start)
                return CreateErrorResponse("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxReadMemRegionSize)
                return CreateErrorResponse($"Region too large — maximum is {MaxReadMemRegionSize / (1024 * 1024)} MB " +
                                           $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            if (useIop)
            {
                if (start >= EeIopMirrorBase && start < EeIopMirrorEnd)
                    start -= EeIopMirrorBase;
                if (end >= EeIopMirrorBase && end < EeIopMirrorEnd)
                    end -= EeIopMirrorBase;

                if (start >= IopRamSize)
                    return CreateErrorResponse($"IOP start address 0x{start:X8} is beyond IOP RAM ({IopRamSize / (1024 * 1024)} MB)");
                if (end > IopRamSize)
                    end = IopRamSize;

                regionSize = end - start;
                if (regionSize <= 0)
                    return CreateErrorResponse("EndAddress must be greater than Address after IOP clamping");
            }

            try
            {
                byte[] bytes = ReadLiveBytesChunked(start, (int)regionSize, useIop);

                List<uint>? matchAddresses = null;
                if (searchPattern != null && searchPattern.Count > 0)
                {
                    matchAddresses = new List<uint>();
                    int patLen = searchPattern.Count;
                    int limit  = bytes.Length - patLen;
                    for (int i = 0; i <= limit; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < patLen; j++)
                        {
                            if (searchPattern[j] != -1 && bytes[i + j] != (byte)searchPattern[j]) { found = false; break; }
                        }
                        if (found)
                            matchAddresses.Add(start + (uint)i);
                    }
                }

                var response = new CommandResponse
                {
                    Success        = true,
                    Command        = "READMEMREGION",
                    Data           = Convert.ToHexString(bytes),
                    MatchAddresses = matchAddresses,
                };
                return System.Text.Json.JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ReadMemRegion error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANPATTERN — scans a memory range for a byte pattern and
        /// returns only the matching addresses. Up to 32 MB; reads happen in
        /// 1 MB chunks to keep individual debug-socket requests reasonable.
        /// </summary>
        private string HandleScanPattern(uint start, uint end, IList<int>? pattern, string? region)
        {
            if (Client == null)
                return CreateErrorResponse("No live memory backend — start with --pcsx2 or STARTEMULATOR");

            if (pattern == null || pattern.Count == 0)
                return CreateErrorResponse("SearchPattern is required for SCANPATTERN");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            if (end == 0)
            {
                end = useIop
                    ? IopRamSize          // IOP: 2 MB
                    : 0x02000000u;        // EE:  32 MB
            }

            if (end <= start)
                return CreateErrorResponse("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxScanPatternSize)
                return CreateErrorResponse($"Scan range too large — maximum is {MaxScanPatternSize / (1024 * 1024)} MB " +
                                           $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            if (useIop)
            {
                if (end > IopRamSize) end = IopRamSize;
                if (start >= end)
                    return CreateErrorResponse("Start address is beyond IOP RAM size");
                regionSize = end - start;
            }

            try
            {
                var matchAddresses = new List<uint>();
                int patLen = pattern.Count;
                var patInts = pattern.ToArray();

                int  chunkSize = Math.Max(ScanChunkSize, patLen + 1);
                int  overlap   = patLen - 1;
                uint scanPos   = start;

                while (scanPos < end)
                {
                    uint chunkEnd = (uint)Math.Min((long)scanPos + chunkSize, end);
                    int  len      = (int)(chunkEnd - scanPos);

                    byte[] chunk = ReadLiveBytesChunked(scanPos, len, useIop);

                    int limit = chunk.Length - patLen;
                    for (int i = 0; i <= limit; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < patLen; j++)
                        {
                            if (patInts[j] != -1 && chunk[i + j] != (byte)patInts[j]) { found = false; break; }
                        }
                        if (found)
                            matchAddresses.Add(scanPos + (uint)i);
                    }

                    if (chunkEnd >= end) break;

                    int advance = chunkSize - overlap;
                    scanPos += (uint)advance;
                }

                var response = new CommandResponse
                {
                    Success        = true,
                    Command        = "SCANPATTERN",
                    MatchAddresses = matchAddresses,
                    Message        = $"Scanned {regionSize:N0} bytes in {region ?? "ee"}, found {matchAddresses.Count} match(es)",
                };
                return System.Text.Json.JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanPattern error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads bytes in 1 MB chunks via the active debug client. For IOP,
        /// the same explicit KSEG1 translation used by <see cref="HandleReadMem"/>
        /// is applied: 0xBC000000 + iopOffset.
        /// </summary>
        private byte[] ReadLiveBytesChunked(uint startAddress, int length, bool useIop)
        {
            if (Client == null) return Array.Empty<byte>();

            var result = new byte[length];
            int remaining  = length;
            int destOffset = 0;
            uint addr      = startAddress;

            while (remaining > 0)
            {
                int  toRead   = Math.Min(remaining, ScanChunkSize);
                uint clientAddr = useIop
                    ? 0xBC000000u + (addr < EeIopMirrorBase ? addr : addr - EeIopMirrorBase)
                    : TranslateClientAddress(addr, useIop: false);
                byte[] chunk = Client.ReadBytes(clientAddr, toRead);
                int copyLen  = Math.Min(chunk.Length, toRead);
                Buffer.BlockCopy(chunk, 0, result, destOffset, copyLen);
                destOffset += copyLen;
                addr       += (uint)toRead;
                remaining  -= toRead;
            }

            return result;
        }
    }
}
