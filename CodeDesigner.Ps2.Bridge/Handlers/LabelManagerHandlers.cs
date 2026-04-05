using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MgrEntry  = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrMember = CodeDesigner.Ps2.LabelManager.Models.LabelMember;
using MgrSet    = CodeDesigner.Ps2.LabelManager.Models.LabelSet;
using MgrType   = CodeDesigner.Ps2.LabelManager.Models.LabelType;

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
            Address        = e.Address,
            Name           = e.Name,
            Comment        = e.Comment,
            Hidden         = e.Hidden,
            Type           = e.Type.ToString(),
            Members        = e.Members.Select(m => new LabelMemberPayload { Offset = m.Offset, Name = m.Name }).ToList(),
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
            Address       = w.Address,
            Name          = w.Name,
            Comment       = w.Comment,
            Hidden        = w.Hidden,
            Type          = Enum.TryParse<MgrType>(w.Type, ignoreCase: true, out var t) ? t : MgrType.Address,
            Members       = w.Members?.Select(m => new MgrMember { Offset = m.Offset, Name = m.Name }).ToList() ?? [],
            ArgRegs       = w.ArgRegs,
            ReturnRegs    = w.ReturnRegs,
            ElementStride  = w.ElementStride,
            ElementCount   = w.ElementCount,
            NodeNextOffset = w.NodeNextOffset,
            NodeSize       = w.NodeSize,
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

        // ── RESOLVESTRINGS ────────────────────────────────────────────────────

        /// <summary>
        /// RESOLVESTRINGS — reads each String-typed label's address from live PCSX2
        /// memory (PINE mode only), extracts the null-terminated ASCII content, and
        /// updates the runtime label display in <c>_labels</c> to include it.
        /// Returns a summary of how many labels were resolved.
        /// </summary>
        private string HandleResolveStrings()
        {
            if (!IsPineMode)
                return CreateErrorResponse("RESOLVESTRINGS requires PINE (PCSX2) mode.");

            var merged = LabelMgr.GetMergedLabels();
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
                    byte[] raw = PineClient!.ReadBytes(addr, MaxStringLen);

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
                    _labels[addr] = ($"{entry.Name}: \"{content}\"", entry.Comment);
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

            return CreateSuccessResponse(msg);
        }

        // ── RESOLVELINKEDLIST ─────────────────────────────────────────────────

        /// <summary>
        /// RESOLVELINKEDLIST — walks a linked list from <paramref name="startAddress"/>
        /// using <paramref name="nextOffset"/> as the byte offset within each node
        /// where the next-node pointer is stored.  Only works in PINE (PCSX2) mode.
        /// Optionally uses the label name found at <paramref name="startAddress"/> to
        /// assign <c>Name[0]</c>, <c>Name[1]</c>, … entries in <c>_labels</c>.
        /// Returns the ordered list of node addresses.
        /// </summary>
        private string HandleResolveLinkedList(uint startAddress, int nextOffset)
        {
            if (!IsPineMode)
                return CreateErrorResponse("RESOLVELINKEDLIST requires PINE (PCSX2) mode.");

            // Look up the label name at the start address for indexed display.
            string labelBase = _labels.TryGetValue(startAddress, out var existing)
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

                // Update _labels for this node address.
                _labels[current] = ($"{labelBase}[{index}]", null);
                index++;

                // Read the next pointer (uint32 little-endian) at current + nextOffset.
                uint ptrAddr = current + (uint)nextOffset;
                try
                {
                    byte[] ptrBytes = PineClient!.ReadBytes(ptrAddr, 4);
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
