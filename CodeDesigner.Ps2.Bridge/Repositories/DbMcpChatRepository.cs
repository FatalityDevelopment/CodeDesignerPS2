using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Repositories
{
    /// <summary>
    /// Async persistence layer for the AI control panel's Chat tab. Owns
    /// <see cref="McpChatSession"/> + <see cref="McpChatMessage"/> CRUD and the
    /// streaming-friendly append/patch path the renderer needs.
    /// </summary>
    internal sealed class DbMcpChatRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbMcpChatRepository(CodeDesignerDbContext db) => _db = db;

        // ── Sessions ─────────────────────────────────────────────────────────

        /// <summary>Returns all chat sessions, newest activity first.</summary>
        internal async Task<List<McpChatSession>> ListSessionsAsync()
            => await _db.McpChatSessions
                        .OrderByDescending(s => s.UpdatedAt)
                        .ToListAsync();

        /// <summary>Returns a single session with its full message list.</summary>
        internal async Task<McpChatSession?> GetSessionWithMessagesAsync(int id)
            => await _db.McpChatSessions
                        .Include(s => s.Messages)
                        .FirstOrDefaultAsync(s => s.Id == id);

        /// <summary>Creates a new chat session and returns it.</summary>
        internal async Task<McpChatSession> CreateSessionAsync(
            string title,
            int? orchestrationId,
            string? gameSerial,
            string? gameTag)
        {
            var session = new McpChatSession
            {
                Title              = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim(),
                McpOrchestrationId = orchestrationId,
                GameSerial         = gameSerial,
                GameTag            = gameTag,
                CreatedAt          = DateTime.UtcNow,
                UpdatedAt          = DateTime.UtcNow,
            };

            _db.McpChatSessions.Add(session);
            await _db.SaveChangesAsync();
            return session;
        }

        /// <summary>Renames a session. No-op when the session is missing.</summary>
        internal async Task<McpChatSession?> RenameSessionAsync(int id, string title)
        {
            var session = await _db.McpChatSessions.FindAsync(id);
            if (session is null) return null;

            session.Title     = string.IsNullOrWhiteSpace(title) ? session.Title : title.Trim();
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return session;
        }

        /// <summary>Cascade-deletes a session and all its messages.</summary>
        internal async Task DeleteSessionAsync(int id)
        {
            var session = await _db.McpChatSessions.FindAsync(id);
            if (session is null) return;
            _db.McpChatSessions.Remove(session);
            await _db.SaveChangesAsync();
        }

        // ── Messages ─────────────────────────────────────────────────────────

        /// <summary>Returns the messages in a session ordered by creation.</summary>
        internal async Task<List<McpChatMessage>> ListMessagesAsync(int sessionId)
            => await _db.McpChatMessages
                        .Where(m => m.McpChatSessionId == sessionId)
                        .OrderBy(m => m.CreatedAt)
                        .ThenBy(m => m.Id)
                        .ToListAsync();

        /// <summary>
        /// Appends one message. The caller is responsible for serialising the
        /// block array (see <c>docs/ai-chat-templates.md</c>) — the repository
        /// treats <c>blocksJson</c> as an opaque payload.
        /// </summary>
        internal async Task<McpChatMessage> AppendMessageAsync(
            int sessionId,
            string kind,
            int? agentId,
            string? agentDisplay,
            string? role,
            string blocksJson,
            int? tokens,
            int? latencyMs,
            string status)
        {
            var session = await _db.McpChatSessions.FindAsync(sessionId)
                ?? throw new KeyNotFoundException($"Chat session {sessionId} not found.");

            var now = DateTime.UtcNow;
            var msg = new McpChatMessage
            {
                McpChatSessionId = sessionId,
                Kind             = string.IsNullOrWhiteSpace(kind) ? "user" : kind,
                McpAgentId       = agentId,
                AgentDisplay     = agentDisplay,
                Role             = role,
                BlocksJson       = string.IsNullOrEmpty(blocksJson) ? "[]" : blocksJson,
                Tokens           = tokens,
                LatencyMs        = latencyMs,
                Status           = string.IsNullOrWhiteSpace(status) ? "done" : status,
                CreatedAt        = now,
                UpdatedAt        = now,
            };

            _db.McpChatMessages.Add(msg);
            // Bump the session's UpdatedAt so the sidebar sort stays accurate.
            session.UpdatedAt = now;
            await _db.SaveChangesAsync();
            return msg;
        }

        /// <summary>
        /// Replaces the block payload + meta on an existing message. Used when
        /// finalising a streamed reply (status: streaming → done) or applying a
        /// model-side patch. Pass <c>null</c> to leave a field unchanged.
        /// </summary>
        internal async Task<McpChatMessage?> UpdateMessageAsync(
            int id,
            string? blocksJson,
            int? tokens,
            int? latencyMs,
            string? status)
        {
            var msg = await _db.McpChatMessages.FindAsync(id);
            if (msg is null) return null;

            if (blocksJson is not null) msg.BlocksJson = blocksJson;
            if (tokens     is not null) msg.Tokens     = tokens;
            if (latencyMs  is not null) msg.LatencyMs  = latencyMs;
            if (status     is not null) msg.Status     = status;

            msg.UpdatedAt = DateTime.UtcNow;

            // Mirror the change onto the parent session so it sorts to the top.
            var session = await _db.McpChatSessions.FindAsync(msg.McpChatSessionId);
            if (session is not null) session.UpdatedAt = msg.UpdatedAt;

            await _db.SaveChangesAsync();
            return msg;
        }

        /// <summary>Deletes one message. No-op when missing.</summary>
        internal async Task DeleteMessageAsync(int id)
        {
            var msg = await _db.McpChatMessages.FindAsync(id);
            if (msg is null) return;
            _db.McpChatMessages.Remove(msg);
            await _db.SaveChangesAsync();
        }

        /// <summary>Clears every message in a session (keeps the session row).</summary>
        internal async Task ClearMessagesAsync(int sessionId)
        {
            var rows = await _db.McpChatMessages
                                .Where(m => m.McpChatSessionId == sessionId)
                                .ToListAsync();
            if (rows.Count == 0) return;
            _db.McpChatMessages.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }
    }
}
