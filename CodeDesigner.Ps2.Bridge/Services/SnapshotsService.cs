using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SnapshotsService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SnapshotsService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetSnapshots(c.IsoName), "GETSNAPSHOTS", "GET_SNAPSHOTS");
            reg.Add(c => HandleRegisterSnapshot(c.OutputPath, c.Regions, c.IsoName, c.Title, c.Description), "REGISTERSNAPSHOT");
        }

        /// <summary>
        /// Handle REGISTERSNAPSHOT — records a snapshot the client wrote (a folder of .bin
        /// memory dumps taken from Ps2Emulator): writes its <c>snapshot-info.json</c> and adds it
        /// to the database, so GETSNAPSHOTS and GETADDRESSHISTORY see it.
        /// </summary>
        private string HandleRegisterSnapshot(string? outputPath, List<SnapshotRegion>? regions, string? isoName, string? title, string? description)
        {
            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
                return Responses.Error("OutputPath (an existing snapshot folder) is required for REGISTERSNAPSHOT");
            string name = title ?? Path.GetFileName(outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            try
            {
                var info = new
                {
                    name,
                    timestamp   = DateTime.UtcNow.ToString("o"),
                    isoName     = isoName ?? string.Empty,
                    description = description ?? string.Empty,
                    regions     = (regions ?? new List<SnapshotRegion>()).Select(r => new { r.Label, r.File, r.BaseAddress, r.Size }).ToList(),
                };
                File.WriteAllText(Path.Combine(outputPath, "snapshot-info.json"),
                    JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                new DbSnapshotRepository(_ctx.Db).AddSnapshotAsync(outputPath, name, description, isoName).GetAwaiter().GetResult();
                return Responses.Success($"Snapshot '{name}' registered.");
            }
            catch (Exception ex)
            {
                return Responses.Error($"REGISTERSNAPSHOT failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETSNAPSHOTS — returns snapshot metadata records from the database for
        /// the game identified by <paramref name="isoName"/>.  The binary snapshot files
        /// themselves remain on disk; callers use the returned <c>SnapshotPath</c> values
        /// to open or load them.
        /// </summary>
        private string HandleGetSnapshots(string? isoName)
        {
            if (string.IsNullOrWhiteSpace(isoName))
                return Responses.Error("IsoName is required for GETSNAPSHOTS");

            try
            {
                var repo      = new DbSnapshotRepository(_ctx.Db);
                var snapshots = repo.GetSnapshotsForGameAsync(isoName.Trim()).GetAwaiter().GetResult();

                var payload = snapshots.Select(s => new SnapshotInfo
                {
                    Id               = s.Id,
                    Title            = s.Title,
                    Description      = s.Description,
                    SnapshotPath     = s.SnapshotPath,
                    SnapshotDateTime = s.SnapshotDateTime.ToString("o"),
                }).ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "GETSNAPSHOTS",
                    Snapshots = payload,
                    Message   = $"{payload.Count} snapshot(s) found for '{isoName}'.",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"GETSNAPSHOTS failed: {ex.Message}");
            }
        }
    }
}
