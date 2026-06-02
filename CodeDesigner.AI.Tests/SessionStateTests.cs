using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Mcp;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class SessionStateTests
{
    [TestMethod]
    public void FreshState_HasZeroCountersAndNoGoal()
    {
        var s = new SessionState();
        Assert.AreEqual(0, s.LabelsAdded);
        Assert.AreEqual(0, s.RiskyOps);
        Assert.AreEqual(0, s.RiskyOpsWithoutReason);
        Assert.IsNull(s.Goal);
        Assert.IsNull(s.LastSave);
        Assert.IsNull(s.LastLabelAdded);
        Assert.AreEqual(0, s.MilestoneCount);
    }

    [TestMethod]
    public void RecordLabelAdded_Accumulates_AndStampsTime()
    {
        var s = new SessionState();
        s.RecordLabelAdded();
        s.RecordLabelAdded(5);
        Assert.AreEqual(6, s.LabelsAdded);
        Assert.IsNotNull(s.LastLabelAdded);
    }

    [TestMethod]
    public void RecordLabelAdded_NonPositive_IsNoOp()
    {
        var s = new SessionState();
        s.RecordLabelAdded(0);
        s.RecordLabelAdded(-3);
        Assert.AreEqual(0, s.LabelsAdded);
        Assert.IsNull(s.LastLabelAdded);
    }

    [TestMethod]
    public void SetGoal_And_RecordSave_AreReflected()
    {
        var s = new SessionState();
        s.SetGoal("map login handler");
        s.RecordSave();
        Assert.AreEqual("map login handler", s.Goal);
        Assert.IsNotNull(s.LastSave);
    }

    [TestMethod]
    public void RecordRiskyOp_CountsAndReturnsRunningTotal()
    {
        var s = new SessionState();
        Assert.AreEqual(1, s.RecordRiskyOp("memory_write", ToolRisk.WritesMemory, null));
        Assert.AreEqual(2, s.RecordRiskyOp("memory_write", ToolRisk.WritesMemory, "patch health"));
        Assert.AreEqual(2, s.RiskyOps);
        Assert.AreEqual(1, s.RiskyOpsWithoutReason); // only the first had no reason
    }

    [TestMethod]
    public void RecordRiskyOp_BlankReason_CountsAsUnexplained()
    {
        var s = new SessionState();
        s.RecordRiskyOp("t", ToolRisk.WritesDisk, "   ");
        Assert.AreEqual(1, s.RiskyOpsWithoutReason);
    }

    [TestMethod]
    public void RecentRiskyOps_CapsAndReturnsMostRecent()
    {
        var s = new SessionState();
        for (int i = 0; i < 8; i++) s.RecordRiskyOp($"t{i}", ToolRisk.WritesMemory, null);
        var recent = s.RecentRiskyOps(3);
        Assert.AreEqual(3, recent.Count);
        Assert.AreEqual("t7", recent[^1].Tool);
        Assert.AreEqual("t5", recent[0].Tool);
    }

    [TestMethod]
    public void RecordMilestone_TracksCountAndRecent()
    {
        var s = new SessionState();
        s.RecordMilestone("found struct", "at 0x1000");
        s.RecordMilestone("verified hook");
        Assert.AreEqual(2, s.MilestoneCount);
        var recent = s.RecentMilestones(5);
        Assert.AreEqual("found struct", recent[0].Name);
        Assert.AreEqual("verified hook", recent[1].Name);
        Assert.IsNull(recent[1].Summary);
    }

    [TestMethod]
    public void GetSummaryReport_ReflectsGoalAndMilestones()
    {
        var s = new SessionState();
        s.SetGoal("trace packet flow");
        s.RecordMilestone("dispatch found");
        var report = s.GetSummaryReport();
        StringAssert.Contains(report, "trace packet flow");
        StringAssert.Contains(report, "dispatch found");
        StringAssert.Contains(report, "Session summary");
    }

    [TestMethod]
    public void GetHealthReport_RecommendsGoalWhenUnset()
    {
        var report = new SessionState().GetHealthReport();
        StringAssert.Contains(report, "cd_set_goal");
    }
}
