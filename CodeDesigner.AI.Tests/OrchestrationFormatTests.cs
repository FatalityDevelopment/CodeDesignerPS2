using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Tools.Admin;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class OrchestrationFormatTests
{
    // Mirrors the bridge's MCPORCHESTRATION_LIST response: CommandResponse wrapper +
    // McpOrchestrationInfo items (camelCase per their [JsonPropertyName] attributes).
    private const string ListWithActive = """
    {
      "Success": true,
      "Command": "MCPORCHESTRATION_LIST",
      "McpOrchestrations": [
        { "id": 1, "isActive": false, "name": "Default", "createdAt": "2026-01-01", "updatedAt": "2026-01-01" },
        { "id": 2, "isActive": true,  "name": "Team Plan", "strategy": "be fast", "workflow": "1. recon\n2. patch", "createdAt": "2026-01-02", "updatedAt": "2026-05-30" }
      ]
    }
    """;

    [TestMethod]
    public void ActivePlan_PicksTheActiveOne_WithFields()
    {
        var result = OrchestrationFormat.ActivePlan(ListWithActive)!;
        StringAssert.Contains(result, "Active plan: Team Plan  [id 2]");
        StringAssert.Contains(result, "## Strategy");
        StringAssert.Contains(result, "be fast");
        StringAssert.Contains(result, "## Workflow");
        StringAssert.Contains(result, "1. recon");
        // The inactive plan must not leak into the output.
        Assert.IsFalse(result.Contains("Default"), "should not include the inactive plan");
    }

    [TestMethod]
    public void ActivePlan_OmitsAbsentMarkdownFields()
    {
        var result = OrchestrationFormat.ActivePlan(ListWithActive)!;
        Assert.IsFalse(result.Contains("## Description"));
        Assert.IsFalse(result.Contains("## Roles"));
        Assert.IsFalse(result.Contains("## Notes"));
    }

    [TestMethod]
    public void ActivePlan_NoneActive_ReturnsHint()
    {
        const string json = """
        { "Success": true, "Command": "MCPORCHESTRATION_LIST",
          "McpOrchestrations": [ { "id": 1, "isActive": false, "name": "A", "createdAt": "x", "updatedAt": "x" } ] }
        """;
        var result = OrchestrationFormat.ActivePlan(json);
        StringAssert.Contains(result, "no active orchestration plan");
    }

    [TestMethod]
    public void ActivePlan_FieldCasingIsInsensitive()
    {
        // camelCase wrapper key (some serializers) must still resolve.
        const string json = """
        { "mcpOrchestrations": [ { "id": 7, "isActive": true, "name": "Lower" } ] }
        """;
        StringAssert.Contains(OrchestrationFormat.ActivePlan(json)!, "Active plan: Lower  [id 7]");
    }

    [TestMethod]
    public void ActivePlan_ErrorOrUnrecognised_ReturnsNull()
    {
        Assert.IsNull(OrchestrationFormat.ActivePlan("""{ "Success": false, "Error": "boom" }"""));
        Assert.IsNull(OrchestrationFormat.ActivePlan("not json at all"));
    }

    // ── StageLine ───────────────────────────────────────────────────────────

    private static OrchestrationInfo Info(string workflow, string? stage)
        => new(1, "p", workflow, stage, null, null, WorkflowParser.Parse(workflow));

    [TestMethod]
    public void StageLine_ShowsCurrentAndNext()
    {
        var line = OrchestrationFormat.StageLine(Info("1. a\n2. b\n3. c", "2"))!;
        StringAssert.Contains(line, "Stage: 2");
        StringAssert.Contains(line, "→ 2. b");
        StringAssert.Contains(line, "next: 3. c");
    }

    [TestMethod]
    public void StageLine_NotStarted_NextIsFirstStep()
    {
        var line = OrchestrationFormat.StageLine(Info("1. a\n2. b", null))!;
        StringAssert.Contains(line, "(not started)");
        StringAssert.Contains(line, "next: 1. a");
    }

    [TestMethod]
    public void StageLine_AtLastStep_NoNext()
    {
        var line = OrchestrationFormat.StageLine(Info("1. a\n2. b", "2"))!;
        StringAssert.Contains(line, "→ 2. b");
        Assert.IsFalse(line.Contains("next:"));
    }

    [TestMethod]
    public void StageLine_NoStepsNoStage_ReturnsNull()
        => Assert.IsNull(OrchestrationFormat.StageLine(Info("", null)));
}
