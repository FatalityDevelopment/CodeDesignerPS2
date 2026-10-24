using CodeDesigner.Ps2.FileFormats.Gta;
using CodeDesigner.Ps2.FileFormats.Zipper.Anim;
using CodeDesigner.Ps2.FileFormats.Zipper.Conversion;
using CodeDesigner.Ps2.FileFormats.Zipper.Package;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class GtaAssetTests
{
    private const string Iso = @"F:\GTA SA.iso", Run = @"G:\RUN";

    [TestMethod]
    public void Gta_ModelsTexturesAndClipsDecode()
    {
        if (!File.Exists(Iso)) Assert.Inconclusive("GTA: San Andreas disc image (F:) missing");
        var pkg = GtaPackage.Open(Iso);
        var kinds = pkg.Models.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g => g.Count());
        Assert.AreEqual(50, kinds["weapon"]);
        Assert.IsTrue(kinds["character"] >= 338, "story peds (unlisted, skinned) count as characters");

        var swat = pkg.BuildMesh(pkg.FindModel("swat")!);
        Assert.AreEqual(1050, swat.Chunks.Sum(c => c.Triangles.Length / 3));
        Assert.IsNotNull(swat.Skeleton);
        Assert.IsTrue(swat.BoundsMax[1] > 0.8f && swat.BoundsMin[1] < -0.9f, "the ped stands Y up");
        Assert.IsNotNull(pkg.FindTexture(swat.Chunks[0].Texture!));

        var clips = pkg.Animations();
        Assert.AreEqual(1919, clips.Count);
        var walk = pkg.FindAnimation("WALK_civi", "ped.ifp")!;
        Assert.AreEqual(1.133f, walk.Duration, 0.01f);
        Assert.AreEqual("Root", pkg.BoneName(walk.Tracks[0].BoneId, walk.Tracks[0].Name));

        // Posed by its first key, idle_stance keeps the ped upright (the root's bind rotation is game -> model space).
        var idle = pkg.FindAnimation("idle_stance", "ped.ifp")!;
        var posed = GtaAnimations.Pose(swat.Skeleton!, idle, 1, pkg.BoneName)[0];
        int head = swat.Skeleton!.Bones.FindIndex(b => b.Name == "Head");
        Assert.AreEqual(swat.Skeleton.Bones[head].World[13], posed[head][13], 0.05f);
    }

    [TestMethod]
    public void Convert_GtaToSocom2OpensAsSocom2()
    {
        if (!File.Exists(Iso) || !File.Exists(Path.Combine(Run, "MP52.ZDB"))) Assert.Inconclusive("GTA disc (F:) or SOCOM II RUN (G:) missing");
        var pkg = GtaPackage.Open(Iso);
        var templates = new Socom2Templates(Run);
        string dir = Path.Combine(Path.GetTempPath(), "cd_gta2s2");

        // A ped with clips: a SOCOM II character on alg_UN01's skeleton, SEAL-sized, with its clips in MOTION_S.ZAR.
        var swat = pkg.BuildMesh(pkg.FindModel("swat")!);
        var names = new[] { "idle_stance", "WALK_civi" };
        var clips = names.Select(n =>
        {
            var clip = pkg.FindAnimation(n, "ped.ifp")!;
            int frames = (int)Math.Round(clip.Duration * 30);
            var posed = GtaAnimations.Pose(swat.Skeleton!, clip, frames, pkg.BoneName);
            return new ModelToSocom2.SourceClip(clip.Name, clip.Duration, frames, k => posed[k]);
        }).ToList();
        var r = ModelToSocom2.Export(new ModelToSocom2.Source(swat, "character", 10.4f,
                t => pkg.FindTexture(t) is { } x ? (x.Rgba, x.Width, x.Height) : null, GtaPedRig.BoneMap, GtaPedRig.Anchors),
            clips, templates, new CaToSocom2.Options("mgswat", "gta_swat", dir));
        Assert.AreEqual(0, r.Warnings.Count, string.Join("; ", r.Warnings));
        var s2 = ZipperPackage.Open(r.CdbPath);
        var mesh = ZipperModelMesh.Build(s2, s2.Models().Single(m => m.Name == "MESH_gta_swat"));
        Assert.AreEqual(1050, mesh.Chunks.Sum(c => c.Triangles.Length / 3));
        Assert.IsTrue(mesh.BoundsMax[1] > 18 && mesh.BoundsMax[1] < 21, $"height {mesh.BoundsMax[1]}");
        CollectionAssert.AreEquivalent(new[] { "IDLE_stance", "WALK_civi" }, PackageAnimations.List(s2, null).Select(x => x.Name).ToList());

        // A weapon: the held layout (high / low then the points), positions in the chunk-scale unit.
        var m4 = pkg.FindModel("m4")!;
        var w = ModelToSocom2.Export(new ModelToSocom2.Source(pkg.BuildMesh(m4), "weapon", 10.4f,
                t => pkg.FindTexture(t) is { } x ? (x.Rgba, x.Width, x.Height) : null,
                Points: new Dictionary<string, float[]> { ["firepoint"] = pkg.FramePoints(m4)["gunflash"] }),
            Array.Empty<ModelToSocom2.SourceClip>(), templates, new CaToSocom2.Options("mgm4", "gta_m4", dir));
        var wp = ZipperPackage.Open(w.CdbPath);
        var gun = ZipperModelMesh.Build(wp, wp.Models().Single(m => m.Name == "gta_m4"));
        Assert.AreEqual(2 * 420, gun.Chunks.Sum(c => c.Triangles.Length / 3), "high + low");
        Assert.IsTrue(gun.BoundsMax[0] > 7 && gun.BoundsMin[0] < -2, "forward along +x, about 10 units long");
    }
}
