using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Memory read/write commands and the bulk-region helpers that build on
    /// them: READMEM, WRITEMEM, LOADBIN, READMEMREGION, SCANPATTERN.
    /// <para>
    /// All commands route through <see cref="IBridgeContext.Client"/>, so this
    /// service works identically against PCSX2 PINE and the built-in
    /// emulator's InternalPineSocket.
    /// </para>
    /// <para>
    /// This is the canonical example of an <see cref="ICommandService"/> —
    /// see <see cref="ICommandService"/> for the migration recipe.
    /// </para>
    /// </summary>
    internal sealed class MemoryService : ICommandService
    {
        private const int MaxReadMemLength       = 65536;
        private const int MaxReadMemRegionSize   = 32 * 1024 * 1024;
        private const int MaxScanPatternSize     = 32 * 1024 * 1024;
        private const int ScanChunkSize          = 1 * 1024 * 1024;

        private readonly IBridgeContext _ctx;

        public MemoryService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleReadMem(c.Address ?? 0, c.Length ?? 256, c.Region),
                "READMEM");
            reg.Add(c => HandleReadMemBatch(c.Addresses, c.Length ?? 4, c.Region),
                "READMEMBATCH");
            reg.Add(c => HandleWriteMem(c.Address ?? 0, c.Data, c.Region),
                "WRITEMEM");
            reg.Add(c => HandleLoadBin(c.InputPath, c.Address ?? 0, c.Region),
                "LOADBIN");
            reg.Add(c => HandleReadMemRegion(c.Address ?? 0, c.EndAddress ?? 0,
                                             c.SearchPattern, c.Region),
                "READMEMREGION");
            reg.Add(c => HandleScanPattern(c.Address ?? 0, c.EndAddress ?? 0,
                                           c.SearchPattern, c.Region),
                "SCANPATTERN");
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        private string HandleReadMem(uint address, int length, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (length <= 0 || length > MaxReadMemLength)
                return Responses.Error($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            try
            {
                byte[] bytes;
                if (useIop)
                {
                    uint iopOffset = address < AddressTranslator.EeIopMirrorBase
                        ? address
                        : address - AddressTranslator.EeIopMirrorBase;
                    if (iopOffset >= AddressTranslator.IopRamSize)
                        return Responses.Error($"IOP address 0x{address:X8} is beyond IOP RAM ({AddressTranslator.IopRamSize / (1024 * 1024)} MB)");
                    int safeLen = (int)Math.Min((uint)length, AddressTranslator.IopRamSize - iopOffset);
                    uint clientAddr = 0xBC000000u + iopOffset;
                    bytes = _ctx.Client.ReadBytes(clientAddr, safeLen);
                }
                else
                {
                    uint clientAddr = AddressTranslator.ToClientAddress(address, useIop: false);
                    bytes = _ctx.Client.ReadBytes(clientAddr, length);
                }

                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "READMEM",
                    Data    = Convert.ToBase64String(bytes)
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ReadMem error: {ex.Message}");
            }
        }

        private string HandleWriteMem(uint address, string? hexData, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(hexData))
                return Responses.Error("No data provided");

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
                return Responses.Error("Invalid hex data — expected pairs of hex digits");
            }

            try
            {
                uint clientAddr = AddressTranslator.ToClientAddress(address, useIop);
                _ctx.Client.WriteBytes(clientAddr, bytes);
                return Responses.Success($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
            }
            catch (Exception ex) { return Responses.Error($"WriteMem error: {ex.Message}"); }
        }

        private string HandleReadMemBatch(IList<uint>? addresses, int length, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (addresses == null || addresses.Count == 0)
                return Responses.Error("Addresses is required for READMEMBATCH");
            if (length <= 0 || length > MaxReadMemLength)
                return Responses.Error($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            var map = new Dictionary<string, string>(addresses.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var address in addresses)
            {
                try
                {
                    byte[] bytes;
                    if (useIop)
                    {
                        uint iopOffset = address < AddressTranslator.EeIopMirrorBase
                            ? address
                            : address - AddressTranslator.EeIopMirrorBase;
                        if (iopOffset >= AddressTranslator.IopRamSize)
                            continue;
                        int safeLen = (int)Math.Min((uint)length, AddressTranslator.IopRamSize - iopOffset);
                        uint clientAddr = 0xBC000000u + iopOffset;
                        bytes = _ctx.Client.ReadBytes(clientAddr, safeLen);
                    }
                    else
                    {
                        uint clientAddr = AddressTranslator.ToClientAddress(address, useIop: false);
                        bytes = _ctx.Client.ReadBytes(clientAddr, length);
                    }

                    map[address.ToString("X8")] = Convert.ToBase64String(bytes);
                }
                catch
                {
                    // Best-effort batch read: skip unreadable addresses rather than
                    // failing the entire batch.
                }
            }

            return Responses.Json(new CommandResponse
            {
                Success = true,
                Command = "READMEMBATCH",
                ReadMemBatch = map
            });
        }

        private string HandleLoadBin(string? filePath, uint address, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(filePath))
                return Responses.Error("InputPath is required for LOADBIN");
            if (!System.IO.File.Exists(filePath))
                return Responses.Error($"File not found: {filePath}");

            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(filePath); }
            catch (Exception ex) { return Responses.Error($"Could not read file: {ex.Message}"); }

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            try
            {
                uint clientAddr = AddressTranslator.ToClientAddress(address, useIop);
                _ctx.Client.WriteBytes(clientAddr, bytes);
                return Responses.Success(
                    $"Loaded {bytes.Length} byte(s) from {System.IO.Path.GetFileName(filePath)} to 0x{address:X8}");
            }
            catch (Exception ex)
            {
                return Responses.Error($"LoadBin error: {ex.Message}");
            }
        }

        private string HandleReadMemRegion(uint start, uint end, IList<int>? searchPattern, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (end <= start)
                return Responses.Error("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxReadMemRegionSize)
                return Responses.Error($"Region too large — maximum is {MaxReadMemRegionSize / (1024 * 1024)} MB " +
                                       $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            if (useIop)
            {
                if (start >= AddressTranslator.EeIopMirrorBase && start < AddressTranslator.EeIopMirrorEnd)
                    start -= AddressTranslator.EeIopMirrorBase;
                if (end >= AddressTranslator.EeIopMirrorBase && end < AddressTranslator.EeIopMirrorEnd)
                    end -= AddressTranslator.EeIopMirrorBase;

                if (start >= AddressTranslator.IopRamSize)
                    return Responses.Error($"IOP start address 0x{start:X8} is beyond IOP RAM");
                if (end > AddressTranslator.IopRamSize) end = AddressTranslator.IopRamSize;

                regionSize = end - start;
                if (regionSize <= 0)
                    return Responses.Error("EndAddress must be greater than Address after IOP clamping");
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
                        if (found) matchAddresses.Add(start + (uint)i);
                    }
                }

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "READMEMREGION",
                    Data           = Convert.ToHexString(bytes),
                    MatchAddresses = matchAddresses,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ReadMemRegion error: {ex.Message}");
            }
        }

        private string HandleScanPattern(uint start, uint end, IList<int>? pattern, string? region)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (pattern == null || pattern.Count == 0)
                return Responses.Error("SearchPattern is required for SCANPATTERN");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            if (end == 0) end = useIop ? AddressTranslator.IopRamSize : 0x02000000u;
            if (end <= start) return Responses.Error("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxScanPatternSize)
                return Responses.Error($"Scan range too large — maximum is {MaxScanPatternSize / (1024 * 1024)} MB " +
                                       $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            if (useIop)
            {
                if (end > AddressTranslator.IopRamSize) end = AddressTranslator.IopRamSize;
                if (start >= end) return Responses.Error("Start address is beyond IOP RAM size");
                regionSize = end - start;
            }

            try
            {
                var matchAddresses = new List<uint>();
                int patLen = pattern.Count;
                var patInts = pattern.ToArray();

                int chunkSize = Math.Max(ScanChunkSize, patLen + 1);
                int overlap   = patLen - 1;
                uint scanPos  = start;

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
                        if (found) matchAddresses.Add(scanPos + (uint)i);
                    }

                    if (chunkEnd >= end) break;
                    scanPos += (uint)(chunkSize - overlap);
                }

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "SCANPATTERN",
                    MatchAddresses = matchAddresses,
                    Message        = $"Scanned {regionSize:N0} bytes in {region ?? "ee"}, found {matchAddresses.Count} match(es)",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ScanPattern error: {ex.Message}");
            }
        }

        // ── Helpers exposed for cross-service use ────────────────────────────

        /// <summary>Read a contiguous range via the active debug client.</summary>
        public byte[] ReadLiveBytes(uint startAddress, int length, bool useIop)
        {
            if (_ctx.Client == null) return Array.Empty<byte>();
            uint clientAddr = useIop
                ? 0xBC000000u + (startAddress < AddressTranslator.EeIopMirrorBase
                    ? startAddress
                    : startAddress - AddressTranslator.EeIopMirrorBase)
                : AddressTranslator.ToClientAddress(startAddress, useIop: false);
            return _ctx.Client.ReadBytes(clientAddr, length);
        }

        /// <summary>Read in 1 MB chunks. Used by SCANPATTERN, scan-string, etc.</summary>
        public byte[] ReadLiveBytesChunked(uint startAddress, int length, bool useIop)
        {
            if (_ctx.Client == null) return Array.Empty<byte>();

            var result = new byte[length];
            int remaining  = length;
            int destOffset = 0;
            uint addr      = startAddress;

            while (remaining > 0)
            {
                int  toRead     = Math.Min(remaining, ScanChunkSize);
                uint clientAddr = useIop
                    ? 0xBC000000u + (addr < AddressTranslator.EeIopMirrorBase
                        ? addr
                        : addr - AddressTranslator.EeIopMirrorBase)
                    : AddressTranslator.ToClientAddress(addr, useIop: false);
                byte[] chunk = _ctx.Client.ReadBytes(clientAddr, toRead);
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
