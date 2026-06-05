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
        /// Persists the user prompt and emits a placeholder orchestrator reply
        /// so the chat renderer has content to display. Returns both rows so
        /// the webview can append them without a follow-up round trip.
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
                // 1. Persist the user message.
                var userBlocks = JsonSerializer.Serialize(new[]
                {
                    new { kind = "text", md = cmd.McpChatUserText! }
                });
                var userMsg = _ctx.ChatRepo.AppendMessageAsync(
                    cmd.McpChatSessionId.Value,
                    "user", null, null, null,
                    userBlocks, null, null, "done"
                ).GetAwaiter().GetResult();

                // 2. Emit a placeholder orchestrator reply. Real implementation
                //    will start in "streaming" status and grow blocks over time.
                var replyBlocks = JsonSerializer.Serialize(new object[]
                {
                    new { kind = "text", md = "Placeholder reply — orchestrator pipeline not yet wired up." },
                    new
                    {
                        kind  = "dispatch",
                        title = "Dispatch · placeholder",
                        rows  = new object[]
                        {
                            new { n = 1, agent = new { id = "placeholder", display = "Placeholder" },
                                  task = "Replace with real subagent work", status = "queued" }
                        }
                    }
                });

                var replyMsg = _ctx.ChatRepo.AppendMessageAsync(
                    cmd.McpChatSessionId.Value,
                    "agent",
                    null,
                    "Orchestrator",
                    "ORCHESTRATOR",
                    replyBlocks, null, null, "done"
                ).GetAwaiter().GetResult();

                return Responses.Json(new CommandResponse
                {
                    Success         = true,
                    Command         = "MCPCHAT_SEND",
                    McpChatMessages = new List<McpChatMessageInfo>
                    {
                        ToMessageDto(userMsg),
                        ToMessageDto(replyMsg),
                    },
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPCHAT_SEND error: {ex.Message}"); }
        }

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
