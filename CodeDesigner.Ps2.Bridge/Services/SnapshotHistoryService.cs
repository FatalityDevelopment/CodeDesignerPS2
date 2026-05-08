using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SnapshotHistoryService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SnapshotHistoryService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetAddressHistory(c.Address, c.SnapshotDir), "GETADDRESSHISTORY", "GET_ADDRESS_HISTORY");
        }

        // ── GETADDRESSHISTORY ─────────────────────────────────────────────────

        // Internal POCO for deserialising snapshot-info.json written by the VS Code
        // extension (and also written by HandleSnapshot after this feature is added).
        private sealed class SnapshotInfoFile
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("timestamp")]
            public string Timestamp { get; set; } = string.Empty;

            [JsonPropertyName("regions")]
            public List<SnapshotRegionInfo> Regions { get; set; } = new();
        }

        private sealed class SnapshotRegionInfo
        {
            [JsonPropertyName("Label")]
            public string Label { get; set; } = string.Empty;

            [JsonPropertyName("File")]
            public string File  { get; set; } = string.Empty;

            [JsonPropertyName("BaseAddress")]
            public uint BaseAddress { get; set; }

            [JsonPropertyName("Size")]
            public int Size { get; set; }
        }

        /// <summary>
        /// Handle GETADDRESSHISTORY — scans a root directory recursively for
        /// <c>snapshot-info.json</c> files (written by both the VS Code extension
        /// and the server-side <see cref="HandleSnapshot"/> method).  For each
        /// snapshot that covers the requested address, reads the 4-byte
        /// little-endian value and appends it to the result list.
        /// <para>
        /// This gives the AI the same per-address value history that VS Code's
        /// disasm editor History tab shows to the user.
        /// </para>
        /// </summary>
        /// <param name="address">Virtual EE address to look up.</param>
        /// <param name="snapshotDir">
        /// Root directory to scan (e.g.
        /// <c>&lt;workspace&gt;/snapshots/&lt;gameName&gt;/</c>).
        /// All subdirectories are searched recursively.
        /// </param>
        private string HandleGetAddressHistory(uint? address, string? snapshotDir)
        {
            if (address == null)
                return Responses.Error("Address is required for GETADDRESSHISTORY");
            if (string.IsNullOrWhiteSpace(snapshotDir))
                return Responses.Error("SnapshotDir is required for GETADDRESSHISTORY");
            if (!Directory.Exists(snapshotDir))
                return Responses.Error($"Snapshot directory not found: {snapshotDir}");

            // Normalise to physical so we can compare against region base addresses.
            uint physAddr = NormalizeToSnapshotPhysical(address.Value);

            string[] infoPaths;
            try
            {
                infoPaths = Directory.GetFiles(snapshotDir, "snapshot-info.json",
                                              SearchOption.AllDirectories)
                                     .OrderBy(p => p)
                                     .ToArray();
            }
            catch (Exception ex)
            {
                return Responses.Error($"Failed to scan snapshot directory: {ex.Message}");
            }

            var results = new List<AddressHistoryEntry>();
            var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var infoPath in infoPaths)
            {
                try
                {
                    var info = JsonSerializer.Deserialize<SnapshotInfoFile>(
                        File.ReadAllText(infoPath), jsonOpts);
                    if (info == null) continue;

                    string snapDir = Path.GetDirectoryName(infoPath)!;

                    foreach (var region in info.Regions)
                    {
                        uint regBase = region.BaseAddress;
                        long offset  = (long)physAddr - (long)regBase;
                        if (offset < 0 || offset + 4 > region.Size) continue;

                        string binPath = Path.Combine(snapDir, region.File);
                        if (!File.Exists(binPath)) continue;

                        using var fs  = new FileStream(binPath, FileMode.Open,
                                                       FileAccess.Read, FileShare.Read);
                        fs.Seek(offset, SeekOrigin.Begin);
                        var buf = new byte[4];
                        if (fs.Read(buf, 0, 4) < 4) continue;

                        uint val = BitConverter.ToUInt32(buf, 0); // little-endian
                        results.Add(new AddressHistoryEntry
                        {
                            SnapshotName = info.Name,
                            Timestamp    = info.Timestamp,
                            Value        = val.ToString("X8"),
                        });
                        break; // found the covering region; move to the next snapshot
                    }
                }
                catch { /* skip unreadable snapshots */ }
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success        = true,
                Command        = "GETADDRESSHISTORY",
                AddressHistory = results,
                Message        = $"{results.Count} snapshot(s) contain address 0x{physAddr:X8}",
            });
        }

        /// <summary>
        /// Normalise a virtual EE address to a physical address that matches the
        /// base addresses used by snapshot regions:
        /// <list type="bullet">
        ///   <item>KSEG0/KSEG1 (0x80000000–0xBFFFFFFF) → strip top 3 bits</item>
        ///   <item>Uncached EE mirror (0x20000000–0x21FFFFFF) → subtract 0x20000000</item>
        ///   <item>Uncached+accel EE (0x30000000–0x31FFFFFF) → subtract 0x30000000</item>
        ///   <item>Already physical → unchanged</item>
        /// </list>
        /// </summary>
        private static uint NormalizeToSnapshotPhysical(uint addr)
        {
            if (addr >= 0x80000000u && addr < 0xC0000000u)
                return addr & 0x1FFFFFFFu;
            if (addr >= 0x20000000u && addr <= 0x21FFFFFFu)
                return addr - 0x20000000u;
            if (addr >= 0x30000000u && addr <= 0x31FFFFFFu)
                return addr - 0x30000000u;
            return addr;
        }
    }
}
