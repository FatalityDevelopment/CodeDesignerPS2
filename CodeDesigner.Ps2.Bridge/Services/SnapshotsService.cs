using System;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Database;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SnapshotsService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SnapshotsService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetSnapshots(c.IsoName), "GETSNAPSHOTS", "GET_SNAPSHOTS");
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
