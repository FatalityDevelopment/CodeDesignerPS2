using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class NavHistoryService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public NavHistoryService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGetNavHistory(),                                  "GETNAVHISTORY");
            reg.Add(c => HandleRecordNav(c.Address, c.NavSource ?? "user"),      "RECORDNAV");
            reg.Add(_ => HandleClearNavHistory(),                                "CLEARNAVHISTORY");
            reg.Add(c => HandleNavigateTo(c.Address, c.NavSource ?? "navigate"), "NAVIGATETO");
        }

        // ── GETNAVHISTORY ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle GETNAVHISTORY — returns all recorded navigation entries,
        /// most-recently visited last.
        /// </summary>
        private string HandleGetNavHistory()
        {
            var entries = _ctx.GetNavHistorySnapshot();
            return Responses.Json(new CommandResponse
            {
                Success    = true,
                Command    = "GETNAVHISTORY",
                NavHistory = new List<NavHistoryEntry>(entries),
                Message    = $"{entries.Count} navigation entry(s)",
            });
        }

        // ── RECORDNAV ─────────────────────────────────────────────────────────

        /// <summary>
        /// Handle RECORDNAV — explicitly records a navigation entry.
        /// Called by the VS Code extension whenever the user navigates in the
        /// disasm editor, so the history reflects both user and AI activity.
        /// </summary>
        private string HandleRecordNav(uint? address, string source)
        {
            if (address == null)
                return Responses.Error("Address is required for RECORDNAV");

            _ctx.RecordNavEntry(address.Value, source);
            return Responses.Success($"Navigation to 0x{AddressTranslator.NormalizePcToKseg0(address.Value):X8} recorded");
        }

        // ── CLEARNAVHISTORY ───────────────────────────────────────────────────

        /// <summary>
        /// Handle CLEARNAVHISTORY — empties the navigation history.
        /// </summary>
        private string HandleClearNavHistory()
        {
            int count = _ctx.GetNavHistorySnapshot().Count;
            _ctx.ClearNavHistory();
            return Responses.Success($"Cleared {count} navigation entry(s)");
        }

        // ── NAVIGATETO ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle NAVIGATETO — records the address in nav history and pushes an
        /// <c>ai_activity</c> notification so the VS Code disasm view (when Follow
        /// AI is enabled) jumps to the address automatically.
        /// </summary>
        private string HandleNavigateTo(uint? address, string source)
        {
            if (address == null)
                return Responses.Error("Address is required for NAVIGATETO");

            uint display = AddressTranslator.NormalizePcToKseg0(address.Value);
            _ctx.RecordNavEntry(address.Value, source);
            return Responses.Success($"Navigate to 0x{display:X8}");
        }
    }
}
