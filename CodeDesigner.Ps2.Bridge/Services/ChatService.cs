using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Backs the AI control panel's Chat tab. Owns MCPCHAT_* commands —
    /// session CRUD, message CRUD, and the orchestrator-stub "SEND" path that
    /// the webview calls when the user submits a prompt.
    ///
    /// The orchestrator behind <c>MCPCHAT_SEND</c> is a placeholder: it stores
    /// the user message and emits one stub agent reply so the renderer has
    /// something to draw. Replace the body of <see cref="HandleSend"/> with a
    /// real LLM call once the streaming pipeline is wired up.
    /// </summary>
    internal sealed class ChatService : ICommandService
    {
        private readonly IBridgeContext _ctx;

        public ChatService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            // Sessions
            reg.Add(_ => HandleSessionList(),                       "MCPCHAT_SESSION_LIST");
            reg.Add(c => HandleSessionGet(c.McpChatSessionId),      "MCPCHAT_SESSION_GET");
            reg.Add(c => HandleSessionCreate(c),                    "MCPCHAT_SESSION_CREATE");
            reg.Add(c => HandleSessionRename(c),                    "MCPCHAT_SESSION_RENAME");
            reg.Add(c => HandleSessionDelete(c.McpChatSessionId),   "MCPCHAT_SESSION_DELETE");

            // Messages
            reg.Add(c => HandleMessageList(c.McpChatSessionId),     "MCPCHAT_MESSAGE_LIST");
            reg.Add(c => HandleMessageAppend(c),                    "MCPCHAT_MESSAGE_APPEND");
            reg.Add(c => HandleMessageUpdate(c),                    "MCPCHAT_MESSAGE_UPDATE");
            reg.Add(c => HandleMessageDelete(c.McpChatMessageId),   "MCPCHAT_MESSAGE_DELETE");
            reg.Add(c => HandleMessagesClear(c.McpChatSessionId),   "MCPCHAT_MESSAGE_CLEAR");

            // Composer
            reg.Add(c => HandleSend(c),                             "MCPCHAT_SEND");
        }

        // ── Sessions ─────────────────────────────────────────────────────────

        private string HandleSessionList()
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            try
            {
                var sessions = _ctx.ChatRepo.ListSessionsAsync().GetAwaiter().GetResult();
                var orchById = (_ctx.McpOrchestrationRepo is not null)
                    ? _ctx.McpOrchestrationRepo.ListAsync().GetAwaiter().GetResult()
                        .ToDictionary(o => o.Id, o => o.Name)
                    : new Dictionary<int, string>();

                var counts = sessions.Select(s => new { s.Id, Count = MessageCountSync(s.Id) })
                                     .ToDictionary(x => x.Id, x => x.Count);

                var dtos = sessions.ConvertAll(s => ToSessionDto(s, orchById, counts));
                return Responses.Json(new CommandResponse
                {
                    Success         = true,
                    Command         = "MCPCHAT_SESSION_LIST",
                    McpChatSessions = dtos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SESSION_LIST error: {ex.Message}"); }
        }

        private string HandleSessionGet(int? id)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (id is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_SESSION_GET");
            try
            {
                var session = _ctx.ChatRepo.GetSessionWithMessagesAsync(id.Value).GetAwaiter().GetResult();
                if (session is null) return Responses.Error($"Chat session {id} not found.");

                var orchName = (session.McpOrchestrationId is int oid && _ctx.McpOrchestrationRepo is not null)
                    ? _ctx.McpOrchestrationRepo.GetAsync(oid).GetAwaiter().GetResult()?.Name
                    : null;

                return Responses.Json(new CommandResponse
                {
                    Success         = true,
                    Command         = "MCPCHAT_SESSION_GET",
                    McpChatSession  = ToSessionDto(session, orchName, session.Messages.Count),
                    McpChatMessages = session.Messages
                                             .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                                             .Select(ToMessageDto).ToList(),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SESSION_GET error: {ex.Message}"); }
        }

        private string HandleSessionCreate(CommandMessage cmd)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            try
            {
                var title = string.IsNullOrWhiteSpace(cmd.McpChatTitle) ? "New chat" : cmd.McpChatTitle!;
                var session = _ctx.ChatRepo.CreateSessionAsync(
                    title, cmd.McpChatOrchestrationId, cmd.McpChatGameSerial, cmd.McpChatGameTag
                ).GetAwaiter().GetResult();

                var orchName = (session.McpOrchestrationId is int oid && _ctx.McpOrchestrationRepo is not null)
                    ? _ctx.McpOrchestrationRepo.GetAsync(oid).GetAwaiter().GetResult()?.Name
                    : null;

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "MCPCHAT_SESSION_CREATE",
                    Message        = $"Created chat '{session.Title}'.",
                    McpChatSession = ToSessionDto(session, orchName, 0),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SESSION_CREATE error: {ex.Message}"); }
        }

        private string HandleSessionRename(CommandMessage cmd)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (cmd.McpChatSessionId is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_SESSION_RENAME");
            if (string.IsNullOrWhiteSpace(cmd.McpChatTitle)) return Responses.Error("McpChatTitle is required for MCPCHAT_SESSION_RENAME");
            try
            {
                var session = _ctx.ChatRepo.RenameSessionAsync(cmd.McpChatSessionId.Value, cmd.McpChatTitle!)
                                            .GetAwaiter().GetResult();
                if (session is null) return Responses.Error($"Chat session {cmd.McpChatSessionId} not found.");

                var orchName = (session.McpOrchestrationId is int oid && _ctx.McpOrchestrationRepo is not null)
                    ? _ctx.McpOrchestrationRepo.GetAsync(oid).GetAwaiter().GetResult()?.Name
                    : null;

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "MCPCHAT_SESSION_RENAME",
                    Message        = $"Renamed to '{session.Title}'.",
                    McpChatSession = ToSessionDto(session, orchName, MessageCountSync(session.Id)),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SESSION_RENAME error: {ex.Message}"); }
        }

        private string HandleSessionDelete(int? id)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (id is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_SESSION_DELETE");
            try
            {
                _ctx.ChatRepo.DeleteSessionAsync(id.Value).GetAwaiter().GetResult();
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "MCPCHAT_SESSION_DELETE",
                    Message = $"Chat session {id} deleted.",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SESSION_DELETE error: {ex.Message}"); }
        }

        // ── Messages ─────────────────────────────────────────────────────────

        private string HandleMessageList(int? sessionId)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (sessionId is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_MESSAGE_LIST");
            try
            {
                var rows = _ctx.ChatRepo.ListMessagesAsync(sessionId.Value).GetAwaiter().GetResult();
                return Responses.Json(new CommandResponse
                {
                    Success         = true,
                    Command         = "MCPCHAT_MESSAGE_LIST",
                    McpChatMessages = rows.ConvertAll(ToMessageDto),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_MESSAGE_LIST error: {ex.Message}"); }
        }

        private string HandleMessageAppend(CommandMessage cmd)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (cmd.McpChatSessionId is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_MESSAGE_APPEND");
            try
            {
                var msg = _ctx.ChatRepo.AppendMessageAsync(
                    cmd.McpChatSessionId.Value,
                    cmd.McpChatKind         ?? "user",
                    cmd.McpChatAgentId,
                    cmd.McpChatAgentDisplay,
                    cmd.McpChatRole,
                    cmd.McpChatBlocksJson   ?? "[]",
                    cmd.McpChatTokens,
                    cmd.McpChatLatencyMs,
                    cmd.McpChatStatus       ?? "done"
                ).GetAwaiter().GetResult();

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "MCPCHAT_MESSAGE_APPEND",
                    McpChatMessage = ToMessageDto(msg),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_MESSAGE_APPEND error: {ex.Message}"); }
        }

        private string HandleMessageUpdate(CommandMessage cmd)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (cmd.McpChatMessageId is null) return Responses.Error("McpChatMessageId is required for MCPCHAT_MESSAGE_UPDATE");
            try
            {
                var msg = _ctx.ChatRepo.UpdateMessageAsync(
                    cmd.McpChatMessageId.Value,
                    cmd.McpChatBlocksJson,
                    cmd.McpChatTokens,
                    cmd.McpChatLatencyMs,
                    cmd.McpChatStatus
                ).GetAwaiter().GetResult();

                if (msg is null) return Responses.Error($"Chat message {cmd.McpChatMessageId} not found.");

                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "MCPCHAT_MESSAGE_UPDATE",
                    McpChatMessage = ToMessageDto(msg),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_MESSAGE_UPDATE error: {ex.Message}"); }
        }

        private string HandleMessageDelete(int? id)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (id is null) return Responses.Error("McpChatMessageId is required for MCPCHAT_MESSAGE_DELETE");
            try
            {
                _ctx.ChatRepo.DeleteMessageAsync(id.Value).GetAwaiter().GetResult();
                return Responses.Success($"Chat message {id} deleted.");
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_MESSAGE_DELETE error: {ex.Message}"); }
        }

        private string HandleMessagesClear(int? sessionId)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (sessionId is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_MESSAGE_CLEAR");
            try
            {
                _ctx.ChatRepo.ClearMessagesAsync(sessionId.Value).GetAwaiter().GetResult();
                return Responses.Success($"Cleared messages in chat session {sessionId}.");
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_MESSAGE_CLEAR error: {ex.Message}"); }
        }

        // ── MCPCHAT_SEND (stub orchestrator) ─────────────────────────────────

        /// <summary>
        /// Persists the user prompt and emits a scripted placeholder squad run
        /// so the chat renderer exercises every template in docs/ai-chat-templates.md.
        ///
        /// Replace the placeholder reply with a real LLM streaming pipeline
        /// once the orchestrator is plumbed in. The wire format already
        /// supports streaming via MCPCHAT_MESSAGE_UPDATE.
        /// </summary>
        private string HandleSend(CommandMessage cmd)
        {
            if (_ctx.ChatRepo is null) return Responses.Error("Chat repository is not available");
            if (cmd.McpChatSessionId is null) return Responses.Error("McpChatSessionId is required for MCPCHAT_SEND");
            if (string.IsNullOrWhiteSpace(cmd.McpChatUserText)) return Responses.Error("McpChatUserText is required for MCPCHAT_SEND");

            try
            {
                var sessionId = cmd.McpChatSessionId.Value;

                // 1. Persist the user message.
                var userBlocks = JsonSerializer.Serialize(new[]
                {
                    new { kind = "text", md = cmd.McpChatUserText! }
                });
                var userMsg = _ctx.ChatRepo.AppendMessageAsync(
                    sessionId,
                    "user", null, null, null,
                    userBlocks, null, null, "done"
                ).GetAwaiter().GetResult();

                // 2. Emit a placeholder multi-agent transcript matching the
                // mockups. These rows intentionally use fake addresses and
                // placeholder tool results; the point is to persist/render the
                // final block contract until live orchestration is wired in.
                var messages = new List<McpChatMessage> { userMsg };
                foreach (var spec in BuildPlaceholderRun())
                {
                    var blocksJson = JsonSerializer.Serialize(spec.Blocks);
                    var row = _ctx.ChatRepo.AppendMessageAsync(
                        sessionId,
                        "agent",
                        null,
                        spec.AgentDisplay,
                        spec.Role,
                        blocksJson,
                        spec.Tokens,
                        spec.LatencyMs,
                        spec.Status
                    ).GetAwaiter().GetResult();
                    messages.Add(row);
                }

                return Responses.Json(new CommandResponse
                {
                    Success         = true,
                    Command         = "MCPCHAT_SEND",
                    McpChatMessages = messages.Select(ToMessageDto).ToList(),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SEND error: {ex.Message}"); }
        }


        private sealed record PlaceholderMessage(
            string AgentDisplay,
            string Role,
            int Tokens,
            int LatencyMs,
            string Status,
            object[] Blocks);

        private static List<PlaceholderMessage> BuildPlaceholderRun() => new()
        {
            new PlaceholderMessage(
                "[Fatality] Raiden",
                "ORCHESTRATOR",
                320,
                800,
                "done",
                new object[]
                {
                    new { kind = "text", md = "Got it. Decomposing into placeholder subtasks and dispatching to the team." },
                    new
                    {
                        kind = "dispatch",
                        title = "Dispatch · 5 placeholder subagent calls",
                        rows = new object[]
                        {
                            new { n = 1, agent = new { id = "sub-zero", display = "Sub-Zero" }, task = "Scan EE RAM for HP candidates", status = "done" },
                            new { n = 2, agent = new { id = "scorpion", display = "Scorpion" }, task = "Trace writers to confirmed address", status = "done" },
                            new { n = 3, agent = new { id = "kitana", display = "Kitana" }, task = "Map player struct around HP offset", status = "done" },
                            new { n = 4, agent = new { id = "kano", display = "Kano" }, task = "Author CDS patch with HP < 50 trap", status = "done" },
                            new { n = 5, agent = new { id = "sonya", display = "Sonya" }, task = "Verify trap fires under controlled step", status = "running" },
                        }
                    }
                }),

            new PlaceholderMessage(
                "[Fatality] Sub-Zero",
                "MEMORY SCANNER",
                180,
                2100,
                "done",
                new object[]
                {
                    new { kind = "text", md = "Placeholder i32 scan narrowed changed health candidates to **1**." },
                    new
                    {
                        kind = "tool-call",
                        tool = "mem.scan",
                        args = new { type = "i32", range = "0x00350000-0x00380000", value = "changed" },
                        status = "done",
                        summary = "1 result · 3 passes",
                        durationMs = 1400,
                        body = new
                        {
                            kind = "table",
                            columns = new object[]
                            {
                                new { key = "n", label = "" },
                                new { key = "addr", label = "" },
                                new { key = "trail", label = "" },
                                new { key = "type", label = "", align = "right" },
                                new { key = "tag", label = "", align = "right" },
                            },
                            rows = new object[]
                            {
                                new { n = 1, addr = "0x0035C100", trail = "= 100 → 78 → 56", type = "i32", tag = new { kind = "pill", text = "CONFIRMED", tone = "ok" } },
                            }
                        }
                    },
                    new { kind = "handoff", label = new { text = "g_player_health @ 0x0035C100" }, to = new { id = "scorpion", display = "Scorpion" }, note = "Confirmed." }
                }),

            new PlaceholderMessage(
                "[Fatality] Scorpion",
                "CALL GRAPH HUNTER",
                240,
                900,
                "done",
                new object[]
                {
                    new
                    {
                        kind = "tool-call",
                        tool = "callgraph.writers",
                        args = new { addr = "0x0035C100", depth = 2 },
                        status = "done",
                        summary = "1 writer · 2 callers",
                        durationMs = 600,
                        body = new
                        {
                            kind = "disasm",
                            header = "Placeholder writers",
                            lines = new object[]
                            {
                                new { addr = "00204A44", op = "sw", ops = "$a0, 0($s0)", note = "sole writer", tag = "WRITE" },
                                new { addr = "00204A30", op = "fn", ops = "Player::UpdateHealth(int)", tag = "FN" },
                                new { addr = "00208C1C", op = "call", ops = "Player::OnTakeDamage()", tag = "CALLER" },
                                new { addr = "00209E40", op = "call", ops = "Player::OnHealItem()", tag = "CALLER" },
                            }
                        }
                    },
                    new
                    {
                        kind = "tool-call",
                        tool = "disasm.range",
                        args = new { fn = "Player::UpdateHealth", count = 8 },
                        status = "done",
                        durationMs = 200,
                        body = new
                        {
                            kind = "disasm",
                            header = "Player::UpdateHealth(int newHealth)",
                            lines = new object[]
                            {
                                new { addr = "00204A30", op = "addiu", ops = "$sp, $sp, -0x20" },
                                new { addr = "00204A34", op = "sw", ops = "$ra, 0x1C($sp)" },
                                new { addr = "00204A38", op = "sw", ops = "$s0, 0x18($sp)" },
                                new { addr = "00204A44", op = "sw", ops = "$a0, 0x00($s0)", note = "← hook point", highlight = true },
                                new { addr = "00204A48", op = "jal", ops = "Player::OnHealthDamaged" },
                            }
                        }
                    },
                    new { kind = "handoff", label = new { text = "hook point 0x00204A48" }, to = new { id = "kitana", display = "Kitana" }, note = "Writer path mapped." }
                }),

            new PlaceholderMessage(
                "[Fatality] Kitana",
                "STRUCTURE ARCHITECT",
                140,
                700,
                "done",
                new object[]
                {
                    new { kind = "text", md = "HP lives at offset [[+0x00]] of placeholder [[CZPlayer]]. Mapped sibling fields:" },
                    new
                    {
                        kind = "tool-call",
                        tool = "struct.map",
                        args = new { @base = "CZPlayer", addr = "0x0035C100" },
                        status = "done",
                        summary = "9 fields · stable across 3 frames",
                        durationMs = 500,
                        body = new
                        {
                            kind = "struct-map",
                            @base = "CZPlayer",
                            addr = "0x0035C100",
                            summary = "9 placeholder fields · stable across 3 frames",
                            fields = new object[]
                            {
                                new { offset = "+0x00", type = "i32", name = "health", value = "78" },
                                new { offset = "+0x04", type = "i32", name = "max_health", value = "100" },
                                new { offset = "+0x08", type = "f32", name = "stamina", value = "0.82" },
                                new { offset = "+0x0C", type = "f32", name = "max_stamina", value = "1.00" },
                            }
                        }
                    },
                    new { kind = "handoff", label = new { text = "CZPlayer.health +0x00" }, to = new { id = "kano", display = "Kano" }, note = "Struct context ready." }
                }),

            new PlaceholderMessage(
                "[Fatality] Kano",
                "CDS PATCHER",
                380,
                1100,
                "done",
                new object[]
                {
                    new { kind = "text", md = "Built a hook-safe placeholder payload. It reads HP from `$s0`, compares against 50, and traps if low." },
                    new
                    {
                        kind = "code",
                        language = "cds",
                        filename = "player_hp_watch.cds",
                        source = "@hook 0x00204A48 // post-store, pre-jal\n@joker_safe\nfn watch_player_hp() {\n    lw    $t0, 0x00($s0)\n    slti  $t1, $t0, 50\n    beqz  $t1, skip\n    nop\n    @break(\"hp low: %d\", $t0)\nskip:\n    @return\n}",
                        actions = new[] { "copy", "run", "insert" }
                    },
                    new
                    {
                        kind = "tool-call",
                        tool = "cds.compile",
                        args = new { src = "player_hp_watch.cds", mode = "dry-run" },
                        status = "done",
                        summary = "28 bytes · 7 instructions",
                        durationMs = 18,
                        body = new { kind = "kv", items = new object[] { new { k = "bytes", v = "28" }, new { k = "instructions", v = "7" } } }
                    },
                    new { kind = "handoff", label = new { text = "player_hp_watch.cds" }, to = new { id = "sonya", display = "Sonya" }, note = "Patch compiled." }
                }),

            new PlaceholderMessage(
                "[Fatality] Sonya",
                "RUNTIME VERIFIER",
                220,
                2400,
                "streaming",
                new object[]
                {
                    new { kind = "text", md = "Setting a placeholder watchpoint on [[0x0035C100]], dry-injecting the patch, and stepping through one damage event." },
                    new
                    {
                        kind = "tool-call",
                        tool = "runtime.verify",
                        args = new { watch = "0x0035C100", steps = "auto", dry = true },
                        status = "running",
                        durationMs = 2400,
                        body = new
                        {
                            kind = "timeline",
                            rows = new object[]
                            {
                                new { t = "+0.0s", status = "done", label = "Attached to placeholder runtime" },
                                new { t = "+0.4s", status = "done", label = "Installed dry-run patch" },
                                new { t = "+1.1s", status = "done", label = "Observed health write at [[0x0035C100]]" },
                                new { t = "+1.9s", status = "done", label = "Trap condition evaluated", tag = new { kind = "pill", text = "PENDING", tone = "warn" } },
                                new { t = "+2.4s", status = "running", label = "Waiting for final verifier result" },
                            }
                        }
                    },
                    new
                    {
                        kind = "actions",
                        chips = new object[]
                        {
                            new { id = "inject-real", label = "Inject for real", command = "@Raiden inject the patch and step until it breaks" },
                            new { id = "break-max-health", label = "Also break on max_health change", command = "@Kitana add a max_health watchpoint" },
                            new { id = "handoff-raiden", label = "Hand back to Raiden", command = "@Raiden summarize verifier status" },
                        }
                    }
                }),
        };

        // ── Helpers ──────────────────────────────────────────────────────────

        private int MessageCountSync(int sessionId)
        {
            // Cheap count — used only to populate the session sidebar.
            // ListMessagesAsync ships a full row, so use a smaller query.
            try
            {
                return _ctx.Db.McpChatMessages.Count(m => m.McpChatSessionId == sessionId);
            }
            catch { return 0; }
        }

        private static McpChatSessionInfo ToSessionDto(McpChatSession s, IReadOnlyDictionary<int, string> orchById, IReadOnlyDictionary<int, int> counts)
        {
            string? orchName = null;
            if (s.McpOrchestrationId is int oid && orchById.TryGetValue(oid, out var name)) orchName = name;
            int count = counts.TryGetValue(s.Id, out var c) ? c : 0;
            return new McpChatSessionInfo
            {
                Id                = s.Id,
                Title             = s.Title,
                OrchestrationId   = s.McpOrchestrationId,
                OrchestrationName = orchName,
                GameSerial        = s.GameSerial,
                GameTag           = s.GameTag,
                MessageCount      = count,
                CreatedAt         = s.CreatedAt.ToString("O"),
                UpdatedAt         = s.UpdatedAt.ToString("O"),
            };
        }

        private static McpChatSessionInfo ToSessionDto(McpChatSession s, string? orchName, int messageCount)
            => new McpChatSessionInfo
            {
                Id                = s.Id,
                Title             = s.Title,
                OrchestrationId   = s.McpOrchestrationId,
                OrchestrationName = orchName,
                GameSerial        = s.GameSerial,
                GameTag           = s.GameTag,
                MessageCount      = messageCount,
                CreatedAt         = s.CreatedAt.ToString("O"),
                UpdatedAt         = s.UpdatedAt.ToString("O"),
            };

        private static McpChatMessageInfo ToMessageDto(McpChatMessage m) => new McpChatMessageInfo
        {
            Id           = m.Id,
            SessionId    = m.McpChatSessionId,
            Kind         = m.Kind,
            AgentId      = m.McpAgentId,
            AgentDisplay = m.AgentDisplay,
            Role         = m.Role,
            BlocksJson   = m.BlocksJson,
            Tokens       = m.Tokens,
            LatencyMs    = m.LatencyMs,
            Status       = m.Status,
            CreatedAt    = m.CreatedAt.ToString("O"),
            UpdatedAt    = m.UpdatedAt.ToString("O"),
        };
    }
}
