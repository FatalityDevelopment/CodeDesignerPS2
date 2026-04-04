using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── GETNAVHISTORY ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle GETNAVHISTORY — returns all recorded navigation entries,
        /// most-recently visited last.
        /// </summary>
        private string HandleGetNavHistory()
        {
            List<NavHistoryEntry> entries;
            lock (_navHistoryLock)
                entries = new List<NavHistoryEntry>(_navHistory);

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success    = true,
                Command    = "GETNAVHISTORY",
                NavHistory = entries,
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
                return CreateErrorResponse("Address is required for RECORDNAV");

            RecordNavEntry(address.Value, source);
            return CreateSuccessResponse($"Navigation to 0x{NormalizePcToKseg0(address.Value):X8} recorded");
        }

        // ── CLEARNAVHISTORY ───────────────────────────────────────────────────

        /// <summary>
        /// Handle CLEARNAVHISTORY — empties the navigation history.
        /// </summary>
        private string HandleClearNavHistory()
        {
            int count;
            lock (_navHistoryLock)
            {
                count = _navHistory.Count;
                _navHistory.Clear();
            }
            return CreateSuccessResponse($"Cleared {count} navigation entry(s)");
        }

        // ── NAVIGATETO ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle NAVIGATETO — records the address in nav history and pushes an
        /// <c>ai_activity</c> notification so the VS Code disasm view (when Follow
        /// AI is enabled) jumps to the address automatically.
        /// </summary>
        private string HandleNavigateTo(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("Address is required for NAVIGATETO");

            uint display = NormalizePcToKseg0(address.Value);
            RecordNavEntry(address.Value, "navigate");
            return CreateSuccessResponse($"Navigate to 0x{display:X8}");
        }
    }
}
