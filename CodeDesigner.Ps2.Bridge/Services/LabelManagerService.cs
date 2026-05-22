using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MgrEntry = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrSet   = CodeDesigner.Ps2.LabelManager.Models.LabelSet;
using MgrType  = CodeDesigner.Ps2.LabelManager.Models.LabelType;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class LabelManagerService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public LabelManagerService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetLabelSets(c.WorkspaceDir, c.GameId),                                                    "GETLABELSETS");
            reg.Add(c => HandleGetLabelSet(c.WorkspaceDir, c.GameId, c.SetName),                                          "GETLABELSET");
            reg.Add(c => HandleSetActiveLabelSet(c.WorkspaceDir, c.GameId, c.SetName),                                    "SETACTIVELABELSET");
            reg.Add(c => HandleCreateLabelSet(c.WorkspaceDir, c.GameId, c.SetName),                                       "CREATELABELSET");
            reg.Add(c => HandleDeleteLabelSet(c.WorkspaceDir, c.GameId, c.SetName),                                       "DELETELABELSET");
            reg.Add(c => HandleRenameLabelSet(c.WorkspaceDir, c.GameId, c.SetName, c.NewSetName),                         "RENAMELABELSET");
            reg.Add(c => HandleAddLabelEntry(c.WorkspaceDir, c.GameId, c.SetName, c.LabelEntry),                          "ADDLABELENTRY");
            reg.Add(c => HandleAddLabelEntries(c.WorkspaceDir, c.GameId, c.SetName, c.LabelEntries),                      "ADDLABELENTRIES");
            reg.Add(c => HandleDeleteLabelEntry(c.WorkspaceDir, c.GameId, c.SetName, c.LabelEntryAddress, c.Address),     "DELETELABELENTRY");
            reg.Add(c => HandleImportLabelSet(c.WorkspaceDir, c.GameId, c.SetName, c.Data),                               "IMPORTLABELSET");
            reg.Add(c => HandleExportLabelSet(c.WorkspaceDir, c.GameId, c.SetName),                                       "EXPORTLABELSET");
            reg.Add(_ => HandleResolveStrings(),                                                                          "RESOLVESTRINGS");
            reg.Add(c => c.Address is null
                ? Responses.Error("Address is required for RESOLVELINKEDLIST")
                : HandleResolveLinkedList(c.Address.Value, c.MemberOffset ?? 0),
                "RESOLVELINKEDLIST");
            reg.Add(c => HandleIopLabelsRefresh(c.GameId, moduleName: null),                                              "IOPLABELSREFRESH");
            reg.Add(c => HandleIopLabelsRefresh(c.GameId, moduleName: c.SetName),                                         "IOPLABELSREFRESHMODULE");
        }

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
            Address        = e.Address,
            ParentAddress  = e.ParentAddress,
            Offset         = e.Offset,
            Name           = e.Name,
            Comment        = e.Comment,
            ValueType      = e.ValueType,
            Hidden         = e.Hidden,
            Dynamic        = e.Dynamic,
            Type           = e.Type == MgrType.Address ? null : e.Type.ToString(),
            ArgRegs        = e.ArgRegs,
            ReturnRegs     = e.ReturnRegs,
            ElementStride  = e.ElementStride,
            ElementCount   = e.ElementCount,
            NodeNextOffset = e.NodeNextOffset,
            NodeSize       = e.NodeSize,
        };

        /// <summary>Convert a wire entry to the LabelManager model.</summary>
        private static MgrEntry FromWireEntry(LabelManagerEntry w) => new()
        {
            Address        = w.Address,
            ParentAddress  = w.ParentAddress,
            Offset         = w.Offset,
            Name           = w.Name,
            Comment        = w.Comment,
            ValueType      = w.ValueType,
            Hidden         = w.Hidden,
            Dynamic        = w.Dynamic,
            Type           = Enum.TryParse<MgrType>(w.Type, ignoreCase: true, out var t) ? t : MgrType.Address,
            ArgRegs        = w.ArgRegs,
            ReturnRegs     = w.ReturnRegs,
            ElementStride  = w.ElementStride,
            ElementCount   = w.ElementCount,
            NodeNextOffset = w.NodeNextOffset,
            NodeSize       = w.NodeSize,
        };

        /// <summary>Convert a LabelManager set to the wire payload.</summary>
        private static LabelSetPayload ToWireSet(MgrSet s) => new()
        {
            SetName    = s.SetName,
            GameDiscId = s.GameId,
            IsBuiltIn  = s.IsBuiltIn,
            IsSystem   = s.IsSystem,
            Entries    = s.Entries.Select(ToWireEntry).ToList(),
        };

        // ── GETLABELSETS ──────────────────────────────────────────────────────

        /// <summary>
        /// GETLABELSETS — return the list of user-defined label set names for a game,
        /// plus the synthetic "Built-in" set name which is always present.
        /// Requires GameId.
        /// </summary>
        private string HandleGetLabelSets(string? workspaceDir, string? gameId)
        {
            gameId = RequireParam(gameId, "GameId", out var e2);
            if (gameId is null) return Responses.Error(e2!);

            try
            {
                var summaries = _ctx.LabelRepo.GetSetSummariesAsync(gameId).GetAwaiter().GetResult();

                // Always include the built-in set in the list (and as a system set so
                // the webview styles it with the amber treatment).
                var names = new List<string> { "Built-in" };
                var summaryList = new List<LabelSetSummary>
                {
                    new() { SetName = "Built-in", IsBuiltIn = true, IsSystem = true },
                };
                foreach (var s in summaries)
                {
                    names.Add(s.SetName);
                    summaryList.Add(new LabelSetSummary
                    {
                        SetName   = s.SetName,
                        IsBuiltIn = s.IsBuiltIn,
                        IsSystem  = s.IsSystem,
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success           = true,
                    Command           = "GETLABELSETS",
                    LabelSets         = names,
                    LabelSetSummaries = summaryList,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"GETLABELSETS failed: {ex.Message}");
            }
        }

        // ── GETLABELSET ───────────────────────────────────────────────────────

        /// <summary>
        /// GETLABELSET — return the full contents of a specific label set.
        /// Pass SetName "Built-in" to retrieve the embedded built-in set.
        /// </summary>
        private string HandleGetLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            setName = RequireParam(setName, "SetName", out var e1);
            if (setName is null) return Responses.Error(e1!);

            MgrSet? set;
            if (setName.Equals("Built-in", StringComparison.OrdinalIgnoreCase))
            {
                set = _ctx.LabelMgr.BuiltIn;
            }
            else
            {
                gameId = RequireParam(gameId, "GameId", out var e3);
                if (gameId is null) return Responses.Error(e3!);

                try
                {
                    set = _ctx.LabelRepo.LoadSetAsync(gameId, setName).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    return Responses.Error($"GETLABELSET failed: {ex.Message}");
                }

                if (set is null) return Responses.Error($"Label set '{setName}' not found.");
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
        /// SETACTIVELABELSET — load the named label set from the DB and make it active.
        /// All subsequent CDS compilation and label lookups use the merged
        /// (built-in + active set) label list.
        /// </summary>
        private string HandleSetActiveLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);

            // "Built-in" is a synthetic set — just clear the active user set.
            if (setName.Equals("Built-in", StringComparison.OrdinalIgnoreCase))
            {
                _ctx.LabelMgr.SetContext(workspaceDir ?? string.Empty, gameId);
                _ctx.LabelMgr.ClearActiveSet();
                _ctx.SyncLegacyDictsFromLabelManager();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SETACTIVELABELSET",
                    LabelSet = ToWireSet(_ctx.LabelMgr.BuiltIn),
                });
            }

            try
            {
                var set = _ctx.LabelRepo.LoadSetAsync(gameId, setName).GetAwaiter().GetResult();
                if (set is null) return Responses.Error($"Label set '{setName}' not found.");

                // Set the in-memory context so GetMergedLabels() resolves the right game.
                _ctx.LabelMgr.SetContext(workspaceDir ?? string.Empty, gameId);

                if (!string.IsNullOrWhiteSpace(workspaceDir))
                {
                    // Mirror the DB set into the on-disk path so _ctx.LabelMgr's file-based
                    // helpers (used by GetMergedLabels, RESOLVESTRINGS, etc.) find it.
                    _ctx.LabelMgr.SaveSet(set, workspaceDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(workspaceDir, gameId, setName);
                }
                else
                {
                    // No workspace supplied: set context only and mark the set active
                    // by saving to a temp directory so SetActiveSet can locate the file.
                    var tempDir = Path.Combine(Path.GetTempPath(), "codedesigner_labels");
                    Directory.CreateDirectory(tempDir);
                    _ctx.LabelMgr.SaveSet(set, tempDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(tempDir, gameId, setName);
                }

                // Rebuild _ctx.Labels / _ctx.Structs so all existing handlers see the updated data.
                _ctx.SyncLegacyDictsFromLabelManager();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SETACTIVELABELSET",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"SETACTIVELABELSET failed: {ex.Message}");
            }
        }

        // ── CREATELABELSET ────────────────────────────────────────────────────

        private string HandleCreateLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);

            try
            {
                var set = _ctx.LabelRepo.CreateSetAsync(gameId, setName).GetAwaiter().GetResult();

                // Persist the empty set to disk immediately so that the server's
                // in-memory state (_ctx.LabelMgr.WorkspaceDir, ActiveSetName, _ctx.Labels/_ctx.Structs)
                // is consistent from the moment the set is created.  This mirrors the
                // logic in HandleSetActiveLabelSet so that GetMergedLabels() can find
                // the set even before the client explicitly calls SETACTIVELABELSET.
                _ctx.LabelMgr.SetContext(workspaceDir ?? string.Empty, gameId);
                if (!string.IsNullOrWhiteSpace(workspaceDir))
                {
                    _ctx.LabelMgr.SaveSet(set, workspaceDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(workspaceDir, gameId, setName);
                }
                else
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "codedesigner_labels");
                    Directory.CreateDirectory(tempDir);
                    _ctx.LabelMgr.SaveSet(set, tempDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(tempDir, gameId, setName);
                }
                _ctx.SyncLegacyDictsFromLabelManager();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "CREATELABELSET",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error(ex.Message);
            }
        }

        // ── DELETELABELSET ────────────────────────────────────────────────────

        private string HandleDeleteLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);

            try
            {
                _ctx.LabelRepo.DeleteSetAsync(gameId, setName).GetAwaiter().GetResult();
                _ctx.SyncLegacyDictsFromLabelManager();
                return Responses.Success($"Label set '{setName}' deleted.");
            }
            catch (Exception ex)
            {
                return Responses.Error($"DELETELABELSET failed: {ex.Message}");
            }
        }

        // ── RENAMELABELSET ────────────────────────────────────────────────────

        private string HandleRenameLabelSet(string? workspaceDir, string? gameId, string? setName, string? newSetName)
        {
            gameId     = RequireParam(gameId,     "GameId",     out var e2);
            setName    = RequireParam(setName,    "SetName",    out var e3);
            newSetName = RequireParam(newSetName, "NewSetName", out var e4);
            if (gameId is null)     return Responses.Error(e2!);
            if (setName is null)    return Responses.Error(e3!);
            if (newSetName is null) return Responses.Error(e4!);

            try
            {
                var set = _ctx.LabelRepo.RenameSetAsync(gameId, setName, newSetName).GetAwaiter().GetResult();

                // Keep the _ctx.LabelMgr in-memory state in sync with the new name.
                _ctx.LabelMgr.SetContext(workspaceDir ?? string.Empty, gameId);
                if (!string.IsNullOrWhiteSpace(workspaceDir))
                {
                    _ctx.LabelMgr.SaveSet(set, workspaceDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(workspaceDir, gameId, newSetName);
                }
                else
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "codedesigner_labels");
                    Directory.CreateDirectory(tempDir);
                    _ctx.LabelMgr.SaveSet(set, tempDir, gameId);
                    _ctx.LabelMgr.SetActiveSet(tempDir, gameId, newSetName);
                }
                _ctx.SyncLegacyDictsFromLabelManager();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "RENAMELABELSET",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"RENAMELABELSET failed: {ex.Message}");
            }
        }

        // ── ADDLABELENTRY ─────────────────────────────────────────────────────

        private string HandleAddLabelEntry(string? workspaceDir, string? gameId, string? setName, LabelManagerEntry? entry)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);
            if (entry is null)   return Responses.Error("LabelEntry is required for ADDLABELENTRY");

            if (string.IsNullOrWhiteSpace(entry.Address))
                return Responses.Error("LabelEntry.Address is required");
            if (string.IsNullOrWhiteSpace(entry.Name))
                return Responses.Error("LabelEntry.Name is required");

            try
            {
                var set = _ctx.LabelRepo.AddOrUpdateEntryAsync(gameId, setName, FromWireEntry(entry)).GetAwaiter().GetResult();

                // Keep legacy dicts in sync.  If no active set is configured yet, or
                // the active set matches the one being written, auto-configure the
                // _ctx.LabelMgr context so that GetMergedLabels() (and therefore GETLABELS)
                // immediately reflects the change.
                bool isActiveSet = string.Equals(_ctx.LabelMgr.ActiveSetName, setName, StringComparison.OrdinalIgnoreCase);
                bool noActiveSet = string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName);

                if (isActiveSet || noActiveSet)
                {
                    EnsureWorkspaceDirectory(workspaceDir, gameId, setName);
                    PersistActiveSetToDisk(set);
                    // EnsureWorkspaceDirectory calls SetActiveSet before the file exists
                    // (noActiveSet case), so ActiveSetName may still be null.  Retry now
                    // that the file has been written to disk.
                    if (string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName) && !string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir))
                        _ctx.LabelMgr.SetActiveSet(_ctx.LabelMgr.WorkspaceDir, gameId, setName);
                    _ctx.SyncLegacyDictsFromLabelManager();
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "ADDLABELENTRY",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ADDLABELENTRY failed: {ex.Message}");
            }
        }

        // ── ADDLABELENTRIES (batch) ───────────────────────────────────────

        private string HandleAddLabelEntries(string? workspaceDir, string? gameId, string? setName, List<LabelManagerEntry>? entries)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);
            if (entries is null || entries.Count == 0)
                return Responses.Error("LabelEntries is required and must contain at least one entry for ADDLABELENTRIES");

            // Validate all entries have required fields.
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(entries[i].Address))
                    return Responses.Error($"LabelEntries[{i}].Address is required");
                if (string.IsNullOrWhiteSpace(entries[i].Name))
                    return Responses.Error($"LabelEntries[{i}].Name is required");
            }

            try
            {
                var mgrEntries = entries.Select(FromWireEntry).ToList();
                var set = _ctx.LabelRepo.AddOrUpdateEntriesAsync(gameId, setName, mgrEntries).GetAwaiter().GetResult();

                bool isActiveSet = string.Equals(_ctx.LabelMgr.ActiveSetName, setName, StringComparison.OrdinalIgnoreCase);
                bool noActiveSet = string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName);

                if (isActiveSet || noActiveSet)
                {
                    EnsureWorkspaceDirectory(workspaceDir, gameId, setName);
                    PersistActiveSetToDisk(set);
                    if (string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName) && !string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir))
                        _ctx.LabelMgr.SetActiveSet(_ctx.LabelMgr.WorkspaceDir, gameId, setName);
                    _ctx.SyncLegacyDictsFromLabelManager();
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "ADDLABELENTRIES",
                    Message  = $"Added/updated {entries.Count} label entries.",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ADDLABELENTRIES failed: {ex.Message}");
            }
        }

        // ── DELETELABELENTRY ──────────────────────────────────────────────────

        private string HandleDeleteLabelEntry(string? workspaceDir, string? gameId, string? setName, string? addressStr, uint? addressUint)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);

            // Prefer the string address field (supports synthetic child paths like
            // "0x80010000:+0x0010").  Fall back to the legacy numeric field.
            string? addrStr = addressStr?.Trim();
            if (string.IsNullOrEmpty(addrStr))
            {
                if (addressUint is null)
                    return Responses.Error("Address is required for DELETELABELENTRY");
                addrStr = $"0x{addressUint.Value:X8}";
            }

            try
            {
                var set = _ctx.LabelRepo.DeleteEntryAsync(gameId, setName, addrStr).GetAwaiter().GetResult();

                bool isActiveSet = string.Equals(_ctx.LabelMgr.ActiveSetName, setName, StringComparison.OrdinalIgnoreCase);
                bool noActiveSet = string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName);

                if (isActiveSet || noActiveSet)
                {
                    EnsureWorkspaceDirectory(workspaceDir, gameId, setName);
                    PersistActiveSetToDisk(set);
                    // EnsureWorkspaceDirectory calls SetActiveSet before the file exists
                    // (noActiveSet case), so ActiveSetName may still be null.  Retry now
                    // that the file has been written to disk.
                    if (string.IsNullOrEmpty(_ctx.LabelMgr.ActiveSetName) && !string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir))
                        _ctx.LabelMgr.SetActiveSet(_ctx.LabelMgr.WorkspaceDir, gameId, setName);
                    _ctx.SyncLegacyDictsFromLabelManager();
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "DELETELABELENTRY",
                    LabelSet = ToWireSet(set),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error(ex.Message);
            }
        }

        /// <summary>
        /// Ensures <see cref="_ctx.LabelMgr"/> has a valid workspace directory and active-set
        /// name so that <see cref="PersistActiveSetToDisk"/> and
        /// <see cref="SyncLegacyDictsFromLabelManager"/> can function.
        /// Uses <paramref name="workspaceDir"/> when supplied, otherwise falls back to a
        /// temp directory.  A no-op when the workspace is already configured.
        /// </summary>
        private void EnsureWorkspaceDirectory(string? workspaceDir, string gameId, string setName)
        {
            if (!string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir)) return;

            var dir = !string.IsNullOrWhiteSpace(workspaceDir)
                ? workspaceDir
                : Path.Combine(Path.GetTempPath(), "codedesigner_labels");
            Directory.CreateDirectory(dir);
            _ctx.LabelMgr.SetActiveSet(dir, gameId, setName);
        }

        /// <summary>
        /// Writes <paramref name="set"/> to the same on-disk location that
        /// <see cref="CodeDesigner.Ps2.LabelManager.Ps2LabelManager.GetMergedLabels"/> will read from.
        /// This keeps the disk file in sync with the DB after any ADDLABELENTRY /
        /// DELETELABELENTRY operation so that <see cref="SyncLegacyDictsFromLabelManager"/>
        /// sees fresh data.
        /// </summary>
        private void PersistActiveSetToDisk(CodeDesigner.Ps2.LabelManager.Models.LabelSet set)
        {
            // _ctx.LabelMgr.WorkspaceDir reflects the directory that was established by the
            // most recent SETACTIVELABELSET (or CREATELABELSET) call – it may be a temp
            // path when no workspace is open.  We must write to exactly that location.
            var dir    = _ctx.LabelMgr.WorkspaceDir;
            var gameId = set.GameId ?? _ctx.LabelMgr.GameId;

            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(gameId))
            {
                // Cannot persist without a valid directory and game ID.  This is a
                // logic error (SETACTIVELABELSET should have been called first) but
                // it is not fatal – the next SETACTIVELABELSET call will re-sync.
                return;
            }

            _ctx.LabelMgr.SaveSet(set, dir, gameId);
        }

        // ── IMPORTLABELSET ────────────────────────────────────────────────────

        private string HandleImportLabelSet(string? workspaceDir, string? gameId, string? setName, string? data)
        {
            gameId = RequireParam(gameId, "GameId", out var e2);
            if (gameId is null) return Responses.Error(e2!);
            if (string.IsNullOrWhiteSpace(data))
                return Responses.Error("Data (JSON) is required for IMPORTLABELSET");

            try
            {
                // Parse using the LabelManager's existing import logic (handles both legacy and
                // native JSON formats), then persist the result to the database.
                var mgrSet = _ctx.LabelMgr.Import(data, workspaceDir ?? string.Empty, gameId, setName?.Trim());
                var saved  = _ctx.LabelRepo.SaveSetAsync(mgrSet, gameId).GetAwaiter().GetResult();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "IMPORTLABELSET",
                    LabelSet = ToWireSet(saved),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"Import failed: {ex.Message}");
            }
        }

        // ── EXPORTLABELSET ────────────────────────────────────────────────────

        private string HandleExportLabelSet(string? workspaceDir, string? gameId, string? setName)
        {
            gameId  = RequireParam(gameId,  "GameId",  out var e2);
            setName = RequireParam(setName, "SetName", out var e3);
            if (gameId is null)  return Responses.Error(e2!);
            if (setName is null) return Responses.Error(e3!);

            try
            {
                var set = _ctx.LabelRepo.LoadSetAsync(gameId, setName).GetAwaiter().GetResult();
                if (set is null) return Responses.Error($"Label set '{setName}' not found.");

                // Serialise using the same format as LabelManager's Export.
                var json = System.Text.Json.JsonSerializer.Serialize(set, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                });

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "EXPORTLABELSET",
                    Data    = json,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error(ex.Message);
            }
        }

        // ── RESOLVESTRINGS ────────────────────────────────────────────────────

        /// <summary>
        /// RESOLVESTRINGS — reads each String-typed label's address from live PCSX2
        /// memory (PINE mode only), extracts the null-terminated ASCII content, and
        /// updates the runtime label display in <c>_ctx.Labels</c> to include it.
        /// Returns a summary of how many labels were resolved.
        /// </summary>
        private string HandleResolveStrings()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("RESOLVESTRINGS requires PINE (PCSX2) mode.");

            var merged = _ctx.LabelMgr.GetMergedLabels();
            int resolved = 0;
            var skipped  = new List<string>();

            foreach (var entry in merged)
            {
                if (entry.Hidden) continue;
                if (entry.Type != MgrType.String) continue;

                var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? entry.Address[2..] : entry.Address;
                if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    continue;

                try
                {
                    // Read up to 256 bytes to find the null terminator.
                    const int MaxStringLen = 256;
                    byte[] raw = _ctx.PineClient!.ReadBytes(addr, MaxStringLen);

                    int len = 0;
                    while (len < raw.Length && raw[len] != 0) len++;

                    // Only include printable ASCII characters.
                    var sb = new StringBuilder(len);
                    for (int i = 0; i < len; i++)
                    {
                        byte b = raw[i];
                        sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '?');
                    }

                    string content = sb.ToString();
                    _ctx.Labels[addr] = ($"{entry.Name}: \"{content}\"", entry.Comment);
                    resolved++;
                }
                catch
                {
                    skipped.Add(entry.Name);
                }
            }

            var msg = $"Resolved {resolved} string label(s).";
            if (skipped.Count > 0)
                msg += $" Skipped (read error): {string.Join(", ", skipped)}.";

            return Responses.Success(msg);
        }

        // ── IOPLABELSREFRESH / IOPLABELSREFRESHMODULE ────────────────────────

        /// <summary>
        /// Scans loaded IOP modules in PCSX2 and populates one read-only system label
        /// set per module (<c>IOP: &lt;modname&gt;</c>) with the exports it provides plus
        /// every import stub aimed at it. When <paramref name="moduleName"/> is supplied
        /// only that one module is refreshed.
        /// </summary>
        private string HandleIopLabelsRefresh(string? gameId, string? moduleName)
        {
            gameId = RequireParam(gameId, "GameId", out var e);
            if (gameId is null) return Responses.Error(e!);

            if (_ctx.PineClient is null)
                return Responses.Error("IOPLABELSREFRESH requires PCSX2 (PINE) mode.");

            try
            {
                var labelizer = new IopModuleLabelizer(_ctx);
                (int modules, int labels) result;
                if (string.IsNullOrWhiteSpace(moduleName))
                    result = labelizer.RefreshAllAsync(gameId).GetAwaiter().GetResult();
                else
                    result = labelizer.RefreshModuleAsync(gameId, moduleName).GetAwaiter().GetResult();

                _ctx.SyncLegacyDictsFromLabelManager();

                return Responses.Success(
                    $"IOP label refresh: {result.modules} module(s), {result.labels} label(s).");
            }
            catch (Exception ex)
            {
                return Responses.Error($"IOPLABELSREFRESH failed: {ex.Message}");
            }
        }

        // ── RESOLVELINKEDLIST ─────────────────────────────────────────────────

        /// <summary>
        /// RESOLVELINKEDLIST — walks a linked list from <paramref name="startAddress"/>
        /// using <paramref name="nextOffset"/> as the byte offset within each node
        /// where the next-node pointer is stored.  Only works in PINE (PCSX2) mode.
        /// Optionally uses the label name found at <paramref name="startAddress"/> to
        /// assign <c>Name[0]</c>, <c>Name[1]</c>, … entries in <c>_ctx.Labels</c>.
        /// Returns the ordered list of node addresses.
        /// </summary>
        private string HandleResolveLinkedList(uint startAddress, int nextOffset)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("RESOLVELINKEDLIST requires PINE (PCSX2) mode.");

            // Look up the label name at the start address for indexed display.
            string labelBase = _ctx.Labels.TryGetValue(startAddress, out var existing)
                ? existing.Label
                : $"0x{startAddress:X8}";

            // Strip "FNC_" prefix if it crept in from a previous sync.
            if (labelBase.StartsWith("FNC_", StringComparison.Ordinal))
                labelBase = labelBase[4..];

            var nodeAddresses = new List<uint>();
            var seen          = new HashSet<uint>();
            uint current      = startAddress;
            int  index        = 0;
            // Cap at 4096 nodes to prevent infinite traversal of circular lists or corrupted memory.
            const int MaxNodes = 4096;

            while (current != 0 && !seen.Contains(current) && index < MaxNodes)
            {
                seen.Add(current);
                nodeAddresses.Add(current);

                // Update _ctx.Labels for this node address.
                _ctx.Labels[current] = ($"{labelBase}[{index}]", null);
                index++;

                // Read the next pointer (uint32 little-endian) at current + nextOffset.
                uint ptrAddr = current + (uint)nextOffset;
                try
                {
                    byte[] ptrBytes = _ctx.PineClient!.ReadBytes(ptrAddr, 4);
                    current = (uint)(ptrBytes[0] | (ptrBytes[1] << 8) | (ptrBytes[2] << 16) | (ptrBytes[3] << 24));
                }
                catch
                {
                    break;
                }
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success   = true,
                Command   = "RESOLVELINKEDLIST",
                Message   = $"Walked {nodeAddresses.Count} node(s) starting at 0x{startAddress:X8}.",
                Data      = JsonSerializer.Serialize(nodeAddresses.Select(a => $"0x{a:X8}").ToList()),
            });
        }
    }
}
