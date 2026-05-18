using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class GameDiscService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public GameDiscService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleUpdateGameDisc(c.GameSerial, c.IsoPath, c.GameCrc, c.GameTitle),         "UPDATEGAMEDISC", "UPDATE_GAME_DISC");
        }

        // ── UPDATEGAMEDISC ────────────────────────────────────────────────────

        /// <summary>
        /// UPDATEGAMEDISC — upsert a GameDisc row identified by its serial number,
        /// creating the parent Game and GameRegion rows as needed.
        ///
        /// Accepts: GameSerial (required), IsoPath (optional), GameCrc (optional),
        ///          GameTitle (optional, used as the new Game title when creating).
        /// The serial is normalised before lookup (SCUS_97130 → SCUS-97130).
        /// </summary>
        private string HandleUpdateGameDisc(string? serial, string? isoPath, string? crc, string? gameTitle = null)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(serial))
                return Responses.Error("GameSerial is required for UPDATEGAMEDISC");

            var disc = UpsertGameDisc(_ctx.Db, serial, gameTitle, crc, isoPath);

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success    = true,
                Command    = "UPDATEGAMEDISC",
                Data       = JsonSerializer.Serialize(new
                {
                    GameDiscId = disc.Id,
                    Serial     = disc.Serial,
                    IsoPath    = disc.IsoPath,
                    CRC        = disc.CRC,
                }),
            });
        }

        /// <summary>
        /// Upserts a <see cref="GameDisc"/> identified by its serial number, creating
        /// the parent <see cref="Game"/> and <see cref="GameRegion"/> rows as needed.
        /// The serial is normalised before lookup (SCUS_97130 → SCUS-97130).
        /// <para>
        /// Shared by the UPDATEGAMEDISC command and the Discord presence publisher,
        /// so it operates on a caller-supplied context rather than a fixed one.
        /// </para>
        /// </summary>
        internal static GameDisc UpsertGameDisc(
            CodeDesignerDbContext db,
            string serial,
            string? gameTitle = null,
            string? crc = null,
            string? isoPath = null)
        {
            var normalized = DbLabelRepository.NormalizeSerial(serial);

            var disc = db.GameDiscs
                         .FirstOrDefault(d =>
                             d.Serial != null &&
                             d.Serial.Replace("_", "-").ToUpper() == normalized);

            if (disc is null)
            {
                // ── Determine region from serial prefix ───────────────────────
                var regionName = InferRegionFromSerial(normalized);
                var region = db.GameRegions.FirstOrDefault(r => r.Name == regionName);
                if (region is null)
                {
                    region = new GameRegion { Name = regionName };
                    db.GameRegions.Add(region);
                    db.SaveChanges();
                }

                // ── Find or create the Game row ───────────────────────────────
                var title = !string.IsNullOrWhiteSpace(gameTitle)
                    ? gameTitle
                    : $"Unknown Game ({normalized})";   // fall back to a descriptive placeholder

                var game = db.Games.FirstOrDefault(g => g.Title == title);
                if (game is null)
                {
                    game = new Game { Title = title };
                    db.Games.Add(game);
                    db.SaveChanges();
                }

                // ── Create the GameDisc row ───────────────────────────────────
                disc = new GameDisc
                {
                    GameId       = game.Id,
                    GameRegionId = region.Id,
                    Serial       = normalized,
                    IsoPath      = isoPath,
                    CRC          = crc,
                };
                db.GameDiscs.Add(disc);
                db.SaveChanges();
            }
            else
            {
                bool changed = false;

                if (isoPath is not null && disc.IsoPath != isoPath)
                {
                    disc.IsoPath = isoPath;
                    changed = true;
                }

                if (crc is not null && disc.CRC != crc)
                {
                    disc.CRC = crc;
                    changed = true;
                }

                if (changed)
                    db.SaveChanges();
            }

            return disc;
        }

        /// <summary>
        /// Infers the GameRegion name from a normalised PS2 serial prefix.
        /// </summary>
        private static string InferRegionFromSerial(string normalizedSerial)
        {
            // Serial format: XXXX-NNNNN  (prefix is the 4-letter code)
            var prefix = normalizedSerial.Length >= 4
                ? normalizedSerial[..4]
                : normalizedSerial;

            return prefix switch
            {
                "SCUS" or "SLUS" => "NTSC-U",
                "SCES" or "SLES" => "PAL",
                "SCPS" or "SLPS" or "SCAJ" => "NTSC-J",
                "SCKA" or "SLKA" => "NTSC-K",
                _ => "Unknown",
            };
        }
    }
}
