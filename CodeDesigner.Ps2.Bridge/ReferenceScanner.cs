using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Emulator;

namespace CodeDesigner.Ps2.Bridge
{
    [Flags]
    internal enum ReferenceScanKinds
    {
        None = 0,
        ControlFlow = 1 << 0,
        ResolvedAddress = 1 << 1,
        Pointer = 1 << 2,
        StringPattern = 1 << 3,
        All = ControlFlow | ResolvedAddress | Pointer | StringPattern,
    }

    internal sealed class ReferenceScanInput
    {
        public uint TargetAddress { get; init; }
        public uint BaseAddress { get; init; }
        public byte[] Bytes { get; init; } = [];
        public ReferenceScanKinds Kinds { get; init; } = ReferenceScanKinds.All;
    }

    internal sealed class ReferenceScanResult
    {
        public uint Address { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string KindLabel { get; init; } = string.Empty;
        public uint TargetAddress { get; init; }
        public string Instruction { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public string? MatchedValue { get; init; }
        public int MatchLength { get; init; }
        public List<uint> SourceAddresses { get; init; } = [];
    }

    /// <summary>
    /// Central reference scanner for disassembler address lookups.
    /// Keep address-reference behavior here so UI, search, and future backend uses
    /// do not grow separate MIPS/pointer/string scanners.
    /// </summary>
    internal static class ReferenceScanner
    {
        private const int MaxLuiWindow = 128;
        private const int MinAsciiStringLength = 4;
        private const int MaxAsciiStringLength = 256;
        private const uint EeUncachedRamBase = 0x20000000u;
        private const uint EeUncachedRamEnd = 0x22000000u;

        private static readonly string[] s_gprNames =
        [
            "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
            "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
            "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
            "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra",
        ];

        public static ReferenceScanKinds ParseKinds(IEnumerable<string>? kinds)
        {
            if (kinds == null)
                return ReferenceScanKinds.All;

            ReferenceScanKinds parsed = ReferenceScanKinds.None;
            foreach (string raw in kinds)
            {
                string kind = raw.Trim();
                if (kind.Equals("controlFlow", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("control", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("branch", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("branches", StringComparison.OrdinalIgnoreCase))
                {
                    parsed |= ReferenceScanKinds.ControlFlow;
                }
                else if (kind.Equals("resolved", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("resolvedAddress", StringComparison.OrdinalIgnoreCase))
                {
                    parsed |= ReferenceScanKinds.ResolvedAddress;
                }
                else if (kind.Equals("pointer", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("pointers", StringComparison.OrdinalIgnoreCase))
                {
                    parsed |= ReferenceScanKinds.Pointer;
                }
                else if (kind.Equals("stringPattern", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("string", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("crc32", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("pattern", StringComparison.OrdinalIgnoreCase))
                {
                    parsed |= ReferenceScanKinds.StringPattern;
                }
            }

            return parsed == ReferenceScanKinds.None ? ReferenceScanKinds.All : parsed;
        }

        public static List<ReferenceScanResult> Scan(ReferenceScanInput input)
        {
            var results = new List<ReferenceScanResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            string? targetAscii = null;
            byte[]? targetAsciiBytes = null;
            uint? targetAsciiCrc32 = null;

            if (input.Kinds.HasFlag(ReferenceScanKinds.StringPattern) &&
                TryGetOffset(input.TargetAddress, input.BaseAddress, input.Bytes.Length, out int targetOffset) &&
                TryReadAsciiString(input.Bytes, targetOffset, out targetAscii, out targetAsciiBytes))
            {
                targetAsciiCrc32 = ComputeCrc32(targetAsciiBytes);
            }

            if (input.Kinds.HasFlag(ReferenceScanKinds.ControlFlow) ||
                input.Kinds.HasFlag(ReferenceScanKinds.ResolvedAddress) ||
                targetAsciiCrc32.HasValue)
            {
                ScanInstructions(input, targetAscii, targetAsciiCrc32, results, seen);
            }

            if (input.Kinds.HasFlag(ReferenceScanKinds.Pointer))
                ScanPointers(input, results, seen);

            if (input.Kinds.HasFlag(ReferenceScanKinds.StringPattern) &&
                targetAscii != null &&
                targetAsciiBytes != null &&
                targetAsciiCrc32.HasValue)
            {
                ScanStringPattern(input, targetAscii, targetAsciiBytes, results, seen);
                ScanCrc32Pattern(input, targetAscii, targetAsciiCrc32.Value, results, seen);
            }

            return results
                .OrderBy(r => KindSort(r.Kind))
                .ThenBy(r => r.Address)
                .ThenBy(r => r.Summary, StringComparer.Ordinal)
                .ToList();
        }

        public static List<SearchMatch> ToSearchMatches(IEnumerable<ReferenceScanResult> references, uint baseAddress)
        {
            var matches = new List<SearchMatch>();
            foreach (ReferenceScanResult reference in references)
            {
                string field = reference.Kind == "controlFlow" ? "op" : "args";
                int instructionIndex = 0;
                if (TryGetOffset(reference.Address, baseAddress, int.MaxValue, out int offset))
                    instructionIndex = offset / 4;

                matches.Add(new SearchMatch
                {
                    InstructionIndex = instructionIndex,
                    Address = reference.Address,
                    Field = field,
                    Start = 0,
                    Length = 100,
                });
            }

            return matches;
        }

        private static void ScanInstructions(
            ReferenceScanInput input,
            string? targetAscii,
            uint? targetAsciiCrc32,
            List<ReferenceScanResult> results,
            HashSet<string> seen)
        {
            byte[] bytes = input.Bytes;
            int len = bytes.Length;

            var known = new bool[32];
            var values = new uint[32];
            var sources = new List<uint>[32];
            var luiSlots = new int[32];
            Array.Fill(luiSlots, -1);

            known[0] = true;
            values[0] = 0;
            sources[0] = [];

            for (int i = 0; i + 3 < len; i += 4)
            {
                uint instr = BitConverter.ToUInt32(bytes, i);
                uint opcode = (instr >> 26) & 0x3Fu;
                uint rs = (instr >> 21) & 0x1Fu;
                uint rt = (instr >> 16) & 0x1Fu;
                uint rd = (instr >> 11) & 0x1Fu;
                int rsIdx = (int)rs;
                int rtIdx = (int)rt;
                int rdIdx = (int)rd;
                uint pc = input.BaseAddress + (uint)i;

                if (input.Kinds.HasFlag(ReferenceScanKinds.ControlFlow))
                {
                    uint? branchTarget = TryGetStaticBranchTarget(instr, pc);
                    if (branchTarget.HasValue && AddressMatches(branchTarget.Value, input.TargetAddress))
                    {
                        AddInstructionResult(
                            results,
                            seen,
                            address: pc,
                            kind: "controlFlow",
                            kindLabel: "Branch/Jump",
                            targetAddress: input.TargetAddress,
                            instruction: DisassembleAt(instr, pc),
                            summary: $"Targets 0x{branchTarget.Value:X8}",
                            detail: null,
                            matchedValue: $"0x{branchTarget.Value:X8}",
                            sourceAddresses: [pc]);
                    }
                }

                if (opcode == 0x00u)
                {
                    uint funct = instr & 0x3Fu;
                    if ((funct == 0x08u || funct == 0x09u) &&
                        input.Kinds.HasFlag(ReferenceScanKinds.ControlFlow) &&
                        rs < 32 && known[rsIdx] &&
                        AddressMatches(values[rsIdx], input.TargetAddress))
                    {
                        AddInstructionResult(
                            results,
                            seen,
                            address: pc,
                            kind: "controlFlow",
                            kindLabel: "Register Jump",
                            targetAddress: input.TargetAddress,
                            instruction: DisassembleAt(instr, pc),
                            summary: $"Targets 0x{values[rsIdx]:X8} from ${RegName(rs)}",
                            detail: null,
                            matchedValue: $"0x{values[rsIdx]:X8}",
                            sourceAddresses: BuildSources(sources[rsIdx], pc));
                    }

                    if (funct == 0x09u)
                        InvalidateRegister(rdIdx, known, values, sources, luiSlots);
                    else if (funct != 0x08u)
                        InvalidateRegister(rdIdx, known, values, sources, luiSlots);
                    continue;
                }

                if (opcode == 0x0Fu)
                {
                    if (rt != 0)
                    {
                        uint value = (instr & 0xFFFFu) << 16;
                        SetKnownRegister(rtIdx, value, [pc], known, values, sources);
                        luiSlots[rtIdx] = i;
                    }
                    continue;
                }

                if (opcode == 0x09u || opcode == 0x19u || opcode == 0x0Du)
                {
                    if (rt != 0 && rs < 32 && known[rsIdx])
                    {
                        uint combined = opcode == 0x0Du
                            ? values[rsIdx] | (instr & 0xFFFFu)
                            : unchecked(values[rsIdx] + (uint)(short)(instr & 0xFFFFu));

                        List<uint> sourceAddresses = BuildSources(sources[rsIdx], pc);
                        if (input.Kinds.HasFlag(ReferenceScanKinds.ResolvedAddress) &&
                            AddressMatches(combined, input.TargetAddress))
                        {
                            AddInstructionResult(
                                results,
                                seen,
                                address: pc,
                                kind: "resolved",
                                kindLabel: "Resolved",
                                targetAddress: input.TargetAddress,
                                instruction: BuildInstructionTrace(bytes, input.BaseAddress, sourceAddresses),
                                summary: $"Builds 0x{combined:X8} in ${RegName(rt)}",
                                detail: null,
                                matchedValue: $"0x{combined:X8}",
                                sourceAddresses: sourceAddresses);
                        }

                        if (targetAsciiCrc32.HasValue && combined == targetAsciiCrc32.Value)
                        {
                            AddInstructionResult(
                                results,
                                seen,
                                address: pc,
                                kind: "crc32",
                                kindLabel: "CRC32",
                                targetAddress: input.TargetAddress,
                                instruction: BuildInstructionTrace(bytes, input.BaseAddress, sourceAddresses),
                                summary: $"Builds CRC32 0x{combined:X8}",
                                detail: targetAscii == null ? null : $"\"{Truncate(targetAscii)}\"",
                                matchedValue: $"0x{combined:X8}",
                                sourceAddresses: sourceAddresses);
                        }

                        SetKnownRegister(rtIdx, combined, sourceAddresses, known, values, sources);
                        luiSlots[rtIdx] = -1;
                    }
                    else
                    {
                        InvalidateRegister(rtIdx, known, values, sources, luiSlots);
                    }
                    continue;
                }

                if (IsMemoryReferenceOpcode(opcode))
                {
                    if (rs < 32 && known[rsIdx])
                    {
                        uint target = unchecked(values[rsIdx] + (uint)(short)(instr & 0xFFFFu));
                        if (input.Kinds.HasFlag(ReferenceScanKinds.ResolvedAddress) &&
                            AddressMatches(target, input.TargetAddress))
                        {
                            List<uint> sourceAddresses = BuildSources(sources[rsIdx], pc);
                            AddInstructionResult(
                                results,
                                seen,
                                address: pc,
                                kind: "resolved",
                                kindLabel: IsStoreOpcode(opcode) ? "Store" : "Load",
                                targetAddress: input.TargetAddress,
                                instruction: BuildInstructionTrace(bytes, input.BaseAddress, sourceAddresses),
                                summary: $"{(IsStoreOpcode(opcode) ? "Stores to" : "Loads from")} 0x{target:X8}",
                                detail: null,
                                matchedValue: $"0x{target:X8}",
                                sourceAddresses: sourceAddresses);
                        }
                    }

                    if (IsLoadOpcode(opcode))
                        InvalidateRegister(rtIdx, known, values, sources, luiSlots);
                    continue;
                }

                if (opcode == 0x02u || opcode == 0x03u)
                {
                    if (opcode == 0x03u)
                        InvalidateRegister(31, known, values, sources, luiSlots);
                    continue;
                }

                if (opcode == 0x01u)
                {
                    if (rt >= 0x10u && rt <= 0x13u)
                        InvalidateRegister(31, known, values, sources, luiSlots);
                    continue;
                }

                if (IsNonWritingBranchOpcode(opcode, rs))
                    continue;

                InvalidateRegister(rtIdx, known, values, sources, luiSlots);
            }
        }

        private static void ScanPointers(ReferenceScanInput input, List<ReferenceScanResult> results, HashSet<string> seen)
        {
            HashSet<uint> aliases = BuildPointerTargetAliases(input.TargetAddress);
            for (int i = 0; i + 3 < input.Bytes.Length; i += 4)
            {
                uint value = BitConverter.ToUInt32(input.Bytes, i);
                if (!aliases.Contains(value))
                    continue;

                uint address = input.BaseAddress + (uint)i;
                AddInstructionResult(
                    results,
                    seen,
                    address: address,
                    kind: "pointer",
                    kindLabel: "Pointer",
                    targetAddress: input.TargetAddress,
                    instruction: string.Empty,
                    summary: $"Pointer value 0x{value:X8}",
                    detail: null,
                    matchedValue: $"0x{value:X8}",
                    sourceAddresses: [address],
                    matchLength: 4);
            }
        }

        private static void ScanStringPattern(
            ReferenceScanInput input,
            string ascii,
            byte[] asciiBytes,
            List<ReferenceScanResult> results,
            HashSet<string> seen)
        {
            if (asciiBytes.Length == 0)
                return;

            TryGetOffset(input.TargetAddress, input.BaseAddress, input.Bytes.Length, out int targetOffset);
            foreach (int offset in FindPattern(input.Bytes, asciiBytes))
            {
                if (offset == targetOffset)
                    continue;

                uint address = input.BaseAddress + (uint)offset;
                AddInstructionResult(
                    results,
                    seen,
                    address: address,
                    kind: "stringPattern",
                    kindLabel: "ASCII",
                    targetAddress: input.TargetAddress,
                    instruction: string.Empty,
                    summary: $"ASCII pattern \"{Truncate(ascii)}\"",
                    detail: null,
                    matchedValue: ascii,
                    sourceAddresses: [address],
                    matchLength: asciiBytes.Length);
            }
        }

        private static void ScanCrc32Pattern(
            ReferenceScanInput input,
            string ascii,
            uint crc32,
            List<ReferenceScanResult> results,
            HashSet<string> seen)
        {
            byte[] little = BitConverter.GetBytes(crc32);
            byte[] big = little.Reverse().ToArray();

            foreach (int offset in FindPattern(input.Bytes, little))
                AddCrcDataResult(input, ascii, crc32, offset, "little-endian", results, seen);

            if (!little.SequenceEqual(big))
            {
                foreach (int offset in FindPattern(input.Bytes, big))
                    AddCrcDataResult(input, ascii, crc32, offset, "big-endian", results, seen);
            }
        }

        private static void AddCrcDataResult(
            ReferenceScanInput input,
            string ascii,
            uint crc32,
            int offset,
            string byteOrder,
            List<ReferenceScanResult> results,
            HashSet<string> seen)
        {
            uint address = input.BaseAddress + (uint)offset;
            AddInstructionResult(
                results,
                seen,
                address: address,
                kind: "crc32",
                kindLabel: "CRC32",
                targetAddress: input.TargetAddress,
                instruction: string.Empty,
                summary: $"CRC32 0x{crc32:X8} ({byteOrder})",
                detail: $"\"{Truncate(ascii)}\"",
                matchedValue: $"0x{crc32:X8}",
                sourceAddresses: [address],
                matchLength: 4);
        }

        private static uint? TryGetStaticBranchTarget(uint instruction, uint pc)
        {
            uint opcode = (instruction >> 26) & 0x3Fu;

            if (opcode == 0x11u && ((instruction >> 21) & 0x1Fu) == 0x08u)
            {
                int offset = (short)(instruction & 0xFFFFu);
                return (uint)(pc + 4 + (offset << 2));
            }

            return MipsAnalyzer.TryGetBranchJumpTarget(instruction, pc, null);
        }

        private static void AddInstructionResult(
            List<ReferenceScanResult> results,
            HashSet<string> seen,
            uint address,
            string kind,
            string kindLabel,
            uint targetAddress,
            string instruction,
            string summary,
            string? detail,
            string? matchedValue,
            List<uint> sourceAddresses,
            int matchLength = 0)
        {
            string key = $"{kind}:{address:X8}:{matchedValue}:{summary}";
            if (!seen.Add(key))
                return;

            results.Add(new ReferenceScanResult
            {
                Address = address,
                Kind = kind,
                KindLabel = kindLabel,
                TargetAddress = targetAddress,
                Instruction = instruction,
                Summary = summary,
                Detail = detail,
                MatchedValue = matchedValue,
                SourceAddresses = sourceAddresses.Distinct().ToList(),
                MatchLength = matchLength,
            });
        }

        private static List<uint> BuildSources(List<uint>? existing, uint pc)
        {
            var result = existing != null ? new List<uint>(existing) : [];
            if (result.Count == 0 || result[^1] != pc)
                result.Add(pc);
            return result;
        }

        private static void SetKnownRegister(
            int register,
            uint value,
            List<uint> sourceAddresses,
            bool[] known,
            uint[] values,
            List<uint>[] sources)
        {
            if (register <= 0 || register >= 32)
                return;

            known[register] = true;
            values[register] = value;
            sources[register] = sourceAddresses;
        }

        private static void InvalidateRegister(
            int register,
            bool[] known,
            uint[] values,
            List<uint>[] sources,
            int[] luiSlots)
        {
            if (register <= 0 || register >= 32)
                return;

            known[register] = false;
            values[register] = 0;
            sources[register] = [];
            luiSlots[register] = -1;
        }

        private static string BuildInstructionTrace(byte[] bytes, uint baseAddress, IReadOnlyList<uint> sourceAddresses)
        {
            var parts = new List<string>();
            foreach (uint address in sourceAddresses.Distinct())
            {
                if (!TryGetOffset(address, baseAddress, bytes.Length, out int offset) || offset + 3 >= bytes.Length)
                    continue;

                uint word = BitConverter.ToUInt32(bytes, offset);
                parts.Add(DisassembleAt(word, address));
            }

            return string.Join(" -> ", parts);
        }

        private static string DisassembleAt(uint instruction, uint address)
        {
            try { return MipsDisassembler.Disassemble(instruction, address); }
            catch { return $"0x{instruction:X8}"; }
        }

        private static bool IsMemoryReferenceOpcode(uint opcode)
        {
            return (opcode >= 0x20u && opcode <= 0x2Fu) ||
                   opcode == 0x1Eu || opcode == 0x1Fu ||
                   opcode == 0x31u || opcode == 0x32u || opcode == 0x33u ||
                   opcode == 0x35u || opcode == 0x36u || opcode == 0x37u ||
                   opcode == 0x39u || opcode == 0x3Au ||
                   opcode == 0x3Du || opcode == 0x3Eu || opcode == 0x3Fu;
        }

        private static bool IsLoadOpcode(uint opcode)
        {
            return (opcode >= 0x20u && opcode <= 0x27u) ||
                   opcode == 0x1Eu ||
                   opcode == 0x31u || opcode == 0x32u || opcode == 0x33u ||
                   opcode == 0x35u || opcode == 0x36u || opcode == 0x37u;
        }

        private static bool IsStoreOpcode(uint opcode) => IsMemoryReferenceOpcode(opcode) && !IsLoadOpcode(opcode);

        private static bool IsNonWritingBranchOpcode(uint opcode, uint rs)
        {
            return opcode == 0x02u ||
                   opcode == 0x04u || opcode == 0x05u ||
                   opcode == 0x06u || opcode == 0x07u ||
                   opcode == 0x14u || opcode == 0x15u ||
                   opcode == 0x16u || opcode == 0x17u ||
                   opcode == 0x01u ||
                   (opcode == 0x11u && rs == 0x08u) ||
                   opcode == 0x1Cu;
        }

        private static bool AddressMatches(uint candidate, uint target)
        {
            if (candidate == target)
                return true;

            return NormalizeComparableAddress(candidate) == NormalizeComparableAddress(target);
        }

        private static uint NormalizeComparableAddress(uint address)
        {
            if (address >= 0x80000000u && address < 0xC0000000u)
                return address & 0x1FFFFFFFu;
            if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                return address - EeUncachedRamBase;
            return address;
        }

        private static HashSet<uint> BuildPointerTargetAliases(uint target)
        {
            var aliases = new HashSet<uint> { target };
            uint normalized = NormalizeComparableAddress(target);
            aliases.Add(normalized);

            if (normalized < (uint)PS2MemoryBus.MainRamSize)
            {
                aliases.Add(0x80000000u | normalized);
                aliases.Add(0xA0000000u | normalized);
                aliases.Add(EeUncachedRamBase + normalized);
            }

            return aliases;
        }

        private static bool TryGetOffset(uint address, uint baseAddress, int length, out int offset)
        {
            long direct = (long)address - baseAddress;
            if (direct >= 0 && direct < length)
            {
                offset = (int)direct;
                return true;
            }

            uint normalizedAddress = NormalizeComparableAddress(address);
            uint normalizedBase = NormalizeComparableAddress(baseAddress);
            long normalized = (long)normalizedAddress - normalizedBase;
            if (normalized >= 0 && normalized < length)
            {
                offset = (int)normalized;
                return true;
            }

            offset = -1;
            return false;
        }

        private static bool TryReadAsciiString(byte[] bytes, int offset, out string text, out byte[] asciiBytes)
        {
            text = string.Empty;
            asciiBytes = [];

            if (offset < 0 || offset >= bytes.Length || !IsPrintableAscii(bytes[offset]))
                return false;

            int end = offset;
            int maxEnd = Math.Min(bytes.Length, offset + MaxAsciiStringLength);
            while (end < maxEnd && IsPrintableAscii(bytes[end]))
                end++;

            int length = end - offset;
            if (length < MinAsciiStringLength)
                return false;

            asciiBytes = new byte[length];
            Buffer.BlockCopy(bytes, offset, asciiBytes, 0, length);
            text = Encoding.ASCII.GetString(asciiBytes);
            return true;
        }

        private static bool IsPrintableAscii(byte value) => value >= 0x20 && value <= 0x7E;

        private static IEnumerable<int> FindPattern(byte[] bytes, byte[] pattern)
        {
            if (pattern.Length == 0 || pattern.Length > bytes.Length)
                yield break;

            int limit = bytes.Length - pattern.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (bytes[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    yield return i;
            }
        }

        private static uint ComputeCrc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int i = 0; i < 8; i++)
                {
                    bool bit = (crc & 1u) != 0;
                    crc >>= 1;
                    if (bit)
                        crc ^= 0xEDB88320u;
                }
            }

            return ~crc;
        }

        private static string RegName(uint reg) => reg < s_gprNames.Length ? s_gprNames[reg] : $"r{reg}";

        private static string Truncate(string value)
        {
            const int max = 80;
            return value.Length <= max ? value : value[..(max - 3)] + "...";
        }

        private static int KindSort(string kind)
        {
            return kind switch
            {
                "controlFlow" => 0,
                "resolved" => 1,
                "pointer" => 2,
                "stringPattern" => 3,
                "crc32" => 4,
                _ => 99,
            };
        }
    }
}
