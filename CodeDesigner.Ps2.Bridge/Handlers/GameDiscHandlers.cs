using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
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
            if (Db == null)
                return CreateErrorResponse("Database is not available");
            if (string.IsNullOrWhiteSpace(serial))
                return CreateErrorResponse("GameSerial is required for UPDATEGAMEDISC");

            var normalized = DbLabelRepository.NormalizeSerial(serial);

            var disc = Db.GameDiscs
                         .FirstOrDefault(d =>
                             d.Serial != null &&
                             d.Serial.Replace("_", "-").ToUpper() == normalized);

            if (disc is null)
            {
                // ── Determine region from serial prefix ───────────────────────
                var regionName = InferRegionFromSerial(normalized);
                var region = Db.GameRegions.FirstOrDefault(r => r.Name == regionName);
                if (region is null)
                {
                    region = new GameRegion { Name = regionName };
                    Db.GameRegions.Add(region);
                    Db.SaveChanges();
                }

                // ── Find or create the Game row ───────────────────────────────
                var title = !string.IsNullOrWhiteSpace(gameTitle)
                    ? gameTitle
                    : $"Unknown Game ({normalized})";   // fall back to a descriptive placeholder

                var game = Db.Games.FirstOrDefault(g => g.Title == title);
                if (game is null)
                {
                    game = new Game { Title = title };
                    Db.Games.Add(game);
                    Db.SaveChanges();
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
                Db.GameDiscs.Add(disc);
                Db.SaveChanges();
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
                    Db.SaveChanges();
            }

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
