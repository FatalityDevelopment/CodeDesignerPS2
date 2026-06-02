using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Wraps every MCP tool so that each invocation — name, arguments, and a short
/// preview of the result — is pushed to the bridge as an <c>AI_TOOL_CALL</c>
/// notification. The bridge re-broadcasts it as an <c>ai_tool_call</c> event,
/// which the VS Code "AI Activity" panel renders. This gives the user a
/// complete, replayable log of what the AI did (every tool, with args + result)
/// for debugging and later automation — not just the small allow-list of
/// address-bearing socket commands the bridge used to surface.
///
/// The push is fire-and-forget and best-effort: a logging failure never affects
/// the tool's own result. Payloads are truncated so a huge disassembly dump
/// doesn't flood the panel or the socket.
/// </summary>
internal sealed class ActivityLoggingTool : DelegatingMcpServerTool
{
    private const int MaxFieldChars = 4000;

    private readonly IEmulatorBridge _bridge;
    private readonly string _toolName;

    public ActivityLoggingTool(McpServerTool inner, IEmulatorBridge bridge, string toolName)
        : base(inner)
    {
        _bridge = bridge;
        _toolName = toolName;
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        // Snapshot the args before the inner tool can mutate them (e.g.
        // RiskFrictionTool strips its injected `reason`).
        string argsJson = SerializeArgs(request);
        var started = DateTime.UtcNow;

        CallToolResult result;
        bool ok = true;
        try
        {
            result = await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            ok = result.IsError != true;
        }
        catch (Exception ex)
        {
            // Log the failure too, then rethrow so behaviour is unchanged.
            PushSafe(argsJson, $"EXCEPTION: {ex.Message}", ok: false,
                     (int)(DateTime.UtcNow - started).TotalMilliseconds);
            throw;
        }

        PushSafe(argsJson, PreviewResult(result), ok,
                 (int)(DateTime.UtcNow - started).TotalMilliseconds);
        return result;
    }

    private void PushSafe(string argsJson, string resultPreview, bool ok, int elapsedMs)
    {
        try
        {
            // The payload is a compact JSON object the bridge forwards verbatim
            // to subscribers as `ai_tool_call`.
            var payload = JsonSerializer.Serialize(new
            {
                tool = _toolName,
                args = argsJson,
                result = resultPreview,
                ok,
                elapsedMs,
            });

            // Fire-and-forget — never block the tool result on a log push, and
            // never let a transport error surface.
            _ = Task.Run(async () =>
            {
                try { await _bridge.SendAsync(new { Command = "AI_TOOL_CALL", Data = payload }); }
                catch { /* logging is best-effort */ }
            });
        }
        catch { /* serialization failure — drop the log entry, keep the tool result */ }
    }

    private static string SerializeArgs(RequestContext<CallToolRequestParams> request)
    {
        var args = request.Params?.Arguments;
        if (args is null || args.Count == 0) { return "{}"; }
        try
        {
            var obj = new Dictionary<string, object?>(args.Count);
            foreach (var (k, v) in args) { obj[k] = v.ValueKind == JsonValueKind.Null ? null : (object)v; }
            return Truncate(JsonSerializer.Serialize(obj));
        }
        catch { return "{…unserializable…}"; }
    }

    private static string PreviewResult(CallToolResult result)
    {
        if (result.Content is not { Count: > 0 }) { return ok(result) ? "(no content)" : "(error, no content)"; }
        var sb = new StringBuilder();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock tb) { sb.Append(tb.Text); }
            else { sb.Append('[').Append(block.Type).Append(']'); }
            if (sb.Length >= MaxFieldChars) { break; }
        }
        return Truncate(sb.ToString());

        static bool ok(CallToolResult r) => r.IsError != true;
    }

    private static string Truncate(string s) =>
        s.Length <= MaxFieldChars ? s : s[..MaxFieldChars] + $"… (+{s.Length - MaxFieldChars} chars)";
}
