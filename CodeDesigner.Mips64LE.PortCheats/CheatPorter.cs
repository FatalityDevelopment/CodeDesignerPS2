using System;
using System.Collections.Generic;
using System.Linq;
using CodeDesigner.Mips64LE.Common.CheatEngine;

namespace CodeDesigner.Mips64LE.PortCheats
{
    /// <summary>
    /// Ports PS2 GameShark cheat codes from one game revision to another by scanning two
    /// raw EE-RAM dump files and relocating addresses using hex-pattern matching.
    ///
    /// Four strategies are tried in order for each unique address:
    /// <list type="number">
    ///   <item><description>S1 — before-window hex pattern (skip all-zero, filter static matches)</description></item>
    ///   <item><description>S2 — MIPS LUI+load code fingerprint (masks address bytes, searches target)</description></item>
    ///   <item><description>S3 — extended anchor search (512B scan-back, static filter)</description></item>
    ///   <item><description>S4 — pointer scan (finds 32-bit LE pointers to source addr/header)</description></item>
    /// </list>
    /// </summary>
    public static class CheatPorter
    {
        private static readonly int[] DefaultWindowSizes = { 48, 32, 16 };

        // ════════════════════════════════════════════════════════════════════════
        // Public API
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ports every cheat entry in <paramref name="blocks"/> from the address space of
        /// <paramref name="sourceData"/> to the address space of <paramref name="targetData"/>.
        /// </summary>
        /// <param name="sourceData">Raw EE-RAM dump for the source game revision.</param>
        /// <param name="targetData">Raw EE-RAM dump for the target game revision.</param>
        /// <param name="blocks">Pre-parsed cheat blocks to port.</param>
        /// <param name="logLines">
        /// Optional list that receives human-readable progress/diagnostic messages.
        /// Pass <c>null</c> to suppress logging.
        /// </param>
        /// <param name="windowSizes">
        /// Byte window sizes for strategy S1, tried largest-first.
        /// Defaults to <c>{ 48, 32, 16 }</c> when <c>null</c>.
        /// </param>
        /// <returns>One <see cref="PortCheatResult"/> per cheat entry in <paramref name="blocks"/>.</returns>
        public static List<PortCheatResult> Port(
            byte[] sourceData,
            byte[] targetData,
            IReadOnlyList<CheatBlock> blocks,
            IList<string>? logLines = null,
            int[]? windowSizes = null)
        {
            logLines    ??= new List<string>();
            windowSizes ??= DefaultWindowSizes;

            var allResults = new List<PortCheatResult>();

            // ── Phase 0: collect all unique physical addresses across every block ──
            // Many cheat lists include both an ON and an OFF code for the same address.
            // Consolidating duplicates here means each address is searched only once.
            var uniqueAddrs = new HashSet<int>();
            foreach (var b in blocks)
                foreach (var c in b.Cheats)
                    uniqueAddrs.Add(c.Address);

            int totalCheats = blocks.Sum(b => b.Cheats.Count);
            logLines.Add($"[PORTCHEATS] {totalCheats} cheat entr{(totalCheats == 1 ? "y" : "ies")} across {blocks.Count} block{(blocks.Count == 1 ? "" : "s")}, {uniqueAddrs.Count} unique address{(uniqueAddrs.Count == 1 ? "" : "es")} to resolve.");
            logLines.Add(string.Empty);

            // ── Phase 1: resolve each unique address exactly once ─────────────────
            var addressCache = new Dictionary<int, AddressSearchResult>(uniqueAddrs.Count);
            foreach (int addr in uniqueAddrs)
            {
                logLines.Add($"Resolving 0x{addr:X6}…");
                addressCache[addr] = ResolvePhysAddress(sourceData, targetData, addr, logLines, windowSizes);
                logLines.Add(string.Empty);
            }

            // ── Phase 2: emit per-block / per-cheat results using the cache ───────
            foreach (var block in blocks)
            {
                string blockName = block.Label ?? string.Empty;
                logLines.Add($"--- {(string.IsNullOrWhiteSpace(blockName) ? "(unnamed block)" : blockName)} ---");

                foreach (var cheat in block.Cheats)
                {
                    int    physAddr = cheat.Address;
                    string typeHex  = ((int)cheat.CheatType).ToString("X2");
                    // Reconstruct the value hex (big-endian as written in the cheat file).
                    byte[] valBytes = (byte[])cheat.Data.Clone();
                    Array.Reverse(valBytes);
                    string valueHex = BitConverter.ToString(valBytes).Replace("-", "");
                    string origCode = $"{typeHex}{physAddr:X6} {valueHex}";

                    var cached = addressCache[physAddr];

                    var result = new PortCheatResult
                    {
                        BlockName    = blockName,
                        BlockText    = block.BlockText,
                        OriginalCode = origCode,
                        Found        = cached.Found,
                        Method       = cached.Method,
                        Confidence   = cached.Confidence,
                        Ambiguous    = cached.Ambiguous,
                    };

                    if (cached.Found)
                    {
                        result.NewCode = $"{typeHex}{cached.NewPhysAddr:X6} {valueHex}";
                        if (cached.Ambiguous && cached.CandidateAddrs != null)
                            result.Candidates = cached.CandidateAddrs
                                .Take(8)
                                .Select(a => $"{typeHex}{a:X6} {valueHex}")
                                .ToList();
                        logLines.Add($"  {origCode} → {result.NewCode}  (confidence {cached.Confidence}%, method: {cached.Method})");
                    }
                    else
                    {
                        logLines.Add($"  {origCode} → NOT FOUND");
                    }

                    allResults.Add(result);
                }

                logLines.Add(string.Empty);
            }

            int foundCount = allResults.Count(r => r.Found);
            logLines.Add($"[PORTCHEATS] Complete: {foundCount}/{allResults.Count} codes ported.");

            return allResults;
        }

        // ════════════════════════════════════════════════════════════════════════
        // PORTCHEATS helper types
        // ════════════════════════════════════════════════════════════════════════

        private sealed class PortCheatCandidate
        {
            public int    Address    { get; set; }
            public int    Confidence { get; set; }
            public string Method     { get; set; } = string.Empty;
        }

        /// <summary>Cached resolution result for a single unique physical address.</summary>
        private sealed class AddressSearchResult
        {
            public bool       Found          { get; set; }
            public int        NewPhysAddr    { get; set; }
            public string     Method         { get; set; } = string.Empty;
            public int        Confidence     { get; set; }
            public bool       Ambiguous      { get; set; }
            /// <summary>Raw target physical addresses for ambiguous results (no code formatting).</summary>
            public List<int>? CandidateAddrs { get; set; }
        }

        private sealed class MipsCodeRef
        {
            /// <summary>Byte position of the LUI instruction in the source dump.</summary>
            public int LuiPos { get; set; }
            /// <summary>Byte position of the load/store/addiu that uses the address offset.</summary>
            public int RefPos { get; set; }
        }

        // ════════════════════════════════════════════════════════════════════════
        // Unified address resolver (runs all four strategies for one physAddr)
        // ════════════════════════════════════════════════════════════════════════

        private static AddressSearchResult ResolvePhysAddress(
            byte[] sourceData, byte[] targetData, int physAddr,
            IList<string> logLines, int[] windowSizes)
        {
            var result   = new AddressSearchResult();
            bool foundAny = false;

            // ── Strategy 1: simple before-window ────────────────────────────────
            foreach (int windowSize in windowSizes)
            {
                int patternStart = physAddr - windowSize;
                if (patternStart < 0 || physAddr >= sourceData.Length)
                {
                    logLines.Add($"    [S1 {windowSize}B window] Address out of range — skipping");
                    continue;
                }

                int actualWindow = physAddr - patternStart;
                if (actualWindow <= 0) continue;

                byte[] pattern = new byte[actualWindow];
                Array.Copy(sourceData, patternStart, pattern, 0, actualWindow);

                bool allZero = pattern.All(b => b == 0);
                if (allZero)
                {
                    logLines.Add($"    [S1 {windowSize}B window] All-zero pattern — skipping");
                    continue;
                }

                logLines.Add($"    [S1] Trying HexPattern-{actualWindow}: scanning {targetData.Length / 1024 / 1024} MB…");
                var matches = FindPattern(targetData, pattern);

                if (matches.Count == 0)
                {
                    logLines.Add($"    [S1] HexPattern-{actualWindow}: no match");
                    continue;
                }

                var dynamicMatches = matches
                    .Where(m => m + actualWindow <= sourceData.Length &&
                                !sourceData.AsSpan(m, actualWindow).SequenceEqual(pattern))
                    .ToList();

                var usefulMatches = dynamicMatches.Count > 0 ? dynamicMatches : matches;

                foundAny = true;
                bool ambiguous = usefulMatches.Count > 1;
                int  bestMatch = usefulMatches[0] + actualWindow;

                result.Found       = true;
                result.NewPhysAddr = bestMatch;
                result.Method      = $"HexPattern-{actualWindow}" + (dynamicMatches.Count > 0 ? "-filtered" : "");
                result.Confidence  = ambiguous ? Math.Max(20, 100 - (usefulMatches.Count - 1) * 25) : 100;
                result.Ambiguous   = ambiguous;

                if (ambiguous)
                {
                    result.CandidateAddrs = usefulMatches.Take(8).Select(m => m + actualWindow).ToList();
                    logLines.Add($"    [S1] HexPattern-{actualWindow}: {usefulMatches.Count} candidates — best: 0x{bestMatch:X6}");
                    foreach (var m in usefulMatches.Take(8))
                        logLines.Add($"      candidate 0x{m + actualWindow:X6}");
                }
                else
                {
                    logLines.Add($"    [S1] HexPattern-{actualWindow}: unique match → 0x{bestMatch:X6}  ✓");
                }

                break;
            }

            // ── Strategy 2: MIPS code-reference fingerprint ──────────────────────
            if (!foundAny || result.Confidence < 100)
            {
                logLines.Add($"    [S2] Trying MIPS code-reference fingerprint…");
                var codeRefs = FindMipsCodeReferences(sourceData, physAddr, logLines);
                if (codeRefs.Count > 0)
                {
                    logLines.Add($"    [S2] Found {codeRefs.Count} code reference(s) — building fingerprints");
                    foreach (var cref in codeRefs)
                    {
                        int fpBefore = Math.Min(cref.LuiPos, 32);
                        int fpAfter  = Math.Min(sourceData.Length - cref.RefPos, 16);
                        int fpStart  = cref.LuiPos - fpBefore;
                        int fpLen    = (cref.RefPos + fpAfter) - fpStart;
                        if (fpLen <= 0 || fpStart + fpLen > sourceData.Length) continue;

                        byte[] fp   = new byte[fpLen];
                        byte[] mask = new byte[fpLen];
                        Array.Copy(sourceData, fpStart, fp, 0, fpLen);
                        Array.Fill(mask, (byte)0xFF);

                        int refInFp = cref.RefPos - fpStart;
                        if (refInFp + 1 < mask.Length)
                        {
                            mask[refInFp]     = 0;
                            mask[refInFp + 1] = 0;
                        }
                        int luiInFp = cref.LuiPos - fpStart;
                        if (luiInFp + 1 < mask.Length)
                        {
                            mask[luiInFp]     = 0;
                            mask[luiInFp + 1] = 0;
                        }

                        var fpMatches = FindMaskedPattern(targetData, fp, mask, step: 4);
                        if (fpMatches.Count == 0)
                        {
                            logLines.Add($"    [S2] Fingerprint-{fpLen}B: no match");
                            continue;
                        }

                        var candidates = new List<int>();
                        foreach (int fm in fpMatches)
                        {
                            int matchRefPos = fm + refInFp;
                            if (matchRefPos + 2 > targetData.Length) continue;
                            int newLow16 = targetData[matchRefPos] | (targetData[matchRefPos + 1] << 8);

                            int matchLuiPos = fm + luiInFp;
                            if (matchLuiPos + 4 > targetData.Length) continue;
                            int newLuiImm = targetData[matchLuiPos] | (targetData[matchLuiPos + 1] << 8);

                            int newAddr = (newLuiImm << 16) + (short)newLow16;
                            if (newAddr > 0 && newAddr < targetData.Length)
                                candidates.Add(newAddr);
                        }

                        if (candidates.Count == 0) continue;

                        bool ambig = candidates.Count > 1;
                        int  best  = candidates[0];
                        int  conf  = ambig ? Math.Max(40, 100 - (candidates.Count - 1) * 30) : 100;

                        if (!foundAny || conf > result.Confidence)
                        {
                            foundAny           = true;
                            result.Found       = true;
                            result.NewPhysAddr = best;
                            result.Method      = $"CodeRef-{fpLen}B";
                            result.Confidence  = conf;
                            result.Ambiguous   = ambig;
                            result.CandidateAddrs = ambig ? candidates.Take(8).ToList() : null;

                            if (ambig)
                                logLines.Add($"    [S2] Fingerprint-{fpLen}B: {candidates.Count} candidates — best 0x{best:X6}");
                            else
                                logLines.Add($"    [S2] Fingerprint-{fpLen}B: unique match → 0x{best:X6}  ✓");
                        }

                        if (result.Confidence == 100) break;
                    }
                }
                else
                {
                    logLines.Add($"    [S2] No direct MIPS code references found");
                }
            }

            // ── Strategy 3: extended anchor search ────────────────────────────────
            if (!foundAny || result.Confidence < 100)
            {
                logLines.Add($"    [S3] Trying extended anchor search (up to 512B scan-back)…");
                var anchorCandidates = FindAnchorCandidates(sourceData, targetData, physAddr, logLines);

                if (anchorCandidates.Count > 0)
                {
                    int  best  = anchorCandidates[0].Address;
                    bool ambig = anchorCandidates.Count > 1;
                    int  conf  = anchorCandidates[0].Confidence;

                    if (!foundAny || conf > result.Confidence)
                    {
                        foundAny           = true;
                        result.Found       = true;
                        result.NewPhysAddr = best;
                        result.Method      = anchorCandidates[0].Method;
                        result.Confidence  = conf;
                        result.Ambiguous   = ambig;
                        result.CandidateAddrs = ambig
                            ? anchorCandidates.Take(8).Select(c => c.Address).ToList()
                            : null;

                        if (ambig)
                            logLines.Add($"    [S3] {anchorCandidates.Count} candidates — best 0x{best:X6} (confidence {conf}%)");
                        else
                            logLines.Add($"    [S3] Unique anchor match → 0x{best:X6}  ✓");
                    }
                }
                else
                {
                    logLines.Add($"    [S3] No anchor match found");
                }
            }

            // ── Strategy 4: pointer scan ─────────────────────────────────────────
            if (!foundAny || result.Confidence < 100)
            {
                logLines.Add($"    [S4] Trying pointer scan…");
                var ptrCandidates = FindViaPointerScan(sourceData, targetData, physAddr, logLines);

                if (ptrCandidates.Count > 0)
                {
                    int  best  = ptrCandidates[0].Address;
                    bool ambig = ptrCandidates.Count > 1;
                    int  conf  = ptrCandidates[0].Confidence;

                    if (!foundAny || conf > result.Confidence)
                    {
                        foundAny           = true;
                        result.Found       = true;
                        result.NewPhysAddr = best;
                        result.Method      = ptrCandidates[0].Method;
                        result.Confidence  = conf;
                        result.Ambiguous   = ambig;
                        result.CandidateAddrs = ambig
                            ? ptrCandidates.Take(8).Select(c => c.Address).ToList()
                            : null;

                        if (ambig)
                            logLines.Add($"    [S4] {ptrCandidates.Count} pointer candidates — best 0x{best:X6} (confidence {conf}%)");
                        else
                            logLines.Add($"    [S4] Unique pointer match → 0x{best:X6}  ✓");
                    }
                }
                else
                {
                    logLines.Add($"    [S4] No pointer match found");
                }
            }

            if (!result.Found)
                result.Method = "No match — all strategies exhausted";

            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 2: MIPS code-reference fingerprint
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans <paramref name="sourceData"/> for MIPS LUI + load/store/addiu instruction
        /// pairs that together reference <paramref name="physAddr"/>.  All standard PS2 EE
        /// virtual address spaces are tried (user, KSEG0, KSEG1).
        /// </summary>
        private static List<MipsCodeRef> FindMipsCodeReferences(
            byte[] sourceData, int physAddr, IList<string> logLines)
        {
            var refs = new List<MipsCodeRef>();

            // Three PS2 EE virtual address spaces:
            //   0x00000000 – user mode (application code, mapped 1:1 to physical)
            //   0x80000000 – KSEG0 (kernel cached, physical = vaddr & 0x1FFFFFFF)
            //   0xA0000000 – KSEG1 (kernel uncached, physical = vaddr & 0x1FFFFFFF)
            // Many games reference EE RAM through KSEG0; device drivers use KSEG1.
            int[] vbases = { 0x00000000, unchecked((int)0x80000000), unchecked((int)0xA0000000) };

            foreach (int vbase in vbases)
            {
                uint vaddr  = unchecked((uint)(vbase + physAddr));
                int  low16  = (int)(vaddr & 0xFFFF);
                int  high16 = (int)((vaddr >> 16) & 0xFFFF);
                // MIPS assemblers correct for the sign-extension applied by load/store
                // immediates: if the low 16 bits would be interpreted as negative (≥ 0x8000),
                // the assembler increments the LUI immediate by 1 so that the runtime sum
                // (LUI_result + sign_extend(imm)) still equals the intended address.
                int luiImm = (high16 + (low16 >= 0x8000 ? 1 : 0)) & 0xFFFF;

                byte luiLo = (byte)(luiImm & 0xFF);
                byte luiHi = (byte)((luiImm >> 8) & 0xFF);
                byte offLo = (byte)(low16 & 0xFF);
                byte offHi = (byte)((low16 >> 8) & 0xFF);

                // Scan 4-byte-aligned positions for LUI instructions.
                for (int i = 0; i <= sourceData.Length - 4; i += 4)
                {
                    // LUI in little-endian: [imm_lo][imm_hi][reg][0x3C]
                    if (sourceData[i] != luiLo || sourceData[i + 1] != luiHi || sourceData[i + 3] != 0x3C)
                        continue;

                    // Look forward up to 256 bytes for a load/store/addiu using the offset.
                    int limit = Math.Min(sourceData.Length - 4, i + 256);
                    for (int j = i + 4; j <= limit; j += 4)
                    {
                        if (sourceData[j] != offLo || sourceData[j + 1] != offHi) continue;

                        // In little-endian MIPS encoding byte[3] is the most-significant byte
                        // of the 32-bit instruction word; the opcode occupies its top 6 bits
                        // (bits [31:26] of the word), so opcode = byte[3] >> 2.
                        // We accept I-type immediate instructions: ALU immediates (0x08–0x0F),
                        // loads (0x20–0x27), and stores (0x28–0x2F).
                        int op = sourceData[j + 3] >> 2;
                        if ((op >= 0x08 && op <= 0x0F) || (op >= 0x20 && op <= 0x2F))
                        {
                            refs.Add(new MipsCodeRef { LuiPos = i, RefPos = j });
                            logLines.Add($"    [S2]   Found: LUI@0x{i:X} + ref@0x{j:X} (vbase=0x{(uint)vbase:X8})");
                        }
                        break;
                    }

                    if (refs.Count >= 8) break; // Enough candidates.
                }

                if (refs.Count >= 8) break;
            }

            return refs;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 3: extended anchor search
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans backwards from <paramref name="physAddr"/> up to 512 bytes to find the
        /// nearest meaningful (non-trivially-zero) byte run, then uses that run as an anchor
        /// pattern to locate the address in the target dump.  "Static" matches — those that
        /// appear at the same position in both dumps — are filtered out, leaving only the
        /// candidates that reflect the actual code-relocation between versions.
        /// When multiple dynamic candidates remain they are ranked by proximity to the
        /// source address.
        /// </summary>
        private static List<PortCheatCandidate> FindAnchorCandidates(
            byte[] sourceData, byte[] targetData, int physAddr, IList<string> logLines)
        {
            if (physAddr <= 0 || physAddr >= sourceData.Length)
                return new List<PortCheatCandidate>();

            const int MaxScanBack    = 512;
            const int MinPatternLen  = 8;    // Anchor must have at least this many bytes.
            const int MinNonZeroBytes = 4;   // Pattern must contain at least this many non-zero bytes.

            // Try progressively larger scan-back distances.
            for (int scanBack = MinPatternLen; scanBack <= MaxScanBack; scanBack += 4)
            {
                int anchorStart = physAddr - scanBack;
                if (anchorStart < 0) break;

                byte[] pattern = new byte[scanBack];
                Array.Copy(sourceData, anchorStart, pattern, 0, scanBack);

                // Count non-zero bytes — skip trivial all-zero windows.
                int nonZeroCount = pattern.Count(b => b != 0);
                if (nonZeroCount < MinNonZeroBytes) continue;

                var rawMatches = FindPattern(targetData, pattern);
                if (rawMatches.Count == 0) continue;

                // Filter static matches: positions where the source also has the same bytes.
                var dynamicMatches = rawMatches
                    .Where(m => m + scanBack <= sourceData.Length &&
                                !sourceData.AsSpan(m, scanBack).SequenceEqual(pattern.AsSpan()))
                    .ToList();

                // If all matches are static skip this window; the non-zero bytes are shared
                // verbatim between versions (e.g. static library data) and not useful for
                // locating the relocated address.
                if (dynamicMatches.Count == 0) continue;

                // Sort dynamic candidates by distance from the source address so that
                // the closest relocation is ranked first.  Use CompareTo on the absolute
                // distances to avoid integer-overflow when the dump is large.
                dynamicMatches.Sort((a, b) =>
                    Math.Abs((a + scanBack) - physAddr).CompareTo(Math.Abs((b + scanBack) - physAddr)));

                int conf = dynamicMatches.Count == 1
                    ? 100
                    : Math.Max(20, 100 - (dynamicMatches.Count - 1) * 25);

                logLines.Add($"    [S3] AnchorSearch-{scanBack}B: {dynamicMatches.Count} dynamic candidate(s)");

                return dynamicMatches
                    .Take(8)
                    .Select(m => new PortCheatCandidate
                    {
                        Address    = m + scanBack,
                        Confidence = conf,
                        Method     = $"AnchorSearch-{scanBack}B",
                    })
                    .ToList();
            }

            return new List<PortCheatCandidate>();
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 4: pointer scan
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches the source dump for 32-bit little-endian pointers that reference
        /// <paramref name="physAddr"/> directly or its structural header (physAddr minus a
        /// small fixed offset found by scanning backwards).  For each such pointer location
        /// the same 4-byte slot is read from the target dump; the resulting pointer value
        /// plus the same header-relative offset gives the candidate target address.
        ///
        /// This strategy is highly reliable when the two dumps share a table or array of
        /// per-version pointers at a stable location (e.g. a module's pad-port table).
        /// </summary>
        private static List<PortCheatCandidate> FindViaPointerScan(
            byte[] sourceData, byte[] targetData, int physAddr, IList<string> logLines)
        {
            var candidates = new List<PortCheatCandidate>();

            // Build a set of candidate "reference values" to look for as pointers:
            // the address itself plus several header offsets found by scanning backwards
            // for the nearest non-zero 4-byte-aligned value.
            var refValues = new List<(int refVal, int headerOffset)>();
            refValues.Add((physAddr, 0)); // direct pointer to the address

            // Scan backwards for non-zero 4-byte values within 64 bytes.
            for (int back = 4; back <= 64; back += 4)
            {
                int scanPos = physAddr - back;
                if (scanPos < 0 || scanPos + 4 > sourceData.Length) break;
                int word = sourceData[scanPos]
                         | (sourceData[scanPos + 1] << 8)
                         | (sourceData[scanPos + 2] << 16)
                         | (sourceData[scanPos + 3] << 24);
                if (word != 0)
                {
                    // The non-zero word at physAddr-back could be the structure header.
                    // Record a pointer to that header position.
                    refValues.Add((scanPos, back));
                    if (refValues.Count >= 4) break; // enough anchors
                }
            }

            var seen = new HashSet<int>();

            foreach (var (refVal, headerOffset) in refValues)
            {
                // Build the 4-byte LE representation of the pointer value.
                byte[] needle = new byte[4];
                needle[0] = (byte)( refVal        & 0xFF);
                needle[1] = (byte)((refVal >>  8) & 0xFF);
                needle[2] = (byte)((refVal >> 16) & 0xFF);
                needle[3] = (byte)((refVal >> 24) & 0xFF);

                // Search source dump for this pointer (not necessarily 4-byte aligned).
                var ptrLocs = FindPattern(sourceData, needle);
                if (ptrLocs.Count == 0) continue;
                if (ptrLocs.Count > 32) continue; // too common — unreliable

                logLines.Add($"    [S4] Found {ptrLocs.Count} pointer(s) to 0x{refVal:X} (hdrOffset={headerOffset})");

                foreach (int loc in ptrLocs)
                {
                    if (loc + 4 > targetData.Length) continue;

                    // Read the pointer value at the same location in the target dump.
                    int tgtPtr = targetData[loc]
                               | (targetData[loc + 1] << 8)
                               | (targetData[loc + 2] << 16)
                               | (targetData[loc + 3] << 24);

                    if (tgtPtr == 0) continue;

                    // Derive candidate target address.
                    int candidateAddr = tgtPtr + headerOffset;
                    if (candidateAddr <= 0 || candidateAddr >= targetData.Length) continue;
                    if (!seen.Add(candidateAddr)) continue;

                    logLines.Add($"    [S4]   ptr@0x{loc:X}: src=0x{refVal:X} → tgt=0x{tgtPtr:X} → addr=0x{candidateAddr:X}");
                    candidates.Add(new PortCheatCandidate
                    {
                        Address    = candidateAddr,
                        Confidence = ptrLocs.Count == 1 ? 100 : Math.Max(30, 100 - (ptrLocs.Count - 1) * 10),
                        Method     = $"PtrScan-hdr{headerOffset}",
                    });
                }
            }

            // Sort by confidence descending, then by proximity to source address.
            candidates.Sort((a, b) =>
            {
                int cmpConf = b.Confidence.CompareTo(a.Confidence);
                if (cmpConf != 0) return cmpConf;
                return Math.Abs(a.Address - physAddr).CompareTo(Math.Abs(b.Address - physAddr));
            });

            return candidates;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Low-level pattern search helpers
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches <paramref name="haystack"/> for every occurrence of
        /// <paramref name="needle"/> and returns the starting byte offset of each match.
        /// Uses <see cref="MemoryExtensions.IndexOf{T}(Span{T},ReadOnlySpan{T})"/> which is
        /// SIMD-accelerated on .NET 8 and therefore significantly faster than a manual
        /// byte-by-byte loop over the 32 MB dump files encountered in practice.
        /// </summary>
        private static List<int> FindPattern(byte[] haystack, byte[] needle)
        {
            var results = new List<int>();
            if (needle.Length == 0 || needle.Length > haystack.Length) return results;

            var h = haystack.AsSpan();
            var n = needle.AsSpan();
            int offset = 0;
            int limit  = haystack.Length - needle.Length;

            while (offset <= limit)
            {
                int idx = h.Slice(offset).IndexOf(n);
                if (idx < 0) break;
                results.Add(offset + idx);
                offset += idx + 1;
            }
            return results;
        }

        /// <summary>
        /// Searches <paramref name="haystack"/> for every occurrence of
        /// <paramref name="pattern"/> where the corresponding <paramref name="mask"/>
        /// byte is 0xFF (must match exactly) or 0x00 (wildcard — any byte accepted).
        /// </summary>
        /// <param name="step">Alignment step for the outer loop (4 for 4-byte-aligned code).</param>
        private static List<int> FindMaskedPattern(byte[] haystack, byte[] pattern, byte[] mask, int step = 1)
        {
            var results = new List<int>();
            if (pattern.Length == 0 || pattern.Length > haystack.Length) return results;

            // Find the first non-zero mask byte for a fast pre-filter.
            int firstMaskIdx = Array.FindIndex(mask, m => m != 0);
            if (firstMaskIdx < 0) return results; // All wildcards — useless.

            byte firstMaskVal = pattern[firstMaskIdx];
            int  limit        = haystack.Length - pattern.Length;

            for (int i = 0; i <= limit; i += step)
            {
                if (haystack[i + firstMaskIdx] != firstMaskVal) continue;
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (mask[j] != 0 && haystack[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) results.Add(i);
            }
            return results;
        }
    }
}
