using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class ToolProfileTests
{
    private static ToolProfile Profile(string name) => ToolProfile.Known[name];

    [TestMethod]
    public void Core_AllowsCoreDomain_WithinRiskCap()
    {
        var core = Profile("core");
        Assert.IsTrue(core.Allows("cd_status", ToolDomain.Core, ToolRisk.ReadOnly));
        Assert.IsTrue(core.Allows("progress_save", ToolDomain.Core, ToolRisk.MutatesDatabase));
    }

    [TestMethod]
    public void Core_BlocksNonCoreDomain_AndOverCapRisk()
    {
        var core = Profile("core");
        Assert.IsFalse(core.Allows("memory_read", ToolDomain.Memory, ToolRisk.ReadOnly)); // domain not allowed
        Assert.IsFalse(core.Allows("x", ToolDomain.Core, ToolRisk.WritesDisk));           // exceeds MutatesDatabase cap
    }

    [TestMethod]
    public void Re_AllowsMemoryUpToWritesMemory_BlocksNetwork()
    {
        var re = Profile("re");
        Assert.IsTrue(re.Allows("memory_write", ToolDomain.Memory, ToolRisk.WritesMemory));
        Assert.IsTrue(re.Allows("label_add", ToolDomain.Labels, ToolRisk.MutatesDatabase));
        Assert.IsFalse(re.Allows("net_capture", ToolDomain.Network, ToolRisk.ReadOnly));            // domain not in profile
        Assert.IsFalse(re.Allows("net_inject", ToolDomain.Memory, ToolRisk.NetworkInjection));      // exceeds WritesMemory cap
    }

    [TestMethod]
    public void Lite_CuratedList_IgnoresDomainButHonoursRiskCap()
    {
        var lite = Profile("lite");
        Assert.IsTrue(lite.Allows("get_pc", ToolDomain.Debug, ToolRisk.ReadOnly));    // in list though Debug isn't a lite domain
        Assert.IsTrue(lite.Allows("memory_read", ToolDomain.Memory, ToolRisk.ReadOnly));
        Assert.IsFalse(lite.Allows("scan_value", ToolDomain.Memory, ToolRisk.ReadOnly)); // not in curated list
        Assert.IsFalse(lite.Allows("progress_save", ToolDomain.Core, ToolRisk.WritesMemory)); // in list but over cap
    }

    [TestMethod]
    public void FromArgs_Default_IsCore()
    {
        var p = ToolProfile.FromArgs(Array.Empty<string>());
        Assert.AreEqual("core", p.Name);
        Assert.AreEqual(ToolRisk.MutatesDatabase, p.MaxRisk);
    }

    [TestMethod]
    public void FromArgs_NamedProfile_Resolves()
    {
        var p = ToolProfile.FromArgs(new[] { "--profile", "re" });
        Assert.AreEqual("re", p.Name);
        Assert.AreEqual(ToolRisk.WritesMemory, p.MaxRisk);
    }

    [TestMethod]
    public void FromArgs_ProfileName_IsCaseInsensitive()
        => Assert.AreEqual("re", ToolProfile.FromArgs(new[] { "--profile", "RE" }).Name);

    [TestMethod]
    public void FromArgs_UnknownProfile_FallsBackToCore()
        => Assert.AreEqual("core", ToolProfile.FromArgs(new[] { "--profile", "does-not-exist" }).Name);

    [TestMethod]
    public void FromArgs_Readonly_CapsRiskAtReadOnly()
    {
        var p = ToolProfile.FromArgs(new[] { "--profile", "re", "--readonly" });
        Assert.AreEqual(ToolRisk.ReadOnly, p.MaxRisk);
        Assert.IsFalse(p.Allows("memory_write", ToolDomain.Memory, ToolRisk.WritesMemory));
        Assert.IsTrue(p.Allows("memory_read", ToolDomain.Memory, ToolRisk.ReadOnly));
    }

    [TestMethod]
    public void FromArgs_DangerousFalse_DowngradesOnlyDangerousProfiles()
    {
        var all = ToolProfile.FromArgs(new[] { "--profile", "all", "--dangerous-tools", "false" });
        Assert.AreEqual(ToolRisk.NetworkInjection, all.MaxRisk); // was Dangerous

        var re = ToolProfile.FromArgs(new[] { "--profile", "re", "--dangerous-tools", "false" });
        Assert.AreEqual(ToolRisk.WritesMemory, re.MaxRisk);      // unchanged (wasn't Dangerous)
    }
}
