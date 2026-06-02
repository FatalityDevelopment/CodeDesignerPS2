using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class WorkflowParserTests
{
    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("   \n  \t \n")]
    public void Parse_BlankInput_ReturnsEmpty(string? input)
        => Assert.AreEqual(0, WorkflowParser.Parse(input).Count);

    [TestMethod]
    public void Parse_NumberedList_KeepsExplicitNumbers()
    {
        var steps = WorkflowParser.Parse("1. alpha\n2. beta\n3. gamma");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, steps.Select(s => s.Number).ToArray());
        Assert.AreEqual("alpha", steps[0].Text);
        Assert.AreEqual("gamma", steps[2].Text);
    }

    [TestMethod]
    [DataRow("Step 1: do x")]
    [DataRow("1) do x")]
    [DataRow("1. do x")]
    [DataRow("  1.   do x  ")]
    public void Parse_NumberedVariants_ExtractNumberAndText(string line)
    {
        var steps = WorkflowParser.Parse(line);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual(1, steps[0].Number);
        Assert.AreEqual("do x", steps[0].Text);
    }

    [TestMethod]
    public void Parse_NonContiguousNumbers_ArePreserved()
    {
        var steps = WorkflowParser.Parse("1. a\n3. c\n7. g");
        CollectionAssert.AreEqual(new[] { 1, 3, 7 }, steps.Select(s => s.Number).ToArray());
    }

    [TestMethod]
    public void Parse_Headings_WhenNoNumbered_AreSequential()
    {
        var steps = WorkflowParser.Parse("# Recon\n## Locate\n### Verify");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, steps.Select(s => s.Number).ToArray());
        Assert.AreEqual("Recon", steps[0].Text);
        Assert.AreEqual("Verify", steps[2].Text);
    }

    [TestMethod]
    public void Parse_Bullets_WhenNoNumberedOrHeadings_AreSequential()
    {
        var steps = WorkflowParser.Parse("- first\n* second\n+ third");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, steps.Select(s => s.Number).ToArray());
        Assert.AreEqual("second", steps[1].Text);
    }

    [TestMethod]
    public void Parse_Precedence_NumberedWinsOverBullets()
    {
        // Mixed content: a numbered line plus bullets — numbered should win.
        var steps = WorkflowParser.Parse("intro\n- bullet a\n1. real step\n- bullet b");
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual("real step", steps[0].Text);
    }

    [TestMethod]
    public void Parse_FreeformLines_FallbackNumbered()
    {
        var steps = WorkflowParser.Parse("do the first thing\ndo the second thing");
        Assert.AreEqual(2, steps.Count);
        Assert.AreEqual(1, steps[0].Number);
        Assert.AreEqual("do the first thing", steps[0].Text);
    }

    [TestMethod]
    public void ResolveCurrent_EmptyOrNull_ReturnsNull()
    {
        var steps = WorkflowParser.Parse("1. a\n2. b");
        Assert.IsNull(WorkflowParser.ResolveCurrent(steps, null));
        Assert.IsNull(WorkflowParser.ResolveCurrent(Array.Empty<OrchestrationStep>(), "1"));
    }

    [TestMethod]
    public void ResolveCurrent_ByNumber_MatchesStepNumber()
    {
        var steps = WorkflowParser.Parse("1. a\n3. c\n7. g");
        Assert.AreEqual("c", WorkflowParser.ResolveCurrent(steps, "3")!.Text);
        Assert.AreEqual("g", WorkflowParser.ResolveCurrent(steps, " 7 ")!.Text);
    }

    [TestMethod]
    public void ResolveCurrent_ByExactText_CaseInsensitive()
    {
        var steps = WorkflowParser.Parse("# Recon\n# Verify");
        Assert.AreEqual(2, WorkflowParser.ResolveCurrent(steps, "verify")!.Number);
    }

    [TestMethod]
    public void ResolveCurrent_BySubstring_EitherDirection()
    {
        var steps = WorkflowParser.Parse("1. Patch with CDS under snapshot gates\n2. Persist to SQL");
        // stage is a substring of the step text
        Assert.AreEqual(1, WorkflowParser.ResolveCurrent(steps, "Patch")!.Number);
        // step text is a substring of the (longer) stage label
        Assert.AreEqual(2, WorkflowParser.ResolveCurrent(WorkflowParser.Parse("1. Patch\n2. Persist"), "Persist to SQL Server")!.Number);
    }

    [TestMethod]
    public void ResolveCurrent_NoMatch_ReturnsNull()
    {
        var steps = WorkflowParser.Parse("1. a\n2. b");
        Assert.IsNull(WorkflowParser.ResolveCurrent(steps, "nonexistent-stage"));
        Assert.IsNull(WorkflowParser.ResolveCurrent(steps, "99"));
    }

    [TestMethod]
    public void OrchestrationInfo_CurrentAndNext_ResolveAcrossSteps()
    {
        var info = new OrchestrationInfo(1, "p", "1. a\n2. b\n3. c", "2", null, null,
            WorkflowParser.Parse("1. a\n2. b\n3. c"));
        Assert.AreEqual("b", info.CurrentStep!.Text);
        Assert.AreEqual("c", info.NextStep!.Text);
    }

    [TestMethod]
    public void OrchestrationInfo_NotStarted_NextIsFirst()
    {
        var steps = WorkflowParser.Parse("1. a\n2. b");
        var info = new OrchestrationInfo(1, "p", null, null, null, null, steps);
        Assert.IsNull(info.CurrentStep);
        Assert.AreEqual("a", info.NextStep!.Text);
    }

    [TestMethod]
    public void OrchestrationInfo_AtLastStep_NextIsNull()
    {
        var steps = WorkflowParser.Parse("1. a\n2. b");
        var info = new OrchestrationInfo(1, "p", null, "2", null, null, steps);
        Assert.AreEqual("b", info.CurrentStep!.Text);
        Assert.IsNull(info.NextStep);
    }
}
