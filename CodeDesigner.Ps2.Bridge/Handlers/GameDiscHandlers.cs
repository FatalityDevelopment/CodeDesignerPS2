using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── UPDATEGAMEDISC ────────────────────────────────────────────────────

        /// <summary>
        /// UPDATEGAMEDISC — update the IsoPath and/or CRC of a GameDisc row
        /// identified by its serial number.
        ///
        /// Accepts: GameSerial (required), IsoPath (optional), GameCrc (optional).
        /// The serial is normalised before lookup (SCUS_97130 → SCUS-97130).
        /// </summary>
        private string HandleUpdateGameDisc(string? serial, string? isoPath, string? crc)
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
                return CreateErrorResponse($"No GameDisc found for serial '{normalized}'.");

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
    }
}
