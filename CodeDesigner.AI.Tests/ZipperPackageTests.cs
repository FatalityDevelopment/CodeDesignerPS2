using CodeDesigner.Ps2.FileFormats.Zipper.Anim;
using System;
using System.IO;
using System.Linq;
using CodeDesigner.Ps2.FileFormats.Zipper.Geo;
using CodeDesigner.Ps2.FileFormats.Zipper.Model;
using CodeDesigner.Ps2.FileFormats.Zipper.Package;
using CodeDesigner.Ps2.FileFormats.Zipper.ZED;

namespace CodeDesigner.AI.Tests;

// Asset indexing on the retail map packages (G:\RUN, or ZIPPER_RUN_DIR). The expected counts are the ones the
// Zipper workbench composes for the same maps, so the bridge and the editors agree. Skipped when the game files
// are not present.
[TestClass]
public class ZipperPackageTests
{
    private static string RunDir => Environment.GetEnvironmentVariable("ZIPPER_RUN_DIR") ?? @"G:\RUN";

    private static ZipperPackage OpenMap(string name)
    {
        string path = Path.Combine(RunDir, name + ".ZDB");
        if (!File.Exists(path)) Assert.Inconclusive($"{path} not available");
        return ZipperPackage.Open(path);
    }

    private static (int Model, int Reference, int Clutter) Compose(ZipperPackage pkg)
    {
        var map = pkg.MapScene();
        Assert.IsNotNull(map, "map scene");
        Func<string, bool> resolves = n => pkg.ResolveModel(n) != null;
        var instances = SceneInstances.Collect(map.Value.Scene.MapRoot, resolves);
        int clutter = 0;
        if (pkg.Entry("CLUTTER.ZAR") is { } c)
            clutter = SceneInstances.FromClutter(ClutterFile.Parse(c.Data), resolves).Count(i => i.Resolved);
        return (instances.Count(i => i.Kind == "model"), instances.Count(i => i.Kind == "reference" && i.Resolved), clutter);
    }

    [TestMethod]
    public void M51_ComposesWorldReferencesAndClutter()
    {
        var pkg = OpenMap("M51");
        Assert.AreEqual("m51", pkg.MapNamespace);
        var (model, reference, clutter) = Compose(pkg);
        Assert.AreEqual(1, model, "worldmodel only; prototypes are not placed");
        Assert.AreEqual(26, reference, "props, turret, car, sky");
        Assert.AreEqual(2078, clutter);
    }

    [TestMethod]
    public void MP6_ClutterAndReferences()
    {
        var (model, reference, clutter) = Compose(OpenMap("MP6"));
        Assert.AreEqual(1, model);
        Assert.AreEqual(133, reference);
        Assert.AreEqual(110, clutter);
    }

    [TestMethod]
    public void MP53_PlacesTurretsHelicopterAndSky()
    {
        var (model, reference, clutter) = Compose(OpenMap("MP53"));
        Assert.AreEqual(1, model);
        Assert.AreEqual(5, reference);
        Assert.AreEqual(0, clutter);
    }

    [TestMethod]
    public void Models_AreGroupedByKind()
    {
        var models = OpenMap("MP53").Models();
        Assert.IsTrue(models.Any(m => m.Kind == "weapon" && m.Name == "m4Acarbine_203"));
        Assert.IsTrue(models.Any(m => m.Kind == "character" && m.Name.StartsWith("MESH_seal")));
        Assert.IsTrue(models.Any(m => m.Kind == "world" && m.Name == "worldmodel"));
        Assert.IsTrue(models.Any(m => m.Kind == "sky"));
    }

    [TestMethod]
    public void Texture_UsesCbpPaletteWithRealAlpha()
    {
        // thintree1.tif: a conifer card, palette texpal_116 with alpha 0 / 0x80 entries.
        var pkg = OpenMap("M51");
        var decoded = pkg.DecodeTexture("thintree1.tif", "m51");
        Assert.IsNotNull(decoded);
        var tex = decoded.Value.Texture;
        Assert.AreEqual(116, tex.Header.ResolvePaletteId());
        Assert.IsTrue(tex.PaletteResolved);
        int clear = 0;
        for (int i = 3; i < tex.Rgba.Length; i += 4) if (tex.Rgba[i] == 0) clear++;
        Assert.IsTrue(clear > tex.Rgba.Length / 4 / 3, "transparent background around the tree");
    }

    [TestMethod]
    public void Clutter_WritePlacement_RoundTripsBothLayouts()
    {
        var pkg = OpenMap("M51");
        var container = ZedContainer.FromBytes(pkg.Entry("CLUTTER.ZAR")!.Data);
        var placements = ClutterFile.Parse(container);
        foreach (var layout in new[] { ClutterFile.Layout.Matrix, ClutterFile.Layout.Quaternion })
        {
            var p = placements.First(x => x.Layout == layout);
            float[] moved = (float[])p.Matrix.Clone();
            moved[12] += 10; moved[14] -= 5;
            ClutterFile.WritePlacement(container, p, moved);
            var again = ClutterFile.Parse(ZedContainer.FromBytes(container.ToBytes()));
            var q = again.Single(x => x.ParamsIndex == p.ParamsIndex);
            for (int k = 0; k < 16; k++) Assert.AreEqual(moved[k], q.Matrix[k], 1e-3f, $"{layout} float {k}");
        }
    }

    private static string RepoFile(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "CodeDesigner.sln"))) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(new[] { dir ?? "." }.Concat(parts).ToArray());
    }

    [TestMethod]
    public void Pack_Cdb_MatchesPythonWriter()
    {
        string folder = RepoFile("Mods", "SOCOM2", "packs", "mvips");
        if (!Directory.Exists(folder)) Assert.Inconclusive("packs/mvips not present");
        var files = new[] { "TXR", "PAL", "GEO", "MDL" }
            .Select(p => ($@"RUN\MP\MVIPS\CLIB_{p}.ZED",File.ReadAllBytes(Path.Combine(folder, $"CLIB_{p}.ZED")))).ToList();
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(folder, "MVIPS.CDB")), PackageSave.BuildCdb(files));
        Assert.ThrowsException<ArgumentException>(() => PackageSave.CheckPackName("charpack"));
    }

    private static ZipperPackage Apply(ZipperPackage pkg, System.Collections.Generic.Dictionary<string, byte[]> changed)
    {
        foreach (var (leaf, bytes) in changed) pkg.ReplaceEntry(leaf, bytes);
        return pkg;
    }

    [TestMethod]
    public void Clutter_AddAndDelete_KeepTheTreeConsistent()
    {
        var pkg = OpenMap("M51");
        int before = ClutterFile.Parse(pkg.Entry("CLUTTER.ZAR")!.Data).Count;
        Apply(pkg, PackageEdits.AddClutter(pkg, "oaktree_near", Matrix4.Compose(1000, -50, 700, 45, 1.2f)));
        var after = ClutterFile.Parse(pkg.Entry("CLUTTER.ZAR")!.Data);
        Assert.AreEqual(before + 1, after.Count);
        var added = after.Last(p => p.ModelName == "oaktree_near");
        Assert.AreEqual(1000, added.Matrix[12], 1e-3);
        Assert.AreEqual(1.2f, Matrix4.ScaleOf(added.Matrix), 1e-3);
        Assert.AreEqual(45, Matrix4.YawOf(added.Matrix), 1e-2);

        Apply(pkg, PackageEdits.DeleteClutter(pkg, after.IndexOf(added)));
        Assert.AreEqual(before, ClutterFile.Parse(pkg.Entry("CLUTTER.ZAR")!.Data).Count);
        Assert.AreEqual(2078, Compose(pkg).Clutter);
    }

    [TestMethod]
    public void Instance_DuplicateMoveDelete()
    {
        var pkg = OpenMap("MP53");
        var (ns, scene) = pkg.MapScene()!.Value;
        Func<string, bool> resolves = n => pkg.ResolveModel(n) != null;
        var turret = SceneInstances.Collect(scene.MapRoot, resolves).First(i => i.ModelName == "LMG_turret_ledge");
        var node = turret.Node!;
        float[] world = Matrix4.Compose(1500, 20, 1500, 90);
        float[] local = Matrix4.Multiply(Matrix4.InverseAffine(node.ParentWorldTransform), world);

        Apply(pkg, PackageEdits.DuplicateInstance(pkg, ns, node.Index, local, null));
        var after = SceneInstances.Collect(pkg.Scene(ns)!.MapRoot, resolves);
        Assert.AreEqual(4, after.Count(i => i.ModelName == "LMG_turret_ledge"));
        var copy = after.Single(i => i.ModelName == "LMG_turret_ledge" && Math.Abs(i.Matrix[12] - 1500) < 1e-2);
        Assert.AreEqual(20, copy.Matrix[13], 1e-2);

        Apply(pkg, PackageEdits.DeleteInstance(pkg, ns, copy.Node!.Index));
        Assert.AreEqual(3, SceneInstances.Collect(pkg.Scene(ns)!.MapRoot, resolves).Count(i => i.ModelName == "LMG_turret_ledge"));
    }

    [TestMethod]
    public void Texture_Replace_RoundTripsThroughThePalette()
    {
        var pkg = OpenMap("M51");
        var original = pkg.DecodeTexture("oaktrunk.tif", "m51")!.Value.Texture;
        int w = original.Header.Width, h = original.Header.Height;
        var red = new byte[w * h * 4];
        for (int i = 0; i < red.Length; i += 4) { red[i] = 200; red[i + 1] = 30; red[i + 2] = 20; red[i + 3] = 255; }
        Apply(pkg, PackageEdits.ReplaceTexture(pkg, "oaktrunk.tif", "m51", red, w, h));
        var again = pkg.DecodeTexture("oaktrunk.tif", "m51")!.Value.Texture;
        Assert.AreEqual(original.Header.ResolvePaletteId(), again.Header.ResolvePaletteId(), "keeps its palette id");
        Assert.AreEqual(200, again.Rgba[0], 8);
        Assert.AreEqual(30, again.Rgba[1], 8);
    }

    [TestMethod]
    public void Model_RetextureAndTint()
    {
        var pkg = OpenMap("M51");
        Apply(pkg, PackageEdits.RetextureModel(pkg, "M51_MDL.ZED", "oaktree_near", "oaktrunk.tif", "thintree1.tif", "N000_000"));
        var model = pkg.DecodeModel(pkg.FindModel("M51_MDL.ZED", "oaktree_near")!);
        Assert.IsTrue(model.Chunks.Where(c => c.VisualKey == "N000_000").All(c => c.TextureName == "thintree1.tif"));
        Assert.IsTrue(model.Chunks.Where(c => c.VisualKey == "N000_001").All(c => c.TextureName == "oaktree_leaves_high.tif"));

        byte r0 = model.Chunks[0].Vertices[0].R!.Value;
        Apply(pkg, PackageEdits.TintVertexColors(pkg, "M51_MDL.ZED", "oaktree_near", new[] { 0.5f, 1f, 1f }, null, null, null, null));
        var tinted = pkg.DecodeModel(pkg.FindModel("M51_MDL.ZED", "oaktree_near")!);
        Assert.AreEqual((int)Math.Round(r0 * 0.5f), (int)tinted.Chunks[0].Vertices[0].R!.Value, 1);
    }

    [TestMethod]
    public void Save_Zdb_WritesTheEditedEntry()
    {
        var pkg = OpenMap("MP53");
        var (ns, scene) = pkg.MapScene()!.Value;
        var heli = SceneInstances.Collect(scene.MapRoot, n => pkg.ResolveModel(n) != null).First(i => i.ModelName == "pavehawk");
        float[] local = Matrix4.Multiply(Matrix4.InverseAffine(heli.Node!.ParentWorldTransform), Matrix4.Compose(100, 0, 100));
        var changed = PackageEdits.SetInstanceTransform(pkg, ns, heli.Node.Index, local);
        Apply(pkg, changed);
        string output = Path.Combine(Path.GetTempPath(), $"mp53-edit-{Guid.NewGuid():N}.ZDB");
        try
        {
            PackageSave.Write(pkg, changed.Keys, output);
            var reopened = ZipperPackage.Open(output);
            var original = ZipperPackage.Open(Path.Combine(RunDir, "MP53.ZDB"));
            foreach (var e in original.Entries)
            {
                byte[] now = reopened.Entry(e.Leaf)!.Data;
                if (changed.ContainsKey(e.Leaf)) CollectionAssert.AreEqual(pkg.Entry(e.Leaf)!.Data, now, e.Leaf);
                else CollectionAssert.AreEqual(e.Data, now, $"{e.Leaf} untouched");
            }
            var moved = SceneInstances.Collect(reopened.MapScene()!.Value.Scene.MapRoot, n => reopened.ResolveModel(n) != null).First(i => i.ModelName == "pavehawk");
            Assert.AreEqual(100, moved.Matrix[12], 1e-3);
        }
        finally { File.Delete(output); File.Delete(output + ".bak"); }
    }

    [TestMethod]
    public void Mesh_MatchesGeoBoundsAndTriangulates()
    {
        var pkg = OpenMap("M51");
        var mesh = ModelMesh.Build(pkg, pkg.FindModel("M51_MDL.ZED", "oaktree_near")!);
        Assert.AreEqual(6, mesh.Chunks.Count);
        Assert.IsTrue(mesh.Chunks.All(c => c.Triangles.Length > 0 && c.Colors != null));
        var b = pkg.DefinitionNode(pkg.FindModel("M51_MDL.ZED", "oaktree_near")!)!.Bounds!.Value;
        for (int k = 0; k < 3; k++)
        {
            Assert.AreEqual(b.Min[k], mesh.BoundsMin[k], 2f, $"min {k}");
            Assert.AreEqual(b.Max[k], mesh.BoundsMax[k], 2f, $"max {k}");
        }
    }

    [TestMethod]
    public void Mesh_SetVertices_RoundTripsPositionUvAndColour()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("M51_MDL.ZED", "oaktree_near")!;
        var c = ModelMesh.Build(pkg, model).Chunks[0];
        float[] p = { c.Positions[0] + 5, c.Positions[1] + 7, c.Positions[2] - 3 };
        Apply(pkg, ModelMesh.SetVertices(pkg, "M51_MDL.ZED", "oaktree_near", c.GeometryOffset,
            new[] { new ModelMesh.VertexEdit(0, p, new[] { 0.25f, 0.75f }, new byte[] { 10, 20, 30, 128 }) }));
        var after = ModelMesh.Build(pkg, model).Chunks[0];
        for (int k = 0; k < 3; k++) Assert.AreEqual(p[k], after.Positions[k], 0.1f, $"position {k}");
        Assert.AreEqual(0.25f, after.Uvs[0], 1e-3f);
        Assert.AreEqual(0.75f, after.Uvs[1], 1e-3f);
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 128 }, after.Colors![..4]);
        for (int i = 3; i < c.Positions.Length; i++) Assert.AreEqual(c.Positions[i], after.Positions[i], 0.1f, "other vertices unchanged");
    }

    [TestMethod]
    public void Mesh_TransformVertices_MovesAndRescalesWhenNeeded()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("M51_MDL.ZED", "oaktree_near")!;
        var before = ModelMesh.Build(pkg, model);
        Apply(pkg, ModelMesh.TransformVertices(pkg, "M51_MDL.ZED", "oaktree_near", Matrix4.Compose(0, 25, 0), null, null));
        var moved = ModelMesh.Build(pkg, model);
        Assert.AreEqual(before.BoundsMin[1] + 25, moved.BoundsMin[1], 0.2f);

        // 40x larger no longer fits int16 at the original chunk scale: the chunks are rescaled.
        Apply(pkg, ModelMesh.TransformVertices(pkg, "M51_MDL.ZED", "oaktree_near", Matrix4.Compose(0, 0, 0, 0, 40), null, null));
        var big = ModelMesh.Build(pkg, model);
        Assert.AreEqual(moved.BoundsMax[0] * 40, big.BoundsMax[0], Math.Abs(moved.BoundsMax[0] * 40) * 0.01f + 1);
        Assert.IsTrue(big.Chunks.Any(c => c.Scale > before.Chunks[0].Scale), "chunk scale grew");
    }

    [TestMethod]
    public void Layout_ChunkReencode_IsByteIdenticalOnRetail()
    {
        int same = 0, differ = 0;
        string? first = null;
        foreach (string map in new[] { "M51", "MP53", "MP71" })
        {
            var pkg = OpenMap(map);
            foreach (var m in pkg.Models().Where(m => m.VisualCount > 0))
            {
                var container = pkg.Container(m.Archive);
                var layout = ModelLayout.Parse(container, m.NodeIndex);
                if (layout == null) continue;
                foreach (var g in layout.Visuals.SelectMany(v => v.Sections).SelectMany(s => s.Chunks).Select(c => c.Geometry).Distinct())
                {
                    byte[] again = ModelLayout.WriteChunk(ModelLayout.ReadChunk(g.Data));
                    if (again.AsSpan().SequenceEqual(g.Data)) same++;
                    else
                    {
                        differ++;
                        if (first == null)
                        {
                            int at = 0;
                            while (at < Math.Min(again.Length, g.Data.Length) && again[at] == g.Data[at]) at++;
                            int n = BitConverter.ToInt32(g.Data, 0x30), t = BitConverter.ToInt32(g.Data, 0x34);
                            first = $"{map} {m.Archive}:{m.Name} len {g.Data.Length} vs {again.Length}, nv {n} nt {t}, first diff at 0x{at:X}: " +
                                $"retail {Convert.ToHexString(g.Data, Math.Max(0, at - 8), Math.Min(24, g.Data.Length - Math.Max(0, at - 8)))} " +
                                $"ours {Convert.ToHexString(again, Math.Max(0, at - 8), Math.Min(24, again.Length - Math.Max(0, at - 8)))}";
                        }
                    }
                }
            }
        }
        Assert.AreEqual(0, differ, $"{same} identical, first difference: {first}");
    }

    [TestMethod]
    public void Layout_RewriteKeepsTheMesh()
    {
        var pkg = OpenMap("M51");
        foreach (string name in new[] { "oaktree_near", "crate_small", "room_table01", "worldmodel" })
        {
            var model = pkg.Models().First(m => m.Name == name);
            var before = ModelMesh.Build(pkg, model);
            var container = ZedContainer.FromBytes(pkg.Entry(model.Archive)!.Data);
            ModelLayout.Parse(container, model.NodeIndex)!.Write(container, model.NodeIndex);
            var copy = ZipperPackage.Open(Path.Combine(RunDir, "M51.ZDB"));
            copy.ReplaceEntry(model.Archive, container.ToBytes());
            var after = ModelMesh.Build(copy, copy.FindModel(model.Archive, name)!);
            Assert.AreEqual(before.Chunks.Count, after.Chunks.Count, name);
            for (int i = 0; i < before.Chunks.Count; i++)
            {
                CollectionAssert.AreEqual(before.Chunks[i].Positions, after.Chunks[i].Positions, $"{name} chunk {i}");
                CollectionAssert.AreEqual(before.Chunks[i].Triangles, after.Chunks[i].Triangles, $"{name} chunk {i}");
                CollectionAssert.AreEqual(before.Chunks[i].Colors, after.Chunks[i].Colors, $"{name} chunk {i}");
                Assert.AreEqual(before.Chunks[i].Texture, after.Chunks[i].Texture);
            }
        }
    }

    private static float[] Pos(ModelMesh.MeshChunk c, int i) => new[] { c.Positions[i * 3], c.Positions[i * 3 + 1], c.Positions[i * 3 + 2] };

    [TestMethod]
    public void Topology_DeleteFaces_RemovesTrianglesAndEmptyChunks()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("M51_MDL.ZED", "crate_small")!;
        var before = ModelMesh.Build(pkg, model);
        var c0 = before.Chunks[0];
        Apply(pkg, MeshTopology.DeleteFaces(pkg, "M51_MDL.ZED", "crate_small", new Dictionary<int, int[]> { [c0.GeometryOffset] = new[] { 0 } }).Changed);
        var after = ModelMesh.Build(pkg, model);
        Assert.AreEqual(before.Chunks.Count, after.Chunks.Count);
        Assert.AreEqual(c0.Triangles.Length - 3, after.Chunks[0].Triangles.Length);
        CollectionAssert.AreEqual(before.Chunks[1].Positions, after.Chunks[1].Positions, "other chunks untouched");

        var c1 = after.Chunks[1];
        Apply(pkg, MeshTopology.DeleteFaces(pkg, "M51_MDL.ZED", "crate_small", new Dictionary<int, int[]> { [c1.GeometryOffset] = Enumerable.Range(0, c1.Triangles.Length / 3).ToArray() }).Changed);
        // crate_small's visuals share their geometry blocks: the emptied chunk leaves every visual that drew it.
        int sharing = after.Chunks.Count(c => c.GeometryOffset == c1.GeometryOffset);
        Assert.AreEqual(before.Chunks.Count - sharing, ModelMesh.Build(pkg, model).Chunks.Count, "emptied chunk removed");
    }

    [TestMethod]
    public void Topology_AddCube_SameTextureAndNewSection()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("M51_MDL.ZED", "oaktree_near")!;
        int before = ModelMesh.Build(pkg, model).Chunks.Count;
        var r = MeshTopology.AddMesh(pkg, "M51_MDL.ZED", "oaktree_near", "N000_000", "oaktrunk.tif", MeshTopology.Primitive("cube", 20, new[] { 5f, -30f, 2f }), false);
        Apply(pkg, r.Changed);
        var mesh = ModelMesh.Build(pkg, model);
        Assert.AreEqual(before + 1, mesh.Chunks.Count);
        var cube = mesh.Chunks[r.NewChunks.Single()];
        Assert.AreEqual("oaktrunk.tif", cube.Texture);
        Assert.AreEqual(24, cube.VertexCount);
        Assert.AreEqual(12, cube.Triangles.Length / 3);
        for (int k = 0; k < 3; k++)
        {
            float lo = Enumerable.Range(0, 24).Min(i => Pos(cube, i)[k]), hi = Enumerable.Range(0, 24).Max(i => Pos(cube, i)[k]);
            Assert.AreEqual(new[] { 5f, -30f, 2f }[k] - 10, lo, 0.1f, $"min {k}");
            Assert.AreEqual(new[] { 5f, -30f, 2f }[k] + 10, hi, 0.1f, $"max {k}");
        }

        var r2 = MeshTopology.AddMesh(pkg, "M51_MDL.ZED", "oaktree_near", "N000_001", "thintree1.tif", MeshTopology.Primitive("plane", 40, new[] { 0f, 0f, 0f }), false);
        Apply(pkg, r2.Changed);
        var plane = ModelMesh.Build(pkg, model).Chunks[r2.NewChunks.Single()];
        Assert.AreEqual("thintree1.tif", plane.Texture, "new section with its own name field");
        Assert.AreEqual("N000_001", plane.Visual);
    }

    [TestMethod]
    public void Topology_DuplicateFaces_Translated()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("M51_MDL.ZED", "crate_small")!;
        var c0 = ModelMesh.Build(pkg, model).Chunks[0];
        var r = MeshTopology.DuplicateFaces(pkg, "M51_MDL.ZED", "crate_small", new Dictionary<int, int[]> { [c0.GeometryOffset] = new[] { 0, 1 } }, Matrix4.Compose(0, -10, 0));
        Apply(pkg, r.Changed);
        var copy = ModelMesh.Build(pkg, model).Chunks[r.NewChunks.Single()];
        Assert.AreEqual(2, copy.Triangles.Length / 3);
        for (int t = 0; t < 6; t++)
        {
            var p = Pos(copy, (int)copy.Triangles[t]);
            var q = Pos(c0, (int)c0.Triangles[t]);
            Assert.AreEqual(q[0], p[0], 0.05f); Assert.AreEqual(q[1] - 10, p[1], 0.05f); Assert.AreEqual(q[2], p[2], 0.05f);
        }
    }

    [TestMethod]
    public void Topology_ObjImport_KeepsYUpAndFans()
    {
        var mesh = MeshTopology.ParseObj("v 0 0 0\nv 1 0 0\nv 1 2 0\nv 0 2 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nf 1/1 2/2 3/3 4/4\n");
        Assert.AreEqual(2, mesh.Triangles.Count);
        Assert.AreEqual(2, mesh.Positions[2][1], "Y up file = model space");
        Assert.AreEqual(-2, MeshTopology.ParseObj("v 0 2 0\nv 1 2 0\nv 1 0 0\nf 1 2 3\n", yUp: false).Positions[0][1], "Y-down file is flipped");
        Assert.AreEqual(1, mesh.Uvs[2][1]);
    }

    [TestMethod]
    public void Topology_AddToWorldModel_FarFromOrigin()
    {
        var pkg = OpenMap("M51");
        var model = pkg.FindModel("WORL_MDL.ZED", "worldmodel")!;
        var visual = ModelMesh.Build(pkg, model).Chunks[0].Visual!;
        var r = MeshTopology.AddMesh(pkg, "WORL_MDL.ZED", "worldmodel", visual, "concrete_base.tif", MeshTopology.Primitive("plane", 200, new[] { 4000f, -50f, 3000f }), false);
        Apply(pkg, r.Changed);
        var plane = ModelMesh.Build(pkg, model).Chunks[r.NewChunks.Single()];
        Assert.AreEqual("Sixteenth", plane.Unit.ToString());
        Assert.AreEqual(3900, Enumerable.Range(0, 4).Min(i => Pos(plane, i)[0]), 0.2f);
        Assert.AreEqual(3100, Enumerable.Range(0, 4).Max(i => Pos(plane, i)[2]), 0.2f);
    }

    [TestMethod]
    public void MapPoints_ParseAndMove()
    {
        var pkg = OpenMap("M51");
        byte[] mps = pkg.Entry("AIMAPS.MPS")!.Data;
        var points = CodeDesigner.Ps2.FileFormats.Zipper.MPS.MapPoints.Parse(mps);
        var church = points.Single(p => p.Name == "Church1");
        Assert.AreEqual("start", church.Kind);
        Assert.AreEqual(258.39f, church.X, 0.05f);
        Assert.AreEqual(388.43f, church.Z, 0.05f);

        byte[] moved = CodeDesigner.Ps2.FileFormats.Zipper.MPS.MapPoints.Move(mps, "Church1", 1000, 900);
        var again = CodeDesigner.Ps2.FileFormats.Zipper.MPS.MapPoints.Parse(moved).Single(p => p.Name == "Church1");
        Assert.AreEqual(1000, again.X, 5f);
        Assert.AreEqual(900, again.Z, 5f);
        Assert.AreEqual(points.Count, CodeDesigner.Ps2.FileFormats.Zipper.MPS.MapPoints.Parse(moved).Count);
    }

    [TestMethod]
    public void Edits_ReturnNewIndices()
    {
        var pkg = OpenMap("M51");
        Apply(pkg, PackageEdits.AddClutter(pkg, "bush2_near", Matrix4.Compose(500, 0, 500), out int ci));
        var list = ClutterFile.Parse(pkg.Entry("CLUTTER.ZAR")!.Data);
        Assert.AreEqual("bush2_near", list[ci].ModelName);
        Assert.AreEqual(500, list[ci].Matrix[12], 1e-3);

        var (ns, scene) = pkg.MapScene()!.Value;
        var crate = SceneInstances.Collect(scene.MapRoot, n => pkg.ResolveModel(n) != null).First(i => i.ModelName == "crate_small");
        Apply(pkg, PackageEdits.DuplicateInstance(pkg, ns, crate.Node!.Index, null, null, out int gi));
        var copy = SceneInstances.Collect(pkg.Scene(ns)!.MapRoot, n => pkg.ResolveModel(n) != null).Single(i => i.Node!.Index == gi);
        Assert.AreEqual("crate_small", copy.ModelName);
    }

    [TestMethod]
    public void Character_SkinnedMeshMatchesPythonDecoder()
    {
        // Reference numbers from Mods/SOCOMCA/s2mesh.py (DMA chain groups, triangles) and charmesh.py (bind-pose
        // bounds) on the same records. charmesh's raw scan misses 2 of the seal's 74 chain groups.
        var pkg = OpenMap("M51");
        var cases = new[] { ("MESH_seal_A_woodland_LO", 74, 1980, 0.12f, 19.09f), ("MESH_alban_informant", 63, 1664, 0.12f, 19.63f) };
        foreach (var (name, groups, tris, minY, maxY) in cases)
        {
            var mesh = ModelMesh.Build(pkg, pkg.FindModel("CLIB_MDL.ZED", name)!);
            Assert.IsTrue(mesh.Skinned, name);
            Assert.AreEqual(groups, mesh.Chunks.Count, name + " groups");
            Assert.AreEqual(tris, mesh.Chunks.Sum(c => c.Triangles.Length / 3), name + " triangles");
            Assert.AreEqual(minY, mesh.BoundsMin[1], 0.02f, name + " feet (Y up)");
            Assert.AreEqual(maxY, mesh.BoundsMax[1], 0.02f, name + " head");
            Assert.IsTrue(mesh.Chunks.All(c => !string.IsNullOrEmpty(c.Texture)), name + " textures");
            Assert.IsTrue(mesh.Chunks.All(c => c.SkinBones!.Where((b, i) => i % ModelMesh.SkinInfluences == 0).All(b => b >= 0)), name + " every vertex has a bone");
        }
        var seal = ModelMesh.Build(pkg, pkg.FindModel("CLIB_MDL.ZED", "MESH_seal_A_woodland_LO")!);
        Assert.AreEqual("seal_A_woodland_LO", seal.Skeleton!.Bones[0].Name);
        Assert.IsTrue(seal.Skeleton.Bones.Any(b => b.Name == "hips" && b.MatrixId == 1));
        Assert.ThrowsException<NotSupportedException>(() => ModelMesh.SetVertices(pkg, "CLIB_MDL.ZED", "MESH_seal_A_woodland_LO", seal.Chunks[0].GeometryOffset, new[] { new ModelMesh.VertexEdit(0, new[] { 0f, 0f, 0f }) }));
    }

    [TestMethod]
    public void Animation_EveryClipRoundTripsByteForByte()
    {
        var pkg = OpenMap("M51");
        string? shared = PackageAnimations.SharedPath(pkg);
        Assert.IsNotNull(shared, "RUN/MOTION_P.ZAR next to the package");
        using var p = ZedContainer.Open(shared!);
        var clips = PackageAnimations.List(pkg, p.Nodes);
        Assert.AreEqual(59, clips.Count(c => c.Editable), "M51 MOTION_S clips");
        Assert.IsTrue(clips.Count(c => !c.Editable) >= 300, "shared clips");
        foreach (var c in clips)
        {
            var clip = MotionClip.Parse(c.Data);
            CollectionAssert.AreEqual(c.Data, clip.Encode(), $"{c.Source}:{c.Name}");
        }
        var stand = MotionClip.Parse(clips.First(c => c.Name == "seal_stand").Data);
        // Positions are 1/256 units: the calf's length equals the GEO bind skeleton's (4.33).
        Assert.AreEqual(4.33f, MotionClip.PositionUnits(stand.Find("rcalf")!.Positions[0])[0], 0.01f);
    }

    [TestMethod]
    public void Animation_AddRenameEditDelete()
    {
        var pkg = OpenMap("M51");
        using var p = ZedContainer.Open(PackageAnimations.SharedPath(pkg)!);
        var source = MotionClip.Parse(PackageAnimations.List(pkg, p.Nodes).First(c => c.Name == "seal_stand").Data);
        Apply(pkg, PackageAnimations.Add(pkg, "my_wave", source));
        Assert.IsTrue(PackageAnimations.List(pkg, null).Any(c => c.Name == "my_wave"));
        Assert.ThrowsException<ArgumentException>(() => PackageAnimations.Add(pkg, "my_wave", source));

        var edit = source.Clone();
        edit.Resample(30);
        var head = edit.Find("head")!;
        head.Rotations = Enumerable.Range(0, edit.Samples).Select(i => MotionClip.QuaternionKey(0, MathF.Sin(i * 0.05f), 0, MathF.Cos(i * 0.05f))).ToList();
        edit.Duration = 1f;
        Apply(pkg, PackageAnimations.Replace(pkg, "my_wave", edit));
        var back = MotionClip.Parse(PackageAnimations.List(pkg, null).First(c => c.Name == "my_wave").Data);
        Assert.AreEqual(31, back.Samples);
        Assert.AreEqual(MotionClip.KindAnimatedRotation, back.Find("head")!.Kind);
        Assert.AreEqual(1f, back.Duration);

        Apply(pkg, PackageAnimations.Rename(pkg, "my_wave", "my_wave2"));
        Apply(pkg, PackageAnimations.Delete(pkg, "my_wave2"));
        Assert.AreEqual(59, PackageAnimations.List(pkg, null).Count);
    }

    [TestMethod]
    public void WeaponFx_FireAnimationsResolveToEffectModels()
    {
        var pkg = OpenMap("M51");
        using var zw = ZedContainer.Open(Path.Combine(RunDir, "ZWEAPON.ZAR"));
        var defs = CodeDesigner.Ps2.FileFormats.Zipper.RDR.WeaponDefs.Parse(zw.Nodes.First(n => n.Name.EndsWith(".rdr")).Data);
        var m4 = defs.ForModel("m4Acarbine")!;
        Assert.AreEqual("muzzle_m4", m4.FireAnimName);
        Assert.AreEqual(0.12f, m4.FireWait, 1e-4f);

        var fx = ZanimFile.Parse(pkg.Entry("CZANIM.ZAR")!.Data);
        Assert.AreEqual(288, fx.Animations.Count);
        // Every track's instructions walk exactly to its end.
        foreach (var a in fx.Animations)
        {
            int consumed = a.Tracks.Sum(t => 28 + t.Instructions.Sum(i => i.Words.Length * 4));
            Assert.AreEqual(a.Sequence.Length, consumed, a.Name);
        }
        CollectionAssert.AreEqual(new[] { "shell_eject", "flash_fire_hider", "shell_smoke_med" }, fx.Find("muzzle_m4")!.Spawns.ToArray());
        var hider = fx.Find("flash_fire_hider")!;
        // Read from the game's opcode table / handlers: named ops, node refs, the scale curves and the WAIT.
        CollectionAssert.AreEqual(new[] { "NA", "muzzle_flash_hider", "rotate", "scale", "muzzle_light_most" }, hider.NodeRefs.Select(r => r.Name).ToArray());
        var scale = hider.Tracks.Single(t => t.Target == "fire_scale").Instructions;
        var ends = scale.Where(i => i.Name == "OBJECT_MOTION_FROM_TO").Select(i => ((float[])i.Fields["scaleEnd"])[0]).ToArray();
        CollectionAssert.AreEqual(new[] { 1.2f, 0.9f, 1.3f }, ends.Select(x => MathF.Round(x, 2)).ToArray());
        Assert.IsTrue(scale.All(i => i.Name != "OBJECT_MOTION_FROM_TO" || (string)i.Fields["object"] == "scale"));
        Assert.AreEqual(0.05f, (float)scale.First(i => i.Name == "OBJECT_MOTION_FROM_TO").Fields["duration"], 1e-4f);
        Assert.AreEqual(1, scale.Single(i => i.Name == "WAIT").Fields["frames"]);
        var rotate = hider.Tracks.Single(t => t.Target == "fire_rotate").Instructions;
        Assert.AreEqual(5, rotate.Count(i => i.Name == "RANDOM_WEIGHT" && i.Condition));
        Assert.IsTrue(fx.Animations.SelectMany(x => x.Tracks).SelectMany(t => t.Instructions).All(i => !i.Name.StartsWith("OP_") || i.Op >= 0x200), "every opcode below 0x200 is named");
        Assert.IsNotNull(pkg.ResolveModel("muzzle_flash_hider"));
        CollectionAssert.Contains(hider.Names, "muzzle_flash_hider");
        CollectionAssert.Contains(hider.Tracks.Select(t => t.Target).ToList(), "fire_rotate");
    }

    [TestMethod]
    public void Packs_CdbAndLooseZedOpenAsPackages()
    {
        // Packs written by build_charpack.py / rip_ca.py: <NAME>.CDB plus the loose CLIB_*.ZED beside it.
        string[] packs = { @"F:\SOCOM Packs\SOCOM2\m20001\M20001.CDB", @"F:\SOCOM Packs\SOCOMCA\packs\m30001\M30001.CDB" };
        if (!packs.All(File.Exists)) Assert.Inconclusive("Pack folders not available");
        foreach (string cdb in packs)
        {
            byte[] raw = File.ReadAllBytes(cdb);
            var archive = CodeDesigner.Ps2.FileFormats.Zipper.ZDB.ZdbArchive.FromBytes(raw);
            Assert.IsTrue(archive.IsMemoryCardPack, cdb);
            CollectionAssert.AreEqual(raw, archive.ToBytes(), cdb + " re-encodes byte for byte");

            foreach (string open in new[] { cdb, Path.Combine(Path.GetDirectoryName(cdb)!, "CLIB_MDL.ZED") })
            {
                var pkg = ZipperPackage.Open(open);
                var mesh = pkg.Models().First(m => m.Kind == "character" && m.Name.StartsWith("MESH_"));
                var built = ModelMesh.Build(pkg, mesh);
                Assert.IsTrue(built.Skinned && built.Chunks.Count > 0, $"{open}: {mesh.Name}");
                Assert.IsTrue(built.BoundsMax[1] > 15 && built.BoundsMin[1] > -1, $"{open}: {mesh.Name} stands Y up");
            }

            // Saving an edited pack writes a pack again (same layout, a .bak of the original).
            var cdbPkg = ZipperPackage.Open(cdb);
            string copy = Path.Combine(Path.GetTempPath(), "cdtest_" + Path.GetFileName(cdb));
            var written = PackageSave.Write(cdbPkg, new[] { "CLIB_MDL.ZED" }, copy);
            CollectionAssert.AreEqual(raw, File.ReadAllBytes(written[0]), "unchanged entries -> identical pack");
            File.Delete(copy);
        }
    }

    [TestMethod]
    public void Ca_CharactersWeaponsAndClips()
    {
        // Oasis (RUN/733981F0.ZAR); weapons come from RUN/GLOBAL.ZDB, clips from RUN/AUX_*ZAN.ZDB beside it.
        const string zar = @"H:\RUN\733981F0.ZAR";
        if (!File.Exists(zar)) Assert.Inconclusive("SOCOM CA disc not on H:");
        var pkg = CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaPackage.Open(zar);

        // Skinned character: triangle count of Mods/SOCOMCA ca2s2.ca_groups (the walk confirmed in game).
        var soldier = pkg.Models().First(m => m.Kind == "character" && m.Name == "clib_c54fb483");
        var mesh = pkg.BuildMesh(soldier);
        Assert.IsTrue(mesh.Skinned);
        Assert.AreEqual(26, mesh.Skeleton!.Bones.Count);
        Assert.AreEqual(1304, mesh.Chunks.Sum(c => c.Triangles.Length / 3));
        // CA stores V top-down and negated (-1..0): flipped into 0..1 (the head's top samples the hair rows).
        Assert.IsTrue(mesh.Chunks.SelectMany(c => c.Uvs.Where((_, i) => i % 2 == 1)).All(v => v >= -0.01f && v <= 1.01f));

        // Weapon: chunk-scale positions (an AK ~8.6 units long next to a 19-unit soldier).
        var ak = pkg.Models().Single(m => m.Kind == "weapon" && m.Name == "ak47");
        var gun = pkg.BuildMesh(ak);
        float length = gun.BoundsMax[0] - gun.BoundsMin[0];
        Assert.IsTrue(length is > 7 and < 10, $"ak47 is {length} units long");
        Assert.AreEqual(88, pkg.Models().Count(m => m.Kind == "weapon"));

        // Clips: seal_stand (6 s, 25 tracks; hips at 11.5 units in 1/256 keys).
        var stand = pkg.Animations().Single(c => c.Name == "seal_stand");
        Assert.AreEqual(6f, stand.Duration, 1e-3f);
        Assert.AreEqual(25, stand.Tracks.Count);
        var hips = stand.Tracks.Single(t => t.Bone == 0x7EE3A92F);
        Assert.AreEqual(11.5f, CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaAnimations.SamplePositions(hips, 181)[0][1], 0.05f);
        Assert.IsTrue(pkg.Animations().Count > 400);
    }

    [TestMethod]
    public void Convert_CaToSocom2MatchesTheInGameConverter()
    {
        // The AK pack Mods/SOCOMCA/convert_weapons.py built (mcw02, confirmed in game): the C# port writes the same bytes.
        const string zar = @"H:\RUN\733981F0.ZAR", run = @"G:\RUN", python = @"F:\SOCOM Packs\SOCOMCA\weapons\mcw02";
        if (!File.Exists(zar) || !Directory.Exists(run) || !Directory.Exists(python)) Assert.Inconclusive("CA disc (H:), SOCOM II RUN (G:) or the Python packs (F:) missing");
        var pkg = CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaPackage.Open(zar);
        var templates = new CodeDesigner.Ps2.FileFormats.Zipper.Conversion.Socom2Templates(run);
        string dir = Path.Combine(Path.GetTempPath(), "cd_ca2s2");
        var ak = pkg.Models().Single(m => m.Kind == "weapon" && m.Name == "ak47");
        var r = CodeDesigner.Ps2.FileFormats.Zipper.Conversion.CaToSocom2.Export(pkg, ak, templates,
            new CodeDesigner.Ps2.FileFormats.Zipper.Conversion.CaToSocom2.Options("mcw02", "ca_ak47", dir));
        foreach (string part in new[] { "TXR", "PAL", "GEO", "MDL" })
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(python, $"WEAP_{part}.ZED")), File.ReadAllBytes(Path.Combine(dir, "mcw02", $"WEAP_{part}.ZED")), $"WEAP_{part}");

        // A character with clips: opens as a SOCOM II package with the same mesh and its MOTION_S.ZAR clips.
        var soldier = pkg.Models().First(m => m.Kind == "character" && m.Name == "clib_c54fb483");
        var c = CodeDesigner.Ps2.FileFormats.Zipper.Conversion.CaToSocom2.Export(pkg, soldier, templates,
            new CodeDesigner.Ps2.FileFormats.Zipper.Conversion.CaToSocom2.Options("mcsold", "ca_c54fb483", dir, new[] { "seal_stand", "seal_healing" }));
        var s2 = ZipperPackage.Open(c.CdbPath);
        var mesh = ModelMesh.Build(s2, s2.Models().Single(m => m.Name == "MESH_ca_c54fb483"));
        Assert.AreEqual(1304, mesh.Chunks.Sum(x => x.Triangles.Length / 3));
        var clips = PackageAnimations.List(s2, null).Select(x => x.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "seal_stand", "seal_healing" }, clips);
    }

    [TestMethod]
    public void Convert_Socom2ToCaRoundTripsTheMesh()
    {
        const string run = @"G:\RUN";
        if (!File.Exists(Path.Combine(run, "MP52.ZDB"))) Assert.Inconclusive("SOCOM II RUN (G:) missing");
        string dir = Path.Combine(Path.GetTempPath(), "cd_s2ca");
        foreach (var (zdb, name, clips) in new[] { ("MP52", "MESH_alg_UN01", new[] { "seal_stand" }), ("MP61", "humvee", Array.Empty<string>()) })
        {
            var pkg = ZipperPackage.Open(Path.Combine(run, zdb + ".ZDB"));
            var model = pkg.Models().First(m => m.Name == name);
            var source = ModelMesh.Build(pkg, model);
            var r = CodeDesigner.Ps2.FileFormats.Zipper.Conversion.Socom2ToCa.Export(pkg, model,
                new CodeDesigner.Ps2.FileFormats.Zipper.Conversion.Socom2ToCa.Options(name.Replace("MESH_", ""), dir, clips));
            var ca = CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaPackage.Open(r.Path);
            var back = ca.BuildMesh(ca.Models().Single(m => m.Name == r.Model));
            Assert.AreEqual(source.Chunks.Sum(x => x.Triangles.Length / 3), back.Chunks.Sum(x => x.Triangles.Length / 3), name);
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(source.BoundsMin[k], back.BoundsMin[k], 0.01f, $"{name} min[{k}]");
                Assert.AreEqual(source.BoundsMax[k], back.BoundsMax[k], 0.01f, $"{name} max[{k}]");
            }
            CollectionAssert.AreEquivalent(clips, ca.Animations().Select(x => x.Name).ToList(), name);
        }
    }

    private static string CaLibraryName(uint hash) => CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaLibrary.TextureName(hash);

    [TestMethod]
    public void Ca_CompoundWorldDecodes()
    {
        // SOCOM: Combined Assault RUN/9EA6A33E.ZAR = COMPOUND (H: holds the disc). Numbers from Mods/SOCOMCA castatic.py.
        const string zar = @"H:\RUN\9EA6A33E.ZAR";
        if (!File.Exists(zar)) Assert.Inconclusive("SOCOM CA disc not on H:");
        Assert.IsTrue(CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaPackage.IsCaPath(zar));
        var pkg = CodeDesigner.Ps2.FileFormats.Zipper.Ca.CaPackage.Open(zar);
        var world = pkg.Models().Single(m => m.Kind == "world" && m.Name == "com_i1");
        Assert.AreEqual(1114, world.Nodes);
        // A node's several objects are separate pieces (terrain + road overlay, trunk + canopy), all drawn: drawing
        // only the largest left holes in the terrain (Oasis). None of COMPOUND's are LOD copies (351,058 triangles).
        var mesh = pkg.BuildMesh(world);
        Assert.AreEqual(351058, mesh.Chunks.Sum(c => c.Triangles.Length / 3));
        Assert.IsTrue(mesh.Chunks.All(c => c.Texture != null), "every chunk has a material texture (kind 0x106 tags too)");
        var scene = pkg.MapScene();
        Assert.IsTrue(pkg.Models().Any(m => m.Kind == "ui" && m.Name == "RoundComplete"), "UI cards are not worlds");
        Assert.AreEqual(351058, scene.Where(p => p.World == "com_i1").Sum(p => pkg.ObjectMesh(p.LibraryIndex, p.Object).Chunks.Sum(c => c.Triangles.Length / 3)));
        var (_, isle) = pkg.Libraries.First(l => l.Res.Tag == "ISLE" && l.Res.Name == "com_i1.src");
        Assert.AreEqual(134, isle.Textures.Count);
        Assert.AreEqual(isle.Textures.Count, isle.Textures.Count(t => pkg.FindTexture(CaLibraryName(isle.U32(t + 0x4C))) != null), "every island texture resolves");
        Assert.IsTrue(mesh.Chunks.All(c => c.Texture == null || pkg.FindTexture(c.Texture) != null), "chunk textures resolve");
        // 4-bit textures: CA keeps 16-colour palettes in CSM1 slots 0-7 / 16-23. Read linearly, colours 8-15 come out
        // black (the stone wall ca_eacd9572 was mostly black noise).
        var wall = pkg.FindTexture("ca_eacd9572")!;
        Assert.AreEqual(0x14, wall.Psm);
        int dark = Enumerable.Range(0, wall.Width * wall.Height).Count(i => wall.Rgba[i * 4] + wall.Rgba[i * 4 + 1] + wall.Rgba[i * 4 + 2] < 30);
        Assert.IsTrue(dark < wall.Width * wall.Height / 20, $"{dark} near-black pixels in a stone wall");
    }
}
