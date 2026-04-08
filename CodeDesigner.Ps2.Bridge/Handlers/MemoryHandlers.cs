using System;
using System.Text.Json;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.IOP;

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
        // EeUncachedRamEnd (0x22000000) is the exclusive upper bound.
        private const uint EeUncachedRamBase = 0x20000000u;
        private const uint EeUncachedRamEnd  = 0x22000000u;   // exclusive

        // IOP: the EE VTLB exposes IOP RAM at 0x1C000000–0x1DFFFFFF (PCSX2 vtlb.cpp).
        // 0x1C000000–0x1DFFFFFF → physical IOP RAM 0x00000000–0x001FFFFF (wrap at RamSize).
        // EeIopMirrorEnd (0x1E000000) is the exclusive upper bound.
        private const uint EeIopMirrorBase = 0x1C000000u;
        private const uint EeIopMirrorEnd  = 0x1E000000u;     // exclusive

        /// <summary>
        /// Translate EE addresses from the UI/bridge into a PINE-friendly virtual address.
        /// The VSCode UI uses physical bases for EE RAM/BIOS, while PCSX2 PINE expects
        /// EE virtual addresses. We map physical and uncached mirrors into KSEG0,
        /// while IOP mirror addresses are mapped to KSEG1 to avoid PCSX2 TLB lookups.
        /// For IOP reads prefer <see cref="HandleReadMem"/> which adds bounds checks.
        /// </summary>
        private static uint TranslatePineAddress(uint address, bool useIop)
        {
            // IOP mirror: map to KSEG1 (0xBC000000+) to bypass TLB in PCSX2.
            // Reading the IOP region as a user-space virtual address triggers TLB exceptions
            // in PCSX2. KSEG1 (0xA0000000–0xBFFFFFFF) bypasses TLB entirely:
            //   virtual 0xBC000000 = physical 0x1C000000 = EE IOP mirror base.
            // Addresses may arrive in two formats; both are normalised to the same IOP offset:
            //   • EE mirror format: 0x1C000000+offset → offset = address - EeIopMirrorBase
            //   • Direct IOP physical: 0x00000000+offset → offset = address
            // Both yield the same KSEG1 result: 0xBC000000 + iopOffset.
            if (useIop || (address >= EeIopMirrorBase && address < EeIopMirrorEnd))
            {
                uint iopOffset = address >= EeIopMirrorBase ? address - EeIopMirrorBase : address;
                return 0xBC000000u + iopOffset;
            }

            // Preserve scratchpad address (hardware-fixed EE VTLB page, no TLB needed).
            if (address >= 0x70000000u &&
                address < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                return address;

            // If already KSEG0/KSEG1, keep it.
            if (address >= 0x80000000u && address <= 0xBFFFFFFFu)
                return address;

            // Uncached user mirror → physical.
            if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                address -= EeUncachedRamBase;

            // Physical RAM/BIOS → KSEG0 cached alias for PINE.
            if (address < 0x20000000u)
                return address | 0x80000000u;

            return address;
        }

        /// <summary>
        /// Handle READMEM command — reads raw bytes from the live emulator memory.
        /// In PINE mode the bytes are read from PCSX2 via the PINE IPC socket.
        /// </summary>
        /// <param name="address">Start virtual address (EE or IOP address space).</param>
        /// <param name="length">Number of bytes to read (capped at MaxReadMemLength).</param>
        /// <param name="region">"ee" for EE bus, "iop" for IOP bus (IOP only in internal mode).</param>
        private string HandleReadMem(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");

            if (length <= 0 || length > MaxReadMemLength)
                return CreateErrorResponse($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            try
            {
                byte[] bytes;

                if (IsPineMode)
                {
                    // ── PINE path: forward read to PCSX2 ─────────────────────
                    // IOP RAM is accessed via KSEG1 of the EE IOP mirror (0xBC000000+).
                    // The read is capped at IOP RAM size (2 MB) to avoid reading past
                    // the mirror boundary into undefined EE memory that could crash PCSX2.
                    if (useIop)
                    {
                        uint iopOffset = address < EeIopMirrorBase
                            ? address
                            : address - EeIopMirrorBase;
                        uint iopLimit  = (uint)IopMemoryBus.RamSize;
                        if (iopOffset >= iopLimit)
                            return CreateErrorResponse($"IOP address 0x{address:X8} is beyond IOP RAM ({IopMemoryBus.RamSize / (1024 * 1024)} MB)");
                        int safeLen = (int)Math.Min((uint)length, iopLimit - iopOffset);
                        uint pineAddr = 0xBC000000u + iopOffset;
                        bytes = PineClient!.ReadBytes(pineAddr, safeLen);
                    }
                    else
                    {
                        uint pineAddr = TranslatePineAddress(address, useIop: false);
                        bytes = PineClient!.ReadBytes(pineAddr, length);
                    }
                }
                else
                {
                    // ── Internal emulator path ────────────────────────────────
                    bytes = new byte[length];

                    for (int i = 0; i < length; i++)
                    {
                        uint vaddr = address + (uint)i;

                        if (useIop)
                        {
                            uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                            if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                                physAddr -= EeIopMirrorBase;
                            bytes[i] = physAddr < (uint)IopMemoryBus.RamSize
                                ? Scheduler!.IOP.Bus.Ram[physAddr]
                                : (byte)0;
                        }
                        else
                        {
                            uint physAddr;
                            if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                                physAddr = vaddr & 0x1FFFFFFFu;
                            else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                                physAddr = vaddr - EeUncachedRamBase;
                            else
                                physAddr = vaddr;

                            if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                                bytes[i] = Scheduler!.EE.Bus.Ram[physAddr];
                            else if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                                     physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                                bytes[i] = Scheduler!.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];
                            else if (vaddr >= 0x70000000u &&
                                     vaddr <  0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                                bytes[i] = Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u];
                            else
                                bytes[i] = 0;
                        }
                    }
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
        /// Handle WRITEMEM command — write arbitrary bytes to live EE or IOP memory.
        /// The bytes are supplied as a hex string (spaces, commas and "0x" prefixes are ignored).
        /// In PINE mode, writes are forwarded to PCSX2 via the PINE IPC socket.
        /// </summary>
        private string HandleWriteMem(uint address, string? hexData, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
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

            if (IsPineMode)
            {
                try
                {
                    uint pineAddr = TranslatePineAddress(address, useIop);
                    PineClient!.WriteBytes(pineAddr, bytes);
                    return CreateSuccessResponse($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE WriteMem error: {ex.Message}"); }
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                uint vaddr = address + (uint)i;
                if (useIop)
                {
                    // Supported address windows for IOP writes:
                    //   0x00000000–0x003FFFFF  physical IOP RAM (direct)
                    //   0x80000000–0xBFFFFFFF  KSEG0/KSEG1 — strip top 3 bits
                    //   0x1C000000–0x1DFFFFFF  EE VTLB mirror of IOP RAM
                    // The IOP RAM panel uses region='iop' and sends addresses starting
                    // at 0x1C000000, so the EeIopMirrorBase subtraction is essential.
                    uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                    if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                        physAddr -= EeIopMirrorBase;
                    if (physAddr < (uint)IopMemoryBus.RamSize)
                        Scheduler!.IOP.Bus.Ram[physAddr] = bytes[i];
                }
                else
                {
                    uint physAddr;
                    if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                        physAddr = vaddr & 0x1FFFFFFFu;
                    else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                        physAddr = vaddr - EeUncachedRamBase;
                    else
                        physAddr = vaddr;

                    if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                        Scheduler!.EE.Bus.Ram[physAddr] = bytes[i];
                    else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                        Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u] = bytes[i];
                }
            }
            return CreateSuccessResponse($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
        }

        /// <summary>
        /// Handle LOADBIN command — read a binary file from disk and copy it directly
        /// into live emulator memory at the specified address.  This is more efficient
        /// than WRITEMEM for large files because the file data is never transferred over
        /// the socket; the server reads it locally from <paramref name="filePath"/>.
        /// </summary>
        private string HandleLoadBin(string? filePath, uint address, string? region)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("InputPath is required for LOADBIN");
            if (!System.IO.File.Exists(filePath))
                return CreateErrorResponse($"File not found: {filePath}");

            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Could not read file: {ex.Message}");
            }

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            if (useIop)
            {
                // Translate the start address and bulk-copy when the entire range fits in IOP RAM.
                uint startPhys = IopMemoryBus.TranslateAddress(address);
                if (startPhys >= EeIopMirrorBase && startPhys < EeIopMirrorEnd)
                    startPhys -= EeIopMirrorBase;

                if (startPhys + (uint)bytes.Length <= (uint)IopMemoryBus.RamSize)
                {
                    Array.Copy(bytes, 0, Scheduler.IOP.Bus.Ram, (int)startPhys, bytes.Length);
                }
                else
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        uint vaddr    = address + (uint)i;
                        uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                        if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                            physAddr -= EeIopMirrorBase;
                        if (physAddr < (uint)IopMemoryBus.RamSize)
                            Scheduler.IOP.Bus.Ram[physAddr] = bytes[i];
                    }
                }
            }
            else
            {
                // Translate the EE start address and bulk-copy when the entire range fits in Main RAM.
                uint startPhys;
                if (address >= 0x80000000u && address < 0xC0000000u)
                    startPhys = address & 0x1FFFFFFFu;
                else if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                    startPhys = address - EeUncachedRamBase;
                else
                    startPhys = address;

                if (startPhys + (uint)bytes.Length <= (uint)PS2MemoryBus.MainRamSize)
                {
                    Array.Copy(bytes, 0, Scheduler.EE.Bus.Ram, (int)startPhys, bytes.Length);
                }
                else
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        uint vaddr = address + (uint)i;
                        uint physAddr;
                        if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                            physAddr = vaddr & 0x1FFFFFFFu;
                        else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                            physAddr = vaddr - EeUncachedRamBase;
                        else
                            physAddr = vaddr;

                        if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                            Scheduler.EE.Bus.Ram[physAddr] = bytes[i];
                        else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                            Scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u] = bytes[i];
                    }
                }
            }
            return CreateSuccessResponse($"Loaded {bytes.Length} byte(s) from {System.IO.Path.GetFileName(filePath)} to 0x{address:X8}");
        }

        /// <summary>
        /// Read <paramref name="length"/> bytes starting at <paramref name="startAddress"/>
        /// directly from the live emulator RAM arrays.  In PINE mode the bytes are
        /// fetched from PCSX2 via the PINE IPC socket.
        /// Used by SCANSTRINGS, SCANFUNCTIONS, and SEARCH so they can scan whole
        /// regions without the 64 KB cap that READMEM enforces.
        /// </summary>
        private byte[] ReadLiveBytes(uint startAddress, int length, bool useIop)
        {
            if (IsPineMode)
            {
                uint pineAddr = TranslatePineAddress(startAddress, useIop);
                return PineClient!.ReadBytes(pineAddr, length);
            }

            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                uint vaddr = startAddress + (uint)i;
                if (useIop)
                {
                    uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                    if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                        physAddr -= EeIopMirrorBase;
                    bytes[i] = physAddr < (uint)IopMemoryBus.RamSize
                        ? Scheduler!.IOP.Bus.Ram[physAddr]
                        : (byte)0;
                }
                else
                {
                    uint physAddr;
                    if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                        physAddr = vaddr & 0x1FFFFFFFu;
                    else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                        physAddr = vaddr - EeUncachedRamBase;
                    else
                        physAddr = vaddr;

                    if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                        bytes[i] = Scheduler!.EE.Bus.Ram[physAddr];
                    else if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                             physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                        bytes[i] = Scheduler!.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];
                    else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                        bytes[i] = Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u];
                    else
                        bytes[i] = 0;
                }
            }
            return bytes;
        }

        // Maximum region size for READMEMREGION (32 MB — covers all of EE main RAM).
        private const int MaxReadMemRegionSize = 32 * 1024 * 1024;

        // Maximum pattern-only scan size (also 32 MB for full EE RAM coverage).
        private const int MaxScanPatternSize = 32 * 1024 * 1024;

        // Chunk size when reading large regions in PINE mode (avoids single giant PINE request).
        private const int PineScanChunkSize = 1 * 1024 * 1024; // 1 MB per chunk

        /// <summary>
        /// Handle READMEMREGION command — reads a contiguous address range and
        /// optionally searches for a byte pattern within the result.
        /// Returns the region as a hex string and a list of absolute EE addresses
        /// where the pattern was found.
        /// </summary>
        /// <param name="start">Inclusive start virtual EE address.</param>
        /// <param name="end">Exclusive end virtual EE address (must be &gt; start).</param>
        /// <param name="searchPattern">
        /// Optional byte sequence to search for.  Each element is an unsigned byte
        /// value (0–255).  Pass <c>null</c> to skip the pattern search.
        /// </param>
        /// <param name="region">"ee" (default) or "iop".</param>
        private string HandleReadMemRegion(uint start, uint end, IList<int>? searchPattern, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");

            if (end <= start)
                return CreateErrorResponse("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxReadMemRegionSize)
                return CreateErrorResponse($"Region too large — maximum is {MaxReadMemRegionSize / (1024 * 1024)} MB " +
                                           $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            // Clamp IOP reads to IOP RAM size so we never send PINE requests beyond
            // the valid IOP mirror range (which would crash PCSX2).
            if (useIop)
            {
                // Normalise IOP addresses: strip EE IOP mirror base so start/end are
                // IOP-physical offsets, then clamp to IOP RAM size.
                if (start >= EeIopMirrorBase && start < EeIopMirrorEnd)
                    start -= EeIopMirrorBase;
                if (end >= EeIopMirrorBase && end < EeIopMirrorEnd)
                    end -= EeIopMirrorBase;

                uint iopLimit = (uint)IopMemoryBus.RamSize;
                if (start >= iopLimit)
                    return CreateErrorResponse($"IOP start address 0x{start:X8} is beyond IOP RAM ({iopLimit / (1024 * 1024)} MB)");
                if (end > iopLimit)
                    end = iopLimit;

                regionSize = end - start;
                if (regionSize <= 0)
                    return CreateErrorResponse("EndAddress must be greater than Address after IOP clamping");
            }

            try
            {
                byte[] bytes = ReadLiveBytesChunked(start, (int)regionSize, useIop);

                // Pattern search (simple sliding window — region is at most 32 MB).
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
                            // -1 is a wildcard — matches any byte.
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
        /// Handle SCANPATTERN command — scans a memory range for a byte pattern and
        /// returns only the matching addresses, without returning the raw region bytes.
        /// Supports up to 32 MB (full EE main RAM).  In PINE mode the scan is done
        /// in 1 MB chunks to keep individual PINE requests manageable.
        /// </summary>
        /// <param name="start">Inclusive start address (default 0 = start of EE RAM).</param>
        /// <param name="end">Exclusive end address (0 = use default max for region).</param>
        /// <param name="pattern">Byte sequence to search for — must not be empty.</param>
        /// <param name="region">"ee" (default) or "iop".</param>
        private string HandleScanPattern(uint start, uint end, IList<int>? pattern, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");

            if (pattern == null || pattern.Count == 0)
                return CreateErrorResponse("SearchPattern is required for SCANPATTERN");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            // Default scan range when end is 0
            if (end == 0)
            {
                end = useIop
                    ? (uint)IopMemoryBus.RamSize   // IOP: 2 MB
                    : 0x02000000u;                 // EE:  32 MB
            }

            if (end <= start)
                return CreateErrorResponse("EndAddress must be greater than Address");

            long regionSize = (long)end - start;
            if (regionSize > MaxScanPatternSize)
                return CreateErrorResponse($"Scan range too large — maximum is {MaxScanPatternSize / (1024 * 1024)} MB " +
                                           $"(requested {regionSize / (1024 * 1024.0):F1} MB)");

            // For IOP in PINE mode, clamp to IOP RAM size to avoid reading past valid IOP memory.
            if (useIop && IsPineMode)
            {
                uint iopLimit = (uint)IopMemoryBus.RamSize;
                if (end > iopLimit)
                    end = iopLimit;
                if (start >= end)
                    return CreateErrorResponse("Start address is beyond IOP RAM size");
                regionSize = end - start;
            }

            try
            {
                var matchAddresses = new List<uint>();
                int patLen = pattern.Count;
                // Keep the pattern as int[] so -1 wildcard entries are preserved.
                var patInts = pattern.ToArray();

                // Ensure chunk size is always larger than the pattern so the advance
                // step is always positive (no infinite-loop risk for very long patterns).
                int  chunkSize = Math.Max(PineScanChunkSize, patLen + 1);
                int  overlap   = patLen - 1;   // bytes to re-read from the previous chunk
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
                            // -1 is a wildcard — matches any byte.
                            if (patInts[j] != -1 && chunk[i + j] != (byte)patInts[j]) { found = false; break; }
                        }
                        if (found)
                            matchAddresses.Add(scanPos + (uint)i);
                    }

                    if (chunkEnd >= end) break;

                    // Advance so the next chunk starts (patLen-1) bytes before the end
                    // of this chunk, covering patterns that straddle the boundary.
                    int advance = chunkSize - overlap;   // always >= 1 since chunkSize > overlap
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
        /// Reads bytes in 1 MB chunks in PINE mode to avoid sending one huge PINE request.
        /// In internal mode delegates directly to <see cref="ReadLiveBytes"/> (RAM is local).
        /// For IOP in PINE mode the same explicit KSEG1 translation used by
        /// <see cref="HandleReadMem"/> is applied: <c>0xBC000000 + iopOffset</c>.
        /// </summary>
        private byte[] ReadLiveBytesChunked(uint startAddress, int length, bool useIop)
        {
            if (!IsPineMode)
                return ReadLiveBytes(startAddress, length, useIop);

            // PINE mode: break the read into 1 MB chunks.
            var result = new byte[length];
            int remaining  = length;
            int destOffset = 0;
            uint addr      = startAddress;

            while (remaining > 0)
            {
                int  toRead   = Math.Min(remaining, PineScanChunkSize);
                uint pineAddr;
                if (useIop)
                {
                    // Use the same explicit KSEG1 IOP translation as HandleReadMem.
                    uint iopOffset = addr < EeIopMirrorBase ? addr : addr - EeIopMirrorBase;
                    pineAddr = 0xBC000000u + iopOffset;
                }
                else
                {
                    pineAddr = TranslatePineAddress(addr, useIop: false);
                }
                byte[] chunk = PineClient!.ReadBytes(pineAddr, toRead);
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
