using System.Text.Json;
using System.Text.Json.Nodes;
using CodeDesigner.AI.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Soft friction for high-risk tools (risk severity above <see cref="ToolRisk.MutatesDatabase"/>).
/// The wrapped tool's input schema gains an <em>optional</em> <c>reason</c> string and its
/// description picks up a one-line high-risk note, so the model is nudged to justify the
/// change. There is <b>no hard block</b>: a call with no reason still runs — it's just logged
/// to <see cref="SessionState"/> and gets a one-line footer reminding the model to justify
/// next time. Calls that <em>do</em> supply a reason run silently (good behaviour costs no
/// extra tokens). The risky-op tally surfaces in <c>cd_check_session_health</c>.
/// </summary>
internal sealed class RiskFrictionTool : DelegatingMcpServerTool
{
    private const string ReasonArg = "reason";

    private readonly SessionState _state;
    private readonly string _toolName;
    private readonly ToolRisk _risk;

    public RiskFrictionTool(McpServerTool inner, SessionState state, string toolName, ToolRisk risk)
        : base(inner)
    {
        _state = state;
        _toolName = toolName;
        _risk = risk;
        AugmentInPlace(inner.ProtocolTool, risk); // adds optional `reason` + high-risk note
    }

    /// <summary>True when a tool at this risk level should carry the reason nudge.</summary>
    public static bool AppliesTo(ToolRisk risk) =>
        risk.Severity() > ToolRisk.MutatesDatabase.Severity();

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        string? reason = TryGetReason(request);

        // Strip our injected arg so the inner tool's binder never sees an unknown property.
        request.Params?.Arguments?.Remove(ReasonArg);

        long opNo = _state.RecordRiskyOp(_toolName, _risk, reason);
        var result = await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

        // Only nag when the model skipped the reason — don't tax well-behaved calls with tokens.
        if (reason is null)
            AppendNudge(result, opNo);

        return result;
    }

    private static string? TryGetReason(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments is { } args &&
            args.TryGetValue(ReasonArg, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        return null;
    }

    private void AppendNudge(CallToolResult result, long opNo)
    {
        var footer =
            $"⚠ high-risk tool ({_toolName}, {_risk}) ran with no `reason` (risky op #{opNo}). " +
            "Pass a one-line `reason` next time; review with cd_check_session_health.";

        if (result.Content is { Count: > 0 } && result.Content[^1] is TextContentBlock tb)
            tb.Text = $"{tb.Text}\n\n{footer}";
        else
            (result.Content ??= new List<ContentBlock>()).Add(new TextContentBlock { Text = footer });
    }

    /// <summary>
    /// Mutates the wrapped tool's protocol definition in place (it is a plain class, not a
    /// record). Safe because the inner tool is created fresh for this wrapper and is never
    /// exposed except through it.
    /// </summary>
    private static void AugmentInPlace(Tool tool, ToolRisk risk)
    {
        var note = $" ⚠ High-risk ({risk}): pass a one-line `reason` to justify the change " +
                   "(optional — the call still runs without it).";
        tool.Description = (tool.Description ?? string.Empty) + note;
        tool.InputSchema = AddReasonProperty(tool.InputSchema);
    }

    /// <summary>Clone the tool's JSON input schema and add an optional <c>reason</c> string property.</summary>
    private static JsonElement AddReasonProperty(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject obj)
            return schema;

        obj["type"] ??= "object";
        if (obj["properties"] is not JsonObject props)
        {
            props = new JsonObject();
            obj["properties"] = props;
        }

        props[ReasonArg] ??= new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Why this high-risk change is needed (one line). " +
                              "Optional but recommended; logged to session health.",
        };

        return JsonSerializer.SerializeToElement(obj);
    }
}
