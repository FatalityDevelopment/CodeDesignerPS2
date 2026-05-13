using System;
using System.Collections.Generic;
using System.Linq;

using CodeDesigner.Ps2.Database.Entities;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Bridge command handlers for the user-controlled disassembler data-format
    /// overrides.  Three commands round-trip the overrides between the VS Code
    /// extension and the database:
    ///
    /// <list type="bullet">
    ///   <item><c>GETDATAFORMATS</c> — every override that touches
    ///         <c>[Address, EndAddress)</c> for a given <c>GameId</c>.</item>
    ///   <item><c>SETDATAFORMAT</c> — insert or replace overrides covering a
    ///         span.  Persists immediately.</item>
    ///   <item><c>CLEARDATAFORMAT</c> — delete every override that intersects a
    ///         span, restoring default 4-byte instruction decoding.</item>
    /// </list>
    ///
    /// <para>
    /// Overrides are stored against a <see cref="GameDisc"/>; when <c>GameId</c>
    /// is omitted the rows are stored with <c>GameDiscId = null</c> so file-only
    /// sessions still get persistence.
    /// </para>
    /// </summary>
    internal sealed class DataFormatService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public DataFormatService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGet(c),    "GETDATAFORMATS");
            reg.Add(c => HandleSet(c),    "SETDATAFORMAT");
            reg.Add(c => HandleClear(c),  "CLEARDATAFORMAT");
        }

        // ── GETDATAFORMATS ────────────────────────────────────────────────────

        private string HandleGet(CommandMessage c)
        {
            try
            {
                // No address range supplied → return every override for the disc.
                long start, end;
                if (c.Address is null && c.EndAddress is null)
                {
                    start = 0;
                    end   = 0x1_0000_0000L; // covers all 32-bit addresses
                }
                else
                {
                    start = c.Address ?? 0;
                    end   = c.EndAddress ?? (c.Address ?? 0) + (c.Length ?? 0);
                    if (end <= start) end = start + 1;
                }

                var rows = _ctx.DataFormatRepo
                    .GetInRangeAsync(c.GameId, start, end)
                    .GetAwaiter().GetResult();

                var infos = rows.Select(ToInfo).ToList();
                return Responses.Json(new CommandResponse
                {
                    Success              = true,
                    Command              = "GETDATAFORMATS",
                    DataFormatOverrides  = infos,
                    Message              = $"{infos.Count} override(s)",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"GETDATAFORMATS failed: {ex.Message}");
            }
        }

        // ── SETDATAFORMAT ─────────────────────────────────────────────────────

        private string HandleSet(CommandMessage c)
        {
            try
            {
                if (c.Address is null)
                    return Responses.Error("Address is required for SETDATAFORMAT");
                if (c.Length is null || c.Length <= 0)
                    return Responses.Error("Length is required for SETDATAFORMAT and must be positive");
                if (string.IsNullOrWhiteSpace(c.DataFormatKind))
                    return Responses.Error("DataFormatKind is required for SETDATAFORMAT");

                DataFormatKind kind = ParseKind(c.DataFormatKind);
                int unit = DbDataFormatRepository.ByteSizeOf(kind);
                if (c.Length.Value % unit != 0)
                    return Responses.Error(
                        $"Length ({c.Length}) must be a multiple of {unit} for {kind}");

                var saved = _ctx.DataFormatRepo
                    .UpsertAsync(c.GameId, c.Address.Value, c.Length.Value, kind)
                    .GetAwaiter().GetResult();

                return Responses.Json(new CommandResponse
                {
                    Success              = true,
                    Command              = "SETDATAFORMAT",
                    DataFormatOverrides  = new List<DataFormatOverrideInfo> { ToInfo(saved) },
                    Message              = $"Override set at 0x{(uint)saved.Address:X8} ({saved.Length} bytes as {kind})",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"SETDATAFORMAT failed: {ex.Message}");
            }
        }

        // ── CLEARDATAFORMAT ───────────────────────────────────────────────────

        private string HandleClear(CommandMessage c)
        {
            try
            {
                if (c.Address is null)
                    return Responses.Error("Address is required for CLEARDATAFORMAT");

                int length = c.Length ?? 1;
                if (length <= 0) length = 1;

                int removed = _ctx.DataFormatRepo
                    .ClearRangeAsync(c.GameId, c.Address.Value, length)
                    .GetAwaiter().GetResult();

                return Responses.Success($"Cleared {removed} override(s)");
            }
            catch (Exception ex)
            {
                return Responses.Error($"CLEARDATAFORMAT failed: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a wire kind string ("byte"/"half"/"word"/"dword"/"qword",
        /// case-insensitive) into the corresponding <see cref="DataFormatKind"/>.
        /// Throws an <see cref="ArgumentException"/> on unknown values.
        /// </summary>
        internal static DataFormatKind ParseKind(string s) => s.Trim().ToLowerInvariant() switch
        {
            "byte"  or "b"          => DataFormatKind.Byte,
            "half"  or "h" or "hw"  => DataFormatKind.Half,
            "word"  or "w"          => DataFormatKind.Word,
            "dword" or "d" or "dw"  => DataFormatKind.DWord,
            "qword" or "q" or "qw"  => DataFormatKind.QWord,
            _ => throw new ArgumentException($"Unknown data format kind: '{s}'"),
        };

        internal static string KindToWire(DataFormatKind k) => k switch
        {
            DataFormatKind.Byte  => "byte",
            DataFormatKind.Half  => "half",
            DataFormatKind.Word  => "word",
            DataFormatKind.DWord => "dword",
            DataFormatKind.QWord => "qword",
            _                    => "byte",
        };

        private static DataFormatOverrideInfo ToInfo(DataFormatOverride o) => new()
        {
            Id      = o.Id,
            Address = unchecked((uint)o.Address),
            Length  = o.Length,
            Kind    = KindToWire(o.Kind),
        };
    }
}
