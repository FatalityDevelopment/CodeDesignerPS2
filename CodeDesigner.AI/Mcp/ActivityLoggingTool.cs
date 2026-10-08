using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
///
/// Every tool's schema also gains a required <c>reason</c> argument — one line
/// on why the AI is making the call — which is logged with the call (so the
/// activity log / Discord feed explains itself) and stripped before the tool
/// runs. A call that omits it still runs; it is just logged without a reason.
/// </summary>
internal sealed class ActivityLoggingTool : DelegatingMcpServerTool
{
    private const int MaxFieldChars = 4000;

    /// <summary>Results get a larger cap: the Discord feed attaches the full text when it overflows a card.</summary>
    private const int MaxResultChars = 16000;

    private const int MaxReasonChars = 512;

    /// <summary>
    /// Keep quotes, apostrophes, '+' and non-ASCII readable in the log. The default
    /// encoder writes them as <c>"</c>-style escapes, which then show up verbatim in
    /// the panel and Discord. The payload is only ever parsed, never embedded in HTML.
    /// </summary>
    private static readonly JsonSerializerOptions LogJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string ReasonArg = "reason";

    private readonly IEmulatorBridge _bridge;
    private readonly string _toolName;
    private readonly bool _stripReason;

    /// <param name="stripReason">
    /// Remove <c>reason</c> before invoking the inner tool. False when the inner
    /// chain is a <see cref="RiskFrictionTool"/>, which reads and strips it itself.
    /// </param>
    public ActivityLoggingTool(McpServerTool inner, IEmulatorBridge bridge, string toolName, bool stripReason = true)
        : base(inner)
    {
        _bridge = bridge;
        _toolName = toolName;
        _stripReason = stripReason;
        inner.ProtocolTool.InputSchema = RequireReason(inner.ProtocolTool.InputSchema);
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        // Snapshot the args before the inner tool can mutate them (e.g.
        // RiskFrictionTool strips its injected `reason`).
        string argsJson = SerializeArgs(request);
        string? reason = TryGetReason(request);
        if (_stripReason) { request.Params?.Arguments?.Remove(ReasonArg); }
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
            PushSafe(argsJson, reason, $"EXCEPTION: {ex.Message}", ok: false,
                     (int)(DateTime.UtcNow - started).TotalMilliseconds);
            throw;
        }

        PushSafe(argsJson, reason, PreviewResult(result), ok,
                 (int)(DateTime.UtcNow - started).TotalMilliseconds);
        return result;
    }

    private void PushSafe(string argsJson, string? reason, string resultPreview, bool ok, int elapsedMs)
    {
        try
        {
            // The payload is a compact JSON object the bridge forwards verbatim
            // to subscribers as `ai_tool_call`.
            var payload = JsonSerializer.Serialize(new
            {
                tool = _toolName,
                args = argsJson,
                reason,
                result = resultPreview,
                ok,
                elapsedMs,
            }, LogJson);

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
            foreach (var (k, v) in args)
            {
                if (k == ReasonArg) { continue; } // logged separately
                obj[k] = v.ValueKind == JsonValueKind.Null ? null : (object)v;
            }
            return Truncate(JsonSerializer.Serialize(obj, LogJson), MaxResultChars);
        }
        catch { return "{…unserializable…}"; }
    }

    private static string PreviewResult(CallToolResult result)
    {
        if (result.Content is not { Count: > 0 }) { return ok(result) ? "(no content)" : "(error, no content)"; }
        var sb = new StringBuilder();
        foreach (var block in result.Content)
        {
            // Strip nulls before measuring: bridge responses serialise every
            // CommandResponse field, so the useful payload would otherwise sit
            // behind ~100 null properties and be truncated away.
            if (block is TextContentBlock tb) { sb.Append(StripJsonNulls(tb.Text)); }
            else { sb.Append('[').Append(block.Type).Append(']'); }
            if (sb.Length >= MaxFieldChars) { break; }
        }
        return Truncate(sb.ToString(), MaxResultChars);

        static bool ok(CallToolResult r) => r.IsError != true;
    }

    private static string? TryGetReason(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments is { } args &&
            args.TryGetValue(ReasonArg, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString()?.Trim();
            if (string.IsNullOrEmpty(s)) { return null; }
            return s.Length <= MaxReasonChars ? s : s[..MaxReasonChars];
        }
        return null;
    }

    /// <summary>Clone the tool's JSON input schema with a required <c>reason</c> string property.</summary>
    private static JsonElement RequireReason(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject obj) { return schema; }

        obj["type"] ??= "object";
        if (obj["properties"] is not JsonObject props)
        {
            props = new JsonObject();
            obj["properties"] = props;
        }

        // Overwrites RiskFrictionTool's optional wording for high-risk tools.
        props[ReasonArg] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "One line: why you are making this call and what you expect to learn or change. " +
                              "Shown in the AI activity log.",
        };

        if (obj["required"] is not JsonArray required)
        {
            required = new JsonArray();
            obj["required"] = required;
        }
        if (!required.Any(n => n?.GetValue<string>() == ReasonArg)) { required.Add(ReasonArg); }

        return JsonSerializer.SerializeToElement(obj);
    }

    /// <summary>
    /// Re-serialises a JSON object/array text without null-valued properties;
    /// returns <paramref name="text"/> unchanged when it is not JSON.
    /// </summary>
    private static string StripJsonNulls(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) { return text; }
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(text);
            if (node is null) { return text; }
            RemoveNulls(node);
            return node.ToJsonString(LogJson);
        }
        catch (JsonException) { return text; }

        static void RemoveNulls(System.Text.Json.Nodes.JsonNode node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                foreach (var key in obj.Where(p => p.Value is null).Select(p => p.Key).ToList()) { obj.Remove(key); }
                foreach (var (_, child) in obj) { RemoveNulls(child!); }
            }
            else if (node is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var child in arr) { if (child is not null) { RemoveNulls(child); } }
            }
        }
    }

    private static string Truncate(string s, int max = MaxFieldChars) =>
        s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
}
