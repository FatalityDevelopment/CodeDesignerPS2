using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Maximum number of bytes that a single full-range SCANVALUE scan will read.
        /// Prevents accidentally scanning more than 256 MB, which is already far larger
        /// than the 32 MB EE Main RAM limit.
        /// </summary>
        private const long MaxValueScanBytes = 256L * 1024 * 1024;

        /// <summary>
        /// Maximum number of result entries a single SCANVALUE command will return.
        /// Keeps the JSON response size manageable; clients can narrow the range
        /// or refine with a filter scan when this cap is reached.
        /// </summary>
        private const int MaxScanResults = 100_000;

        /// <summary>
        /// Handle SCANVALUE — scans a live memory range for values that match a
        /// comparison operator, or refines a previous result set (filter scan).
        /// </summary>
        /// <param name="startAddress">Inclusive start of the scan range.</param>
        /// <param name="endAddress">Exclusive end of the scan range.</param>
        /// <param name="scanValue">Value to compare against (decimal, 0x-prefixed hex, or bare hex).</param>
        /// <param name="comparison">Operator: "eq","ne","lt","gt","le","ge".</param>
        /// <param name="dataType">Data type: "uint8","int8","uint16","int16","uint32","int32","uint64","int64","float32","float64".</param>
        /// <param name="region">"ee" or "iop".</param>
        /// <param name="previousAddresses">When non-null, only re-check these addresses (filter/refine scan).</param>
        private string HandleScanValue(
            uint startAddress,
            uint endAddress,
            string? scanValue,
            string? comparison,
            string? dataType,
            string? region,
            List<uint>? previousAddresses)
        {
            try
            {
                if (Scheduler == null && PineClient == null)
                    return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");

                if (string.IsNullOrWhiteSpace(scanValue))
                    return CreateErrorResponse("ScanValue is required");

                bool useIop   = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                int  typeSize = GetScanTypeSize(dataType);

                // Validate the scan value upfront so we get a clear error instead of
                // silently matching nothing.
                string? parseError = ValidateScanValue(scanValue, dataType);
                if (parseError != null)
                    return CreateErrorResponse(parseError);

                var  results  = new List<ScanValueResult>();
                bool capped   = false;

                if (previousAddresses != null && previousAddresses.Count > 0)
                {
                    // Filter (refine) scan: re-read each previous address.
                    foreach (uint addr in previousAddresses)
                    {
                        byte[] bytes;
                        try { bytes = ReadLiveBytes(addr, typeSize, useIop); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SCANVALUE] Skipping 0x{addr:X8}: {ex.Message}");
                            continue;
                        }

                        if (ScanValueMatches(bytes, 0, scanValue, comparison, dataType))
                        {
                            results.Add(new ScanValueResult
                            {
                                Address = $"0x{addr:X8}",
                                Value   = FormatScanValue(bytes, 0, dataType),
                            });
                            if (results.Count >= MaxScanResults) { capped = true; break; }
                        }
                    }
                }
                else
                {
                    // Full range scan.
                    if (endAddress <= startAddress)
                        return CreateErrorResponse("End address must be greater than start address");

                    long rangeLen = Math.Min((long)(endAddress - startAddress), MaxValueScanBytes);

                    byte[] ram;
                    try
                    {
                        ram = ReadScanRam(startAddress, (int)rangeLen, useIop);
                    }
                    catch (Exception ex)
                    {
                        return CreateErrorResponse($"Failed to read memory: {ex.Message}");
                    }

                    for (int offset = 0; offset + typeSize <= ram.Length; offset += typeSize)
                    {
                        if (ScanValueMatches(ram, offset, scanValue, comparison, dataType))
                        {
                            uint addr = startAddress + (uint)offset;
                            results.Add(new ScanValueResult
                            {
                                Address = $"0x{addr:X8}",
                                Value   = FormatScanValue(ram, offset, dataType),
                            });
                            if (results.Count >= MaxScanResults) { capped = true; break; }
                        }
                    }
                }

                var response = new CommandResponse
                {
                    Success     = true,
                    Command     = "SCANVALUE",
                    ScanResults = results,
                };
                if (capped)
                    response.Message = $"Results capped at {MaxScanResults}. Narrow the range or use Filter Search.";

                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"SCANVALUE failed: {ex.Message}");
            }
        }

        // ── Bulk RAM reader (optimised for full-range scans) ─────────────────────

        /// <summary>
        /// Reads a contiguous block of RAM for scanning.  For the built-in
        /// emulator this copies directly from the RAM array (fast block-copy);
        /// for PINE mode it delegates to the batched ReadBytes path.
        /// </summary>
        private byte[] ReadScanRam(uint startAddress, int length, bool useIop)
        {
            if (IsPineMode)
            {
                uint pineAddr = TranslatePineAddress(startAddress, useIop);
                return PineClient!.ReadBytes(pineAddr, length);
            }

            // Built-in emulator: use fast Array.Copy for aligned EE main RAM.
            if (!useIop)
            {
                uint physAddr;
                if (startAddress >= 0x80000000u && startAddress < 0xC0000000u)
                    physAddr = startAddress & 0x1FFFFFFFu;
                else if (startAddress >= 0x20000000u && startAddress < 0x22000000u)
                    physAddr = startAddress - 0x20000000u;
                else
                    physAddr = startAddress;

                byte[] ram = Scheduler!.EE.Bus.Ram;
                if (physAddr + (uint)length <= (uint)ram.Length)
                {
                    var buf = new byte[length];
                    Array.Copy(ram, (int)physAddr, buf, 0, length);
                    return buf;
                }
            }

            // Fallback: byte-by-byte read (handles IOP, mixed regions, BIOS, etc.)
            return ReadLiveBytes(startAddress, length, useIop);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        internal static int GetScanTypeSize(string? dataType) =>
            (dataType ?? "uint32").ToUpperInvariant() switch
            {
                "UINT8"   or "INT8"               => 1,
                "UINT16"  or "INT16"              => 2,
                "UINT32"  or "INT32"  or "FLOAT32"
                or "FLOAT" or "4BYTE"             => 4,
                "UINT64"  or "INT64"  or "FLOAT64"
                or "DOUBLE" or "8BYTE"            => 8,
                _                                 => 4,
            };

        /// <summary>
        /// Pre-validates the scan value string before iterating over memory.
        /// Returns null when valid, or an error message when invalid.
        /// </summary>
        internal static string? ValidateScanValue(string? rawValue, string? dataType)
        {
            string dt = (dataType ?? "uint32").ToUpperInvariant();
            string sv = rawValue?.Trim() ?? "";
            if (sv.Length == 0)
                return "ScanValue is required";

            try
            {
                switch (dt)
                {
                    case "UINT8":
                        ParseScanUInt8(sv); break;
                    case "INT8":
                        ParseScanInt64(sv); break;
                    case "UINT16":
                    case "UINT32": case "4BYTE":
                    case "UINT64": case "8BYTE":
                        ParseScanUInt64(sv); break;
                    case "INT16":
                    case "INT32":
                    case "INT64":
                        ParseScanInt64(sv); break;
                    case "FLOAT32": case "FLOAT":
                        float.Parse(sv, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "FLOAT64": case "DOUBLE":
                        double.Parse(sv, System.Globalization.CultureInfo.InvariantCulture); break;
                    default:
                        ParseScanUInt64(sv); break;
                }
                return null;
            }
            catch (FormatException)
            {
                return $"Cannot parse \"{sv}\" as {dt}. Use decimal, 0x-prefixed hex, or plain hex digits.";
            }
            catch (OverflowException)
            {
                return $"Value \"{sv}\" is out of range for {dt}.";
            }
        }

        internal static bool ScanValueMatches(byte[] mem, int offset, string? rawValue, string? comparison, string? dataType)
        {
            string dt  = (dataType   ?? "uint32").ToUpperInvariant();
            string cmp = (comparison ?? "eq").ToUpperInvariant();
            string sv  = rawValue?.Trim() ?? "0";

            try
            {
                switch (dt)
                {
                    case "UINT8":
                    {
                        byte a = mem[offset];
                        byte b = ParseScanUInt8(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "INT8":
                    {
                        sbyte a = (sbyte)mem[offset];
                        sbyte b = (sbyte)ParseScanInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "UINT16":
                    {
                        ushort a = BitConverter.ToUInt16(mem, offset);
                        ushort b = (ushort)ParseScanUInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "INT16":
                    {
                        short a = BitConverter.ToInt16(mem, offset);
                        short b = (short)ParseScanInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "UINT32": case "4BYTE":
                    {
                        uint a = BitConverter.ToUInt32(mem, offset);
                        uint b = (uint)ParseScanUInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "INT32":
                    {
                        int a = BitConverter.ToInt32(mem, offset);
                        int b = (int)ParseScanInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "UINT64": case "8BYTE":
                    {
                        ulong a = BitConverter.ToUInt64(mem, offset);
                        ulong b = ParseScanUInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "INT64":
                    {
                        long a = BitConverter.ToInt64(mem, offset);
                        long b = ParseScanInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                    case "FLOAT32": case "FLOAT":
                    {
                        float a = BitConverter.ToSingle(mem, offset);
                        float b = float.Parse(sv, System.Globalization.CultureInfo.InvariantCulture);
                        return CompScan(a, b, cmp);
                    }
                    case "FLOAT64": case "DOUBLE":
                    {
                        double a = BitConverter.ToDouble(mem, offset);
                        double b = double.Parse(sv, System.Globalization.CultureInfo.InvariantCulture);
                        return CompScan(a, b, cmp);
                    }
                    default:
                    {
                        uint a = BitConverter.ToUInt32(mem, offset);
                        uint b = (uint)ParseScanUInt64(sv);
                        return CompScan(a, b, cmp);
                    }
                }
            }
            catch (FormatException)   { return false; }
            catch (OverflowException) { return false; }
        }

        private static bool CompScan<T>(T a, T b, string cmp) where T : IComparable<T>
        {
            int c = a.CompareTo(b);
            return cmp switch
            {
                "EQ" => c == 0,
                "NE" => c != 0,
                "LT" => c < 0,
                "GT" => c > 0,
                "LE" => c <= 0,
                "GE" => c >= 0,
                _    => c == 0,
            };
        }

        internal static string FormatScanValue(byte[] mem, int offset, string? dataType)
        {
            string dt = (dataType ?? "uint32").ToUpperInvariant();
            try
            {
                return dt switch
                {
                    "UINT8"                     => mem[offset].ToString(),
                    "INT8"                      => ((sbyte)mem[offset]).ToString(),
                    "UINT16"                    => BitConverter.ToUInt16(mem, offset).ToString(),
                    "INT16"                     => BitConverter.ToInt16(mem, offset).ToString(),
                    "UINT32" or "4BYTE"         => BitConverter.ToUInt32(mem, offset).ToString(),
                    "INT32"                     => BitConverter.ToInt32(mem, offset).ToString(),
                    "UINT64" or "8BYTE"         => BitConverter.ToUInt64(mem, offset).ToString(),
                    "INT64"                     => BitConverter.ToInt64(mem, offset).ToString(),
                    "FLOAT32" or "FLOAT"        => BitConverter.ToSingle(mem, offset)
                                                       .ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                    "FLOAT64" or "DOUBLE"       => BitConverter.ToDouble(mem, offset)
                                                       .ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                    _                           => BitConverter.ToUInt32(mem, offset).ToString(),
                };
            }
            catch (ArgumentException) { return "?"; }
        }

        // Parse helpers that accept 0x-prefixed hex, bare hex, and plain decimal.

        /// <summary>
        /// Returns true when the string looks like a bare hex value (contains
        /// at least one hex-letter A–F and only hex digits 0–9, A–F).
        /// </summary>
        internal static bool LooksLikeBareHex(string s)
        {
            if (s.Length == 0) return false;
            bool hasHexLetter = false;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9') continue;
                if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    hasHexLetter = true;
                    continue;
                }
                return false; // non-hex character
            }
            return hasHexLetter;
        }

        internal static byte ParseScanUInt8(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToByte(s[2..], 16);
            if (LooksLikeBareHex(s))
                return Convert.ToByte(s, 16);
            return byte.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static ulong ParseScanUInt64(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt64(s[2..], 16);
            if (LooksLikeBareHex(s))
                return Convert.ToUInt64(s, 16);
            return ulong.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static long ParseScanInt64(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return (long)Convert.ToUInt64(s[2..], 16);
            if (s.StartsWith("-"))
                return long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            if (LooksLikeBareHex(s))
                return (long)Convert.ToUInt64(s, 16);
            return long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
