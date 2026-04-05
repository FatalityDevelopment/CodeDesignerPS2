using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MgrEntry  = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrMember = CodeDesigner.Ps2.LabelManager.Models.LabelMember;
using MgrSet    = CodeDesigner.Ps2.LabelManager.Models.LabelSet;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? RequireParam(string? value, string name, out string? error)
        {
            if (string.IsNullOrWhiteSpace(value)) { error = $"{name} is required"; return null; }
            error = null;
            return value.Trim();
        }

        /// <summary>Convert a LabelManager entry to the wire payload.</summary>
        private static LabelManagerEntry ToWireEntry(MgrEntry e) => new()
        {
            Address = e.Address,
            Name    = e.Name,
            Comment = e.Comment,
            Members = e.Members.Select(m => new LabelMemberPayload { Offset = m.Offset, Name = m.Name }).ToList(),
        };

        /// <summary>Convert a wire entry to the LabelManager model.</summary>
        private static MgrEntry FromWireEntry(LabelManagerEntry w) => new()
        {
            Address = w.Address,
            Name    = w.Name,
            Comment = w.Comment,
            Members = w.Members?.Select(m => new MgrMember { Offset = m.Offset, Name = m.Name }).ToList() ?? [],
        };

        /// <summary>Convert a LabelManager set to the wire payload.</summary>
        private static LabelSetPayload ToWireSet(MgrSet s) => new()
        {
            SetName   = s.SetName,
            GameId    = s.GameId,
            IsBuiltIn = s.IsBuiltIn,
            Entries   = s.Entries.Select(ToWireEntry).ToList(),
        };

        // ── GETLABELSETS ──────────────────────────────────────────────────────

        /// <summary>
        /// GETLABELSETS — return the list of user-defined label set names for a game,
        /// plus the synthetic "Built-in" set name which is always present.
        /// Requires WorkspaceDir and GameId.
        /// </summary>
        private string HandleGetLabelSets(string? workspaceDir, string? gameId)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);

            var userSets = LabelMgr.GetSetNames(workspaceDir, gameId).ToList();

            // Always include the built-in set in the list.
            var all = new List<string> { "Built-in" };
            all.AddRange(userSets);

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success    = true,
                Command    = "GETLABELSETS",
                LabelSets  = all,
            });
        }

        // ── GETLABELSET ───────────────────────────────────────────────────────

        /// <summary>
        /// GETLABELSET — return the full contents of a specific label set.
        /// Pass SetName "Built-in" to retrieve the embedded built-in set.
        /// </summary>
        private string HandleGetLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            setName = RequireParam(setName, "SetName", out var e1);
            if (setName is null) return CreateErrorResponse(e1!);

            MgrSet? set;
            if (setName.Equals("Built-in", StringComparison.OrdinalIgnoreCase))
            {
                set = LabelMgr.BuiltIn;
            }
            else
            {
                workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e2);
                gameId       = RequireParam(gameId,       "GameId",       out var e3);
                if (workspaceDir is null) return CreateErrorResponse(e2!);
                if (gameId is null)       return CreateErrorResponse(e3!);

                set = LabelMgr.LoadSet(workspaceDir, gameId, setName);
                if (set is null) return CreateErrorResponse($"Label set '{setName}' not found.");
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success  = true,
                Command  = "GETLABELSET",
                LabelSet = ToWireSet(set),
            });
        }

        // ── SETACTIVELABELSET ─────────────────────────────────────────────────

        /// <summary>
        /// SETACTIVELABELSET — load the named label set and make it active.
        /// All subsequent CDS compilation and label lookups use the merged
        /// (built-in + active set) label list.
        /// </summary>
        private string HandleSetActiveLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);

            // "Built-in" is a synthetic set — there is nothing on disk, just clear the user set.
            if (setName.Equals("Built-in", StringComparison.OrdinalIgnoreCase))
            {
                LabelMgr.SetContext(workspaceDir, gameId);
                LabelMgr.ClearActiveSet();
                SyncLegacyDictsFromLabelManager();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SETACTIVELABELSET",
                    LabelSet = ToWireSet(LabelMgr.BuiltIn),
                });
            }

            var set = LabelMgr.SetActiveSet(workspaceDir, gameId, setName);
            if (set is null) return CreateErrorResponse($"Label set '{setName}' not found.");

            // Rebuild _labels / _structs so all existing handlers see the updated data.
            SyncLegacyDictsFromLabelManager();

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success  = true,
                Command  = "SETACTIVELABELSET",
                LabelSet = ToWireSet(set),
            });
        }

        // ── CREATELABELSET ────────────────────────────────────────────────────

        private string HandleCreateLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);

            try
            {
                var set = LabelMgr.CreateSet(workspaceDir, gameId, setName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "CREATELABELSET",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex.Message);
            }
        }

        // ── DELETELABELSET ────────────────────────────────────────────────────

        private string HandleDeleteLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);

            LabelMgr.DeleteSet(workspaceDir, gameId, setName);
            SyncLegacyDictsFromLabelManager();

            return CreateSuccessResponse($"Label set '{setName}' deleted.");
        }

        // ── ADDLABELENTRY ─────────────────────────────────────────────────────

        private string HandleAddLabelEntry(string? workspaceDir, string? gameId, string? setName, LabelManagerEntry? entry)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);
            if (entry is null)        return CreateErrorResponse("LabelEntry is required for ADDLABELENTRY");

            if (string.IsNullOrWhiteSpace(entry.Address))
                return CreateErrorResponse("LabelEntry.Address is required");
            if (string.IsNullOrWhiteSpace(entry.Name))
                return CreateErrorResponse("LabelEntry.Name is required");

            var set = LabelMgr.AddOrUpdate(workspaceDir, gameId, setName, FromWireEntry(entry));

            // If this is the active set, keep legacy dicts in sync.
            if (string.Equals(LabelMgr.ActiveSetName, setName, StringComparison.OrdinalIgnoreCase))
                SyncLegacyDictsFromLabelManager();

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success  = true,
                Command  = "ADDLABELENTRY",
                LabelSet = ToWireSet(set),
            });
        }

        // ── DELETELABELENTRY ──────────────────────────────────────────────────

        private string HandleDeleteLabelEntry(string? workspaceDir, string? gameId, string? setName, uint? address)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);
            if (address is null)      return CreateErrorResponse("Address is required for DELETELABELENTRY");

            try
            {
                var addrStr = $"0x{address.Value:X8}";
                var set = LabelMgr.DeleteEntry(workspaceDir, gameId, setName, addrStr);

                if (string.Equals(LabelMgr.ActiveSetName, setName, StringComparison.OrdinalIgnoreCase))
                    SyncLegacyDictsFromLabelManager();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "DELETELABELENTRY",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex.Message);
            }
        }

        // ── IMPORTLABELSET ────────────────────────────────────────────────────

        private string HandleImportLabelSet(string? workspaceDir, string? gameId, string? setName, string? data)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (string.IsNullOrWhiteSpace(data))
                return CreateErrorResponse("Data (JSON) is required for IMPORTLABELSET");

            try
            {
                var set = LabelMgr.Import(data, workspaceDir, gameId, setName?.Trim());
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "IMPORTLABELSET",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Import failed: {ex.Message}");
            }
        }

        // ── EXPORTLABELSET ────────────────────────────────────────────────────

        private string HandleExportLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            workspaceDir = RequireParam(workspaceDir, "WorkspaceDir", out var e1);
            gameId       = RequireParam(gameId,       "GameId",       out var e2);
            setName      = RequireParam(setName,      "SetName",      out var e3);
            if (workspaceDir is null) return CreateErrorResponse(e1!);
            if (gameId is null)       return CreateErrorResponse(e2!);
            if (setName is null)      return CreateErrorResponse(e3!);

            try
            {
                var json = LabelMgr.Export(workspaceDir, gameId, setName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "EXPORTLABELSET",
                    Data    = json,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex.Message);
            }
        }
    }
}
