using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // Pointer-scanner helpers (shared by HandleScanPointers + HandleGetPointers)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maximum byte distance between a LUI instruction and its paired
        /// load/store/addiu in <see cref="BuildCodeReferencedAddresses"/>.
        /// 128 bytes (32 instructions) covers essentially all real MIPS compiler output.
        /// </summary>
        private const int MaxLuiWindow = 128;

        /// <summary>
        /// Minimum physical address a pointer must target to be considered valid.
        /// The first 64 KB of EE physical RAM is the exception-vector / BIOS-stub area;
        /// game data and heap allocations never live that low, so small constants such as
        /// 0x00000001 that accidentally match the address range are rejected.
        /// </summary>
        private const uint MinValidPointerPhysAddr = 0x00010000u;

        /// <summary>
        /// Tests whether <paramref name="val"/> is a plausible EE virtual address:
        /// user-mode or KSEG0/KSEG1 main RAM (physical 0x10000–32 MB), or EE scratchpad
        /// (0x70000000–0x70003FFF).
        /// </summary>
        private static bool IsValidEePointerValue(uint val)
        {
            if (val == 0) return false;

            // Pointers are always 4-byte aligned in MIPS — reject unaligned values.
            if ((val & 3) != 0) return false;

            uint phys = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;

            // Reject the exception-vector / BIOS-stub area (physical < 0x00010000).
            if (phys < MinValidPointerPhysAddr) return false;

            return phys < (uint)PS2MemoryBus.MainRamSize ||
                   (val >= 0x70000000u && val < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize);
        }

        /// <summary>
        /// Normalises <paramref name="vaddr"/> to its EE physical equivalent and adds it
        /// to <paramref name="set"/>.  KSEG0/KSEG1 addresses (0x80000000–0xBFFFFFFF) are
        /// stripped to physical via <c>&amp; 0x1FFFFFFF</c>; scratchpad virtual addresses
        /// (0x70000000–0x70003FFF) are stored as-is; non-RAM addresses are silently ignored.
        /// Zero is never added.
        /// </summary>
        private static void AddEePhysAddr(HashSet<uint> set, uint vaddr)
        {
            if (vaddr == 0) return;
            uint phys = vaddr >= 0x80000000u && vaddr < 0xC0000000u ? vaddr & 0x1FFFFFFFu : vaddr;
            if (phys < (uint)PS2MemoryBus.MainRamSize)
                set.Add(phys);
            else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                set.Add(vaddr); // scratchpad: keep virtual address
        }

        /// <summary>
        /// Scans <paramref name="codeBytes"/> (loaded at virtual base
        /// <paramref name="baseVAddr"/>) for all EE addresses referenced by MIPS code,
        /// and returns them as a set of normalised physical addresses.
        /// </summary>
        private static HashSet<uint> BuildCodeReferencedAddresses(byte[] codeBytes, uint baseVAddr)
        {
            var set     = new HashSet<uint>();
            int len     = codeBytes.Length;

            // Per-register LUI tracking: luiSlot[r] = byte offset of the most recent
            // LUI that wrote $r (-1 means none); luiImm[r] = that LUI's imm16.
            var luiSlot = new int[32];
            var luiImm  = new uint[32];
            Array.Fill(luiSlot, -1);

            for (int i = 0; i + 3 < len; i += 4)
            {
                uint instr  = BitConverter.ToUInt32(codeBytes, i);
                uint opcode = (instr >> 26) & 0x3Fu;
                uint rs     = (instr >> 21) & 0x1Fu;
                uint rt     = (instr >> 16) & 0x1Fu;

                // ── LUI: rt ← imm << 16 ──────────────────────────────────────
                if (opcode == 0x0Fu)
                {
                    if (rt != 0) { luiSlot[rt] = i; luiImm[rt] = instr & 0xFFFFu; }
                    continue;
                }

                // ── JAL: call absolute target; clobbers $ra ───────────────────
                if (opcode == 0x03u)
                {
                    uint pc     = baseVAddr + (uint)i;
                    uint target = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);
                    AddEePhysAddr(set, target);
                    luiSlot[31] = -1; // JAL writes $ra
                    continue;
                }

                // ── ADDIU: rt ← rs + sign_ext(imm) ──────────────────────────
                if (opcode == 0x09u)
                {
                    short addOff = (short)(instr & 0xFFFFu);
                    if (rs == 29u && rt == 29u && addOff < 0)
                    {
                        // ADDIU $sp, $sp, -N  →  function prologue
                        AddEePhysAddr(set, baseVAddr + (uint)i);
                    }
                    else if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        // LUI $rs, high  +  ADDIU $rt, $rs, low  →  address reference
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)addOff));
                    }
                    if (rt != 0) luiSlot[rt] = -1; // ADDIU writes $rt
                    continue;
                }

                // ── Loads (0x20–0x27): rt ← mem[rs + off] ────────────────────
                if (opcode >= 0x20u && opcode <= 0x27u)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short loadOff = (short)(instr & 0xFFFFu);
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)loadOff));
                    }
                    if (rt != 0) luiSlot[rt] = -1; // load writes $rt
                    continue;
                }

                // ── Stores (0x28–0x2F): mem[rs + off] ← rt ───────────────────
                if (opcode >= 0x28u && opcode <= 0x2Fu)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short storeOff = (short)(instr & 0xFFFFu);
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)storeOff));
                    }
                    // Stores do not write to any register — no invalidation.
                    continue;
                }

                // ── Register-write invalidation for everything else ───────────
                // SPECIAL (0x00) R-type instructions write $rd.
                if (opcode == 0x00u)
                {
                    uint rd = (instr >> 11) & 0x1Fu;
                    if (rd != 0) luiSlot[rd] = -1;
                    continue;
                }

                // Branch opcodes (REGIMM, BEQ/BNE/BLEZ/BGTZ, BEQL/BNEL/BLEZL/BGTZL),
                // plain J, and the EE MMI group (0x1C, R-type) do not write $rt.
                // Every other I-type opcode is assumed to write $rt — invalidate.
                if (opcode != 0x01u             // REGIMM (branches)
                 && opcode != 0x02u             // J
                 && opcode != 0x04u && opcode != 0x05u  // BEQ / BNE
                 && opcode != 0x06u && opcode != 0x07u  // BLEZ / BGTZ
                 && opcode != 0x14u && opcode != 0x15u  // BEQL / BNEL
                 && opcode != 0x16u && opcode != 0x17u  // BLEZL / BGTZL
                 && opcode != 0x1Cu)            // MMI (handled as R-type above via rd)
                {
                    if (rt != 0) luiSlot[rt] = -1;
                }
            }

            return set;
        }

        /// <summary>
        /// Handle GETSTRINGS — scans base64-encoded binary data for null-terminated
        /// printable ASCII strings of at least MinStringLength characters.
        /// </summary>
        private string HandleGetStrings(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETSTRINGS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<StringEntry>();
                const int MinLength = 4;

                int i = 0;
                while (i < bytes.Length)
                {
                    if (bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                    {
                        int start = i;
                        while (i < bytes.Length && bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                            i++;

                        // Must be null-terminated and meet minimum length
                        if (i < bytes.Length && bytes[i] == 0x00 && (i - start) >= MinLength)
                        {
                            results.Add(new StringEntry
                            {
                                Address = $"0x{(startAddress + (uint)start):X8}",
                                Value   = Encoding.ASCII.GetString(bytes, start, i - start)
                            });
                        }
                        i++; // step past the null / non-printable byte
                    }
                    else
                    {
                        i++;
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETSTRINGS",
                    Strings = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetStrings error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETFUNCTIONS — scans base64-encoded binary data for MIPS function
        /// entry points.  Detects two patterns:
        ///   1. ADDIU $sp, $sp, -N  (stack-frame prologue, opcode 9, rs=rt=29, imm less than 0)
        ///   2. Targets of JAL instructions within the same binary block.
        /// Auto-labels each candidate "FNC_0xADDRESS", but respects any user-defined
        /// label already in the database.
        /// </summary>
        private string HandleGetFunctions(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETFUNCTIONS");

            try
            {
                var bytes     = Convert.FromBase64String(base64Data);
                var candidates = new HashSet<uint>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    // Read little-endian 32-bit instruction word
                    uint instr  = BitConverter.ToUInt32(bytes, i);
                    uint opcode = (instr >> 26) & 0x3Fu;

                    if (opcode == 0x09u) // ADDIU
                    {
                        uint  rs  = (instr >> 21) & 0x1Fu;
                        uint  rt  = (instr >> 16) & 0x1Fu;
                        short imm = (short)(instr & 0xFFFFu);

                        // ADDIU $sp, $sp, -N  →  function prologue
                        if (rs == 29u && rt == 29u && imm < 0)
                            candidates.Add(startAddress + (uint)i);
                    }
                    else if (opcode == 0x03u) // JAL
                    {
                        uint pc         = startAddress + (uint)i;
                        uint target     = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);

                        // Only include targets that fall inside the scanned block
                        if (target >= startAddress && target < startAddress + (uint)bytes.Length)
                            candidates.Add(target);
                    }
                }

                var functions = candidates
                    .OrderBy(addr => addr)
                    .Select(addr => new FunctionEntry
                    {
                        Address = $"0x{addr:X8}",
                        Label   = _labels.TryGetValue(addr, out var entry)
                                      ? entry.Label
                                      : $"FNC_0x{addr:X8}"
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "GETFUNCTIONS",
                    Functions = functions
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetFunctions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETPOINTERS — scans base64-encoded binary data for 4-byte aligned
        /// values that are confirmed EE memory pointers.
        /// </summary>
        private string HandleGetPointers(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETPOINTERS");

            try
            {
                var bytes = Convert.FromBase64String(base64Data);

                // Build the set of all EE addresses referenced by MIPS code.
                HashSet<uint> codeRefs;
                if (Scheduler != null)
                {
                    codeRefs = BuildCodeReferencedAddresses(Scheduler.EE.Bus.Ram, 0x00000000u);
                }
                else
                {
                    int prologueCount = 0;
                    for (int k = 0; k + 3 < bytes.Length; k += 4)
                    {
                        uint w = BitConverter.ToUInt32(bytes, k);
                        // ADDIU $sp, $sp, -N  (opcode=9, rs=rt=29, imm<0)
                        if ((w >> 26) == 0x09u && ((w >> 21) & 0x1Fu) == 29u &&
                            ((w >> 16) & 0x1Fu) == 29u && (short)(w & 0xFFFF) < 0)
                            prologueCount++;
                    }
                    codeRefs = prologueCount >= 10
                        ? BuildCodeReferencedAddresses(bytes, startAddress)
                        : new HashSet<uint>();
                }

                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);

                    // Filter 1: plausible EE address range.
                    if (!IsValidEePointerValue(val)) continue;

                    // Filter 2: must be referenced by MIPS code.
                    // Skip this filter when no code was found (pure-data binary or early boot).
                    uint physVal = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;
                    if (codeRefs.Count > 0 && !codeRefs.Contains(physVal)) continue;

                    results.Add(new PointerEntry
                    {
                        Address  = $"0x{(startAddress + (uint)i):X8}",
                        PointsTo = $"0x{val:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETPOINTERS",
                    Pointers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetPointers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETFLOATS — scans base64-encoded binary data for 4-byte aligned
        /// plausible IEEE-754 single-precision floats.
        /// </summary>
        private string HandleGetFloats(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETFLOATS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<FloatEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint bits = BitConverter.ToUInt32(bytes, i);
                    float f   = BitConverter.Int32BitsToSingle((int)bits);

                    if (float.IsNaN(f) || float.IsInfinity(f))
                        continue;

                    uint exp = (bits >> 23) & 0xFFu;
                    if (exp == 0u)
                        continue;

                    float absVal = MathF.Abs(f);
                    if (absVal < FloatScanMinAbs || absVal > FloatScanMaxAbs)
                        continue;

                    results.Add(new FloatEntry
                    {
                        Address = $"0x{(startAddress + (uint)i):X8}",
                        Value   = f,
                        Raw     = $"0x{bits:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETFLOATS",
                    Floats  = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetFloats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETINTEGERS — scans base64-encoded binary data for non-zero
        /// 4-byte aligned 32-bit signed integers.
        /// </summary>
        private string HandleGetIntegers(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETINTEGERS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<IntegerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint raw = BitConverter.ToUInt32(bytes, i);
                    if (raw == 0x00000000u || raw == 0xFFFFFFFFu)
                        continue;

                    int val = (int)raw;
                    results.Add(new IntegerEntry
                    {
                        Address = $"0x{(startAddress + (uint)i):X8}",
                        Value   = val,
                        Hex     = $"0x{raw:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETINTEGERS",
                    Integers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetIntegers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANSTRINGS — reads <paramref name="length"/> bytes of live emulator
        /// memory starting at <paramref name="address"/> and scans for null-terminated
        /// printable ASCII strings of at least 4 characters.
        /// </summary>
        private string HandleScanStrings(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANSTRINGS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<StringEntry>();
                const int MinLength = 4;

                int i = 0;
                while (i < bytes.Length)
                {
                    if (bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                    {
                        int start = i;
                        while (i < bytes.Length && bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                            i++;
                        if (i < bytes.Length && bytes[i] == 0x00 && (i - start) >= MinLength)
                        {
                            results.Add(new StringEntry
                            {
                                Address = $"0x{(address + (uint)start):X8}",
                                Value   = Encoding.ASCII.GetString(bytes, start, i - start)
                            });
                        }
                        i++;
                    }
                    else
                    {
                        i++;
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SCANSTRINGS",
                    Strings = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanStrings error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANFUNCTIONS — reads <paramref name="length"/> bytes of live emulator
        /// memory and detects MIPS function entry points via ADDIU $sp,$sp,-N prologues
        /// and JAL targets.
        /// </summary>
        private string HandleScanFunctions(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANFUNCTIONS");

            try
            {
                bool useIop  = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes      = ReadLiveBytes(address, length, useIop);
                var candidates = new HashSet<uint>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint instr  = BitConverter.ToUInt32(bytes, i);
                    uint opcode = (instr >> 26) & 0x3Fu;

                    if (opcode == 0x09u)
                    {
                        uint  rs  = (instr >> 21) & 0x1Fu;
                        uint  rt  = (instr >> 16) & 0x1Fu;
                        short imm = (short)(instr & 0xFFFFu);
                        if (rs == 29u && rt == 29u && imm < 0)
                            candidates.Add(address + (uint)i);
                    }
                    else if (opcode == 0x03u)
                    {
                        uint pc     = address + (uint)i;
                        uint target = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);
                        if (target >= address && target < address + (uint)bytes.Length)
                            candidates.Add(target);
                    }
                }

                var functions = candidates
                    .OrderBy(addr => addr)
                    .Select(addr => new FunctionEntry
                    {
                        Address = $"0x{addr:X8}",
                        Label   = _labels.TryGetValue(addr, out var entry)
                                      ? entry.Label
                                      : $"FNC_0x{addr:X8}"
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "SCANFUNCTIONS",
                    Functions = functions
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanFunctions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANPOINTERS — reads <paramref name="length"/> bytes of live emulator
        /// memory starting at <paramref name="address"/> and returns 4-byte aligned values
        /// that are confirmed EE memory pointers.
        /// </summary>
        private string HandleScanPointers(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANPOINTERS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);

                // Build the set of all EE addresses referenced by MIPS code.
                // We always use EE RAM as the code source regardless of the scan
                // region so that the filter works when scanning BIOS, scratchpad, IOP, etc.
                // In PINE mode we don't have direct access to the full EE RAM array;
                // skip the code-reference filter to avoid a huge IPC read.
                HashSet<uint> codeRefs = Scheduler != null
                    ? BuildCodeReferencedAddresses(Scheduler.EE.Bus.Ram, 0x00000000u)
                    : new HashSet<uint>();

                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);

                    // Filter 1: plausible EE address range.
                    if (!IsValidEePointerValue(val)) continue;

                    // Filter 2: must be referenced by MIPS code in EE RAM.
                    // Skip this filter when no code has been found yet (early boot).
                    uint physVal = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;
                    if (codeRefs.Count > 0 && !codeRefs.Contains(physVal)) continue;

                    results.Add(new PointerEntry
                    {
                        Address  = $"0x{(address + (uint)i):X8}",
                        PointsTo = $"0x{val:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SCANPOINTERS",
                    Pointers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanPointers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANFLOATS — reads <paramref name="length"/> bytes of live emulator
        /// memory and returns 4-byte aligned IEEE-754 single-precision floats whose
        /// absolute value falls in the range [0.001, 1 000 000].
        /// </summary>
        private string HandleScanFloats(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANFLOATS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<FloatEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint bits = BitConverter.ToUInt32(bytes, i);
                    float f   = BitConverter.Int32BitsToSingle((int)bits);

                    if (float.IsNaN(f) || float.IsInfinity(f))
                        continue;

                    // Skip subnormals (exponent bits == 0) — PS2 hardware flushes these to 0.
                    uint exp = (bits >> 23) & 0xFFu;
                    if (exp == 0u)
                        continue;

                    float absVal = MathF.Abs(f);
                    if (absVal < FloatScanMinAbs || absVal > FloatScanMaxAbs)
                        continue;

                    results.Add(new FloatEntry
                    {
                        Address = $"0x{(address + (uint)i):X8}",
                        Value   = f,
                        Raw     = $"0x{bits:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SCANFLOATS",
                    Floats  = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanFloats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANINTEGERS — reads <paramref name="length"/> bytes of live emulator
        /// memory and returns every non-zero 4-byte aligned 32-bit signed integer.
        /// Values of 0x00000000 and 0xFFFFFFFF (common fill/sentinel patterns) are excluded.
        /// </summary>
        private string HandleScanIntegers(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANINTEGERS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<IntegerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint raw = BitConverter.ToUInt32(bytes, i);
                    // Skip common fill/sentinel values.
                    if (raw == 0x00000000u || raw == 0xFFFFFFFFu)
                        continue;

                    int val = (int)raw;
                    results.Add(new IntegerEntry
                    {
                        Address = $"0x{(address + (uint)i):X8}",
                        Value   = val,
                        Hex     = $"0x{raw:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SCANINTEGERS",
                    Integers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanIntegers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SEARCH — searches bytes for a query string using the given mode.
        /// Bytes are taken from base64-encoded <c>Data</c> when present, or read
        /// directly from live emulator memory using <c>Address</c>, <c>Length</c>
        /// and <c>Region</c>.
        /// Modes: "text" (case-insensitive substring), "bytes" (hex pattern),
        ///        "string" (raw byte match), "regex" (case-insensitive regex).
        /// </summary>
        private string HandleSearch(string query, string mode, uint startAddress, int length,
                                    string? region, string? base64Data)
        {
            if (string.IsNullOrEmpty(query))
                return CreateErrorResponse("Query is required for SEARCH");

            try
            {
                byte[] bytes;
                if (!string.IsNullOrEmpty(base64Data))
                {
                    bytes = Convert.FromBase64String(base64Data);
                }
                else
                {
                    if (Scheduler == null && PineClient == null)
                        return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
                    if (length <= 0)
                        return CreateErrorResponse("Length is required when searching live memory");
                    bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                    bytes = ReadLiveBytes(startAddress, length, useIop);
                }

                var matches = new List<SearchMatch>();

                // ── Instruction-reference search ──────────────────────────────────────
                if (mode == "refs")
                {
                    string hexStr = query.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? query[2..] : query;
                    if (!uint.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber,
                                       System.Globalization.CultureInfo.InvariantCulture, out uint targetAddr))
                        return CreateErrorResponse("Invalid address for instruction-reference search");

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success  = true,
                        Command  = "SEARCH",
                        Matches  = HandleSearchRefs(targetAddr, bytes, startAddress),
                    });
                }

                if (mode == "bytes")
                {
                    var pattern = ParseBytePattern(query);
                    if (pattern == null)
                        return CreateErrorResponse("Invalid byte pattern");

                    for (int i = 0; i <= bytes.Length - pattern.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < pattern.Length; j++)
                            if (bytes[i + j] != pattern[j]) { found = false; break; }
                        if (found)
                        {
                            matches.Add(new SearchMatch
                            {
                                InstructionIndex = i / 4,
                                Address          = startAddress + (uint)i,
                                Field            = "bytes",
                                Start            = 0,
                                Length           = pattern.Length * 3 - 1
                            });
                            i += pattern.Length - 1;
                        }
                    }
                }
                else if (mode == "string")
                {
                    var strBytes = Encoding.UTF8.GetBytes(query);
                    for (int i = 0; i <= bytes.Length - strBytes.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < strBytes.Length; j++)
                            if (bytes[i + j] != strBytes[j]) { found = false; break; }
                        if (found)
                        {
                            matches.Add(new SearchMatch
                            {
                                InstructionIndex = i / 4,
                                Address          = startAddress + (uint)i,
                                Field            = "bytes",
                                Start            = 0,
                                Length           = strBytes.Length * 3 - 1
                            });
                            i += strBytes.Length - 1;
                        }
                    }
                }
                else
                {
                    // text or regex: disassemble each instruction and search its fields
                    bool useRegex = mode == "regex";
                    Regex? rx = null;
                    if (useRegex)
                    {
                        try { rx = new Regex(query, RegexOptions.IgnoreCase); }
                        catch { return CreateErrorResponse("Invalid regular expression"); }
                    }
                    string queryLower = query.ToLowerInvariant();

                    for (int i = 0; i + 3 < bytes.Length; i += 4)
                    {
                        uint   word = BitConverter.ToUInt32(bytes, i);
                        uint   addr = startAddress + (uint)i;
                        int    idx  = i / 4;

                        string addrStr  = $"{addr:X8}";
                        string bytesStr = $"{bytes[i+3]:X2}{bytes[i+2]:X2}{bytes[i+1]:X2}{bytes[i]:X2}";
                        string disasm   = MipsDisassembler.Disassemble(word, addr);
                        int    sp       = disasm.IndexOf(' ');
                        string op       = sp < 0 ? disasm : disasm[..sp];
                        string args     = sp < 0 ? "" : disasm[(sp + 1)..].Trim();
                        bool   hasLabel = _labels.TryGetValue(addr, out var le);
                        string label    = hasLabel ? le!.Label         : "";
                        string comment  = hasLabel ? (le!.Comment ?? "") : "";

                        (string name, string text)[] fields =
                        {
                            ("addr",    addrStr),
                            ("bytes",   bytesStr),
                            ("label",   label),
                            ("op",      op),
                            ("args",    args),
                            ("comment", comment),
                        };

                        foreach (var (fieldName, fieldText) in fields)
                        {
                            if (useRegex && rx != null)
                            {
                                foreach (Match m in rx.Matches(fieldText))
                                    matches.Add(new SearchMatch
                                    {
                                        InstructionIndex = idx, Address = addr,
                                        Field = fieldName, Start = m.Index, Length = m.Length
                                    });
                            }
                            else
                            {
                                string fl = fieldText.ToLowerInvariant();
                                int    fi = 0;
                                while ((fi = fl.IndexOf(queryLower, fi, StringComparison.Ordinal)) >= 0)
                                {
                                    matches.Add(new SearchMatch
                                    {
                                        InstructionIndex = idx, Address = addr,
                                        Field = fieldName, Start = fi, Length = query.Length
                                    });
                                    fi += query.Length;
                                }
                            }
                        }
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SEARCH",
                    Matches = matches
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Search error: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans <paramref name="bytes"/> (loaded at virtual base <paramref name="baseAddress"/>)
        /// for all MIPS instructions that directly or indirectly reference
        /// <paramref name="targetAddress"/>, and returns them as a list of search matches.
        /// <para>
        /// Covers: J/JAL absolute jumps; I-type branches (BEQ, BNE, BLEZ, BGTZ,
        /// BEQL, BNEL, BLEZL, BGTZL); REGIMM branches (BLTZ, BGEZ and their
        /// -L/-AL/-ALL variants); COP1 branches (BC1F, BC1T, BC1FL, BC1TL);
        /// and LUI+ADDIU/ORI/load/store pointer-load pairs.
        /// </para>
        /// </summary>
        private static List<SearchMatch> HandleSearchRefs(uint targetAddress, byte[] bytes, uint baseAddress)
        {
            var matches = new List<SearchMatch>();
            int len = bytes.Length;

            // Per-register LUI tracking (same approach as BuildCodeReferencedAddresses).
            var luiSlot = new int[32];    // byte offset of most recent LUI per register (-1 = none)
            var luiImm  = new uint[32];   // immediate of that LUI instruction
            Array.Fill(luiSlot, -1);

            for (int i = 0; i + 3 < len; i += 4)
            {
                uint instr  = BitConverter.ToUInt32(bytes, i);
                uint opcode = (instr >> 26) & 0x3Fu;
                uint rs     = (instr >> 21) & 0x1Fu;
                uint rt     = (instr >> 16) & 0x1Fu;
                uint pc     = baseAddress + (uint)i;

                // ── J / JAL (opcode 2/3): absolute jump ──────────────────────────────
                if (opcode == 0x02u || opcode == 0x03u)
                {
                    uint target = ((pc + 4) & 0xF0000000u) | ((instr & 0x03FFFFFFu) << 2);
                    if (target == targetAddress)
                        matches.Add(new SearchMatch { InstructionIndex = i / 4, Address = pc, Field = "op", Start = 0, Length = 3 });
                    if (opcode == 0x03u) luiSlot[31] = -1; // JAL writes $ra
                    continue;
                }

                // ── I-type branches: BEQ/BNE/BLEZ/BGTZ (4-7) + BEQL/BNEL/BLEZL/BGTZL (0x14-0x17) ──
                if ((opcode >= 0x04u && opcode <= 0x07u) || (opcode >= 0x14u && opcode <= 0x17u))
                {
                    short offset = (short)(instr & 0xFFFFu);
                    uint target = (uint)(pc + 4 + ((int)offset << 2));
                    if (target == targetAddress)
                        matches.Add(new SearchMatch { InstructionIndex = i / 4, Address = pc, Field = "op", Start = 0, Length = 3 });
                    continue;
                }

                // ── REGIMM (opcode 0x01): BLTZ/BGEZ/BLTZL/BGEZL/BLTZAL/BGEZAL/BLTZALL/BGEZALL ──
                if (opcode == 0x01u)
                {
                    bool isBranch = rt == 0x00u || rt == 0x01u || rt == 0x02u || rt == 0x03u
                                 || rt == 0x10u || rt == 0x11u || rt == 0x12u || rt == 0x13u;
                    if (isBranch)
                    {
                        short offset = (short)(instr & 0xFFFFu);
                        uint target = (uint)(pc + 4 + ((int)offset << 2));
                        if (target == targetAddress)
                            matches.Add(new SearchMatch { InstructionIndex = i / 4, Address = pc, Field = "op", Start = 0, Length = 4 });
                    }
                    // BLTZAL/BGEZAL/BLTZALL/BGEZALL write $ra (reg 31)
                    if (rt == 0x10u || rt == 0x11u || rt == 0x12u || rt == 0x13u)
                        luiSlot[31] = -1;
                    continue;
                }

                // ── COP1 branches (opcode 0x11, rs=8): BC1F/BC1T/BC1FL/BC1TL ────────
                if (opcode == 0x11u && rs == 0x08u)
                {
                    short offset = (short)(instr & 0xFFFFu);
                    uint target = (uint)(pc + 4 + ((int)offset << 2));
                    if (target == targetAddress)
                        matches.Add(new SearchMatch { InstructionIndex = i / 4, Address = pc, Field = "op", Start = 0, Length = 4 });
                    continue;
                }

                // ── LUI (opcode 0x0F): track per register ────────────────────────────
                if (opcode == 0x0Fu)
                {
                    if (rt != 0) { luiSlot[rt] = i; luiImm[rt] = instr & 0xFFFFu; }
                    continue;
                }

                // ── ADDIU (opcode 0x09): detect LUI+ADDIU pair ───────────────────────
                if (opcode == 0x09u)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short addOff  = (short)(instr & 0xFFFFu);
                        uint combined = unchecked((luiImm[rs] << 16) + (uint)addOff);
                        if (combined == targetAddress)
                        {
                            int luiIdx = luiSlot[rs];
                            matches.Add(new SearchMatch { InstructionIndex = luiIdx / 4, Address = baseAddress + (uint)luiIdx, Field = "args", Start = 0, Length = 100 });
                            matches.Add(new SearchMatch { InstructionIndex = i / 4,      Address = pc,                         Field = "args", Start = 0, Length = 100 });
                        }
                    }
                    if (rt != 0) luiSlot[rt] = -1;
                    continue;
                }

                // ── ORI (opcode 0x0D): detect LUI+ORI pair ───────────────────────────
                if (opcode == 0x0Du)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        uint oriOff   = instr & 0xFFFFu;
                        uint combined = (luiImm[rs] << 16) | oriOff;
                        if (combined == targetAddress)
                        {
                            int luiIdx = luiSlot[rs];
                            matches.Add(new SearchMatch { InstructionIndex = luiIdx / 4, Address = baseAddress + (uint)luiIdx, Field = "args", Start = 0, Length = 100 });
                            matches.Add(new SearchMatch { InstructionIndex = i / 4,      Address = pc,                         Field = "args", Start = 0, Length = 100 });
                        }
                    }
                    if (rt != 0) luiSlot[rt] = -1;
                    continue;
                }

                // ── Loads (0x20–0x27): detect LUI+load pair ──────────────────────────
                if (opcode >= 0x20u && opcode <= 0x27u)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short loadOff = (short)(instr & 0xFFFFu);
                        uint combined = unchecked((luiImm[rs] << 16) + (uint)loadOff);
                        if (combined == targetAddress)
                        {
                            int luiIdx = luiSlot[rs];
                            matches.Add(new SearchMatch { InstructionIndex = luiIdx / 4, Address = baseAddress + (uint)luiIdx, Field = "args", Start = 0, Length = 100 });
                            matches.Add(new SearchMatch { InstructionIndex = i / 4,      Address = pc,                         Field = "args", Start = 0, Length = 100 });
                        }
                    }
                    if (rt != 0) luiSlot[rt] = -1;
                    continue;
                }

                // ── Stores (0x28–0x2F): detect LUI+store pair ────────────────────────
                if (opcode >= 0x28u && opcode <= 0x2Fu)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short storeOff = (short)(instr & 0xFFFFu);
                        uint combined  = unchecked((luiImm[rs] << 16) + (uint)storeOff);
                        if (combined == targetAddress)
                        {
                            int luiIdx = luiSlot[rs];
                            matches.Add(new SearchMatch { InstructionIndex = luiIdx / 4, Address = baseAddress + (uint)luiIdx, Field = "args", Start = 0, Length = 100 });
                            matches.Add(new SearchMatch { InstructionIndex = i / 4,      Address = pc,                         Field = "args", Start = 0, Length = 100 });
                        }
                    }
                    // Stores do not write to any register — no LUI slot invalidation.
                    continue;
                }

                // ── Register-write invalidation for everything else ───────────────────
                // SPECIAL (0x00) R-type instructions write $rd.
                if (opcode == 0x00u)
                {
                    uint rd = (instr >> 11) & 0x1Fu;
                    if (rd != 0) luiSlot[rd] = -1;
                    continue;
                }

                // All other I-type opcodes write $rt — invalidate its LUI slot.
                // Branch/jump opcodes that do not write $rt are excluded.
                if (opcode != 0x02u             // J
                 && opcode != 0x04u && opcode != 0x05u  // BEQ / BNE
                 && opcode != 0x06u && opcode != 0x07u  // BLEZ / BGTZ
                 && opcode != 0x14u && opcode != 0x15u  // BEQL / BNEL
                 && opcode != 0x16u && opcode != 0x17u  // BLEZL / BGTZL
                 && opcode != 0x1Cu)            // MMI (R-type, $rd handled above)
                {
                    if (rt != 0) luiSlot[rt] = -1;
                }
            }

            return matches;
        }

        /// <summary>Parse a whitespace/comma-separated hex byte pattern string.</summary>
        private static byte[]? ParseBytePattern(string pattern)
        {
            var cleaned = pattern
                .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                .Replace(",", " ").Trim();
            if (string.IsNullOrEmpty(cleaned)) return null;
            var parts  = cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<byte>();
            foreach (var p in parts)
            {
                if (!Regex.IsMatch(p, @"^[0-9a-fA-F]{1,2}$")) return null;
                result.Add(Convert.ToByte(p, 16));
            }
            return result.Count > 0 ? result.ToArray() : null;
        }
    }
}
