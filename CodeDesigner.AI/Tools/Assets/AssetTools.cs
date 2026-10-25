using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Assets;

// SOCOM II asset editing (maps, models, textures) through bridge asset sessions (ZipperAssetService). These are
// the same operations the Model / Map editor webviews use, on the same sessions: an agent and a person with an
// editor open on one package see each other's edits (the editor polls the session revision).

[CdTool("assets_open", ToolDomain.Media, ToolRisk.ReadOnly,
    "Open a SOCOM II map package (G:\\RUN\\M51.ZDB, or a folder with its unpacked files) as an asset session, or " +
    "join the session already open on it. Returns the session id and a summary (models by kind, changed archives). " +
    "Start here before any other assets_* tool. Risk: read-only.")]
public sealed class AssetsOpenTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ZDB package or its unpacked folder.")] string path,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_OPEN", InputPath = path }, ct));
}

[CdTool("assets_sessions", ToolDomain.Media, ToolRisk.ReadOnly,
    "List open asset sessions (including ones a person opened in an editor) with revision and unsaved archives. " +
    "Risk: read-only.")]
public sealed class AssetsSessionsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_SESSIONS" }, ct));
}

[CdTool("assets_list_models", ToolDomain.Media, ToolRisk.ReadOnly,
    "List models in a session: archive, name, kind (prop, weapon, character, world, sky, library), visual count. " +
    "Filter by kind, name substring or archive (M51_MDL.ZED). Risk: read-only.")]
public sealed class AssetsListModelsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id from assets_open.")] string session,
        [Description("Optional kind: prop, weapon, character, world, sky, library.")] string kind = "",
        [Description("Optional name substring.")] string query = "",
        [Description("Optional archive leaf, e.g. M51_MDL.ZED.")] string archive = "",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_LIST_MODELS",
            AssetSession = session,
            AssetJson = JsonSerializer.Serialize(new { kind = Opt(kind), query = Opt(query), archive = Opt(archive) }),
        }, ct));

    internal static string? Opt(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

[CdTool("assets_describe_model", ToolDomain.Media, ToolRisk.ReadOnly,
    "Describe one model: visuals (N<node>_<visual>), chunks (texture, vertex/triangle counts, vertex colours), each " +
    "texture (size, format, palette id, transparency, alpha test) and its GEO definition node (transform, bounds, " +
    "position unit, children). Use before retexture / tintVertexColors edits. Risk: read-only.")]
public sealed class AssetsDescribeModelTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Model name (exact, or a scene/clutter name that resolves to one).")] string model,
        [Description("Optional archive leaf to disambiguate.")] string archive = "",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_DESCRIBE_MODEL",
            AssetSession = session,
            AssetName = model,
            AssetArchive = AssetsListModelsTool.Opt(archive),
        }, ct));
}

[CdTool("assets_texture", ToolDomain.Media, ToolRisk.ReadOnly,
    "Decode a texture (.tif) with the palette the game binds (TEX0.CBP) and return its info plus a base64 PNG with " +
    "real alpha. Risk: read-only.")]
public sealed class AssetsTextureTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Texture name, e.g. oaktrunk.tif.")] string texture,
        [Description("Optional TXR archive leaf or namespace to prefer (M51_TXR.ZED or m51).")] string archive = "",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_TEXTURE",
            AssetSession = session,
            AssetName = texture,
            AssetArchive = AssetsListModelsTool.Opt(archive),
        }, ct));
}

[CdTool("assets_map_scene", ToolDomain.Media, ToolRisk.ReadOnly,
    "List what the map places: kind (model = worldmodel, reference = a placed prop/turret/vehicle/sky, clutter = " +
    "trees/bushes/grass/rocks from CLUTTER.ZAR), model, position, scale, matrix, and the geoIndex / clutterIndex " +
    "that edit ops take. Filter by kind or name/path substring; limit defaults to 500. Risk: read-only.")]
public sealed class AssetsMapSceneTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Optional kind: model, reference, visuals, clutter.")] string kind = "",
        [Description("Optional model name or scene path substring.")] string query = "",
        [Description("Maximum instances to return (default 500).")] int limit = 500,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_MAP_SCENE",
            AssetSession = session,
            AssetJson = JsonSerializer.Serialize(new { kind = AssetsListModelsTool.Opt(kind), query = AssetsListModelsTool.Opt(query), limit }),
        }, ct));
}

[CdTool("assets_model_mesh", ToolDomain.Media, ToolRisk.ReadOnly,
    "The editable mesh of a model in model space, the geometry the Model Editor shows: per chunk its geometryOffset " +
    "(the id setVertices / transformVertices take), visual, texture, vertex/triangle counts, unit and placement. " +
    "With arrays=true also base64 float32 positions/uvs/normals, RGBA colours (128 = 1.0) and uint32 triangles. " +
    "Skinned characters (MESH_* in CLIB_MDL) come in bind pose (Y up, feet at 0) with a skeleton [{name, matrixId, " +
    "parent, local, world}] and per vertex skinInfluences slots: skinBones int32 (skeleton index, -1 unused), " +
    "skinWeights, skinPositions / skinNormals (bone-local); their vertices are view-only for now. " +
    "Risk: read-only.")]
public sealed class AssetsModelMeshTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Archive leaf, e.g. M51_MDL.ZED.")] string archive,
        [Description("Model name.")] string model,
        [Description("Include the vertex arrays (large).")] bool arrays = false,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_MODEL_MESH",
            AssetSession = session,
            AssetArchive = archive,
            AssetName = model,
            AssetJson = JsonSerializer.Serialize(new { arrays }),
        }, ct));
}

[CdTool("assets_map_points", ToolDomain.Media, ToolRisk.ReadOnly,
    "The map's named points from AIMAPS.MPS (player / hostage starts, waypoints) with world X / Z and the AI grid. " +
    "Move one with assets_edit movePoint. Risk: read-only.")]
public sealed class AssetsMapPointsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_MAP_POINTS", AssetSession = session }, ct));
}

[CdTool("assets_list_animations", ToolDomain.Media, ToolRisk.ReadOnly,
    "Character animation clips: the package's own MOTION_S.ZAR (editable) and the shared RUN/MOTION_P.ZAR " +
    "(read-only; copy one in with assets_edit addAnimation {from}). Each: name, source, editable, duration (s), " +
    "frames (intervals; samples = frames + 1), fps, bones. Risk: read-only.")]
public sealed class AssetsListAnimationsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Optional substring filter on the clip name (e.g. seal_, guard_walk).")] string? query = null,
        [Description("Optional source: MOTION_S.ZAR (package) or MOTION_P.ZAR (shared).")] string? source = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_LIST_ANIMATIONS",
            AssetSession = session,
            AssetJson = JsonSerializer.Serialize(new { query, source }),
        }, ct));
}

[CdTool("assets_animation", ToolDomain.Media, ToolRisk.ReadOnly,
    "One animation clip's tracks: per bone (names match the character skeleton from assets_model_mesh) its kind and " +
    "positions [[x,y,z]] (local translation, units) and rotations [[x,y,z,w]] (local unit quaternions under the " +
    "skeleton parent), one entry when static or one per sample (frames + 1, evenly over the duration). Extra bones " +
    "rifle / pistol / launcher / weapon sit under the hand (weapon attachment). Risk: read-only.")]
public sealed class AssetsAnimationTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Clip name, e.g. seal_stand.")] string name,
        [Description("Optional source (MOTION_S.ZAR or MOTION_P.ZAR); default: the package's clip, else the shared one.")] string? source = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_ANIMATION",
            AssetSession = session,
            AssetName = name,
            AssetArchive = source,
        }, ct));
}

[CdTool("assets_weapons", ToolDomain.Media, ToolRisk.ReadOnly,
    "Weapons whose model is in the package (RUN/ZWEAPON.ZAR zweapon.rdr): internal name, model, the ZANIM effect " +
    "played when fired (fireAnimName, e.g. muzzle_m4) and fireWait (s between shots). Risk: read-only.")]
public sealed class AssetsWeaponsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_WEAPONS", AssetSession = session }, ct));
}

[CdTool("assets_weapon_fx", ToolDomain.Media, ToolRisk.ReadOnly,
    "What firing a weapon plays: its zweapon.rdr record (all scalar fields) and the CZANIM.ZAR effect tree from " +
    "FireAnimName: each effect's names, the package models it uses (muzzle_flash_* / bullet_shell_* in EFFE_MDL), " +
    "textures, tracks (target + instructions: opcode, block depth, words as floats) and the effects it spawns " +
    "(opcode 0x2D). Weapon models carry firepoint / firepoint_shell / aimpoint locators (assets_model_mesh " +
    "locators) where these spawn. Risk: read-only.")]
public sealed class AssetsWeaponFxTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Weapon model (m4Acarbine) or internal name (M4A1).")] string weapon,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_WEAPON_FX", AssetSession = session, AssetName = weapon }, ct));
}

[CdTool("assets_effects", ToolDomain.Media, ToolRisk.ReadOnly,
    "Every ZANIM effect script in the package's CZANIM.ZAR (muzzle flashes, shells, smoke, explosions, sounds, " +
    "camera shakes ...): name, its tracks, the effects it calls (CALL_ANIMATION), the package models it uses and the " +
    "opcodes it contains (official names from the game's opcode table). Risk: read-only.")]
public sealed class AssetsEffectsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Optional substring filter on the effect name (e.g. muzzle_, grenade, flash_fire).")] string? query = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_EFFECTS",
            AssetSession = session,
            AssetJson = JsonSerializer.Serialize(new { query }),
        }, ct));
}

[CdTool("assets_effect", ToolDomain.Media, ToolRisk.ReadOnly,
    "One ZANIM effect script fully decoded: names, node refs (its objects: models, model nodes, lights), and per track " +
    "(tracks run in parallel) the instructions with official names, nesting depth, whether they are IF / ELSEIF " +
    "conditions, decoded fields (e.g. WAIT frames/seconds, RANDOM_WEIGHT weight, OBJECT_ROTATE_STATE object/reference/" +
    "euler, OBJECT_MOTION_FROM_TO duration + scaleStart/End/Rate, LIGHT colour/range/time, CALL_ANIMATION animation) " +
    "and raw words. Objects: self = the effect's object, owner = what started it (a weapon's firepoint). " +
    "Risk: read-only.")]
public sealed class AssetsEffectTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Effect name, e.g. flash_fire_hider or muzzle_m4.")] string name,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_EFFECT", AssetSession = session, AssetName = name }, ct));
}

[CdTool("assets_edit", ToolDomain.Media, ToolRisk.MutatesDatabase,
    "Apply one edit to a session (undoable, not written to disk until assets_save). opJson is an object with \"op\":\n" +
    "- moveClutter {clutterIndex, position?, yaw?, scale?, matrix?}\n" +
    "- addClutter {model, position, yaw?, scale?} (model = a prop such as oaktree_near, bush2_near)\n" +
    "- deleteClutter {clutterIndex}\n" +
    "- moveInstance {geoIndex, position?, yaw?, scale?, matrix?} (world space; scene defaults to the map scene)\n" +
    "- duplicateInstance {geoIndex, position?, yaw?, scale?, name?}\n" +
    "- deleteInstance {geoIndex}\n" +
    "- replaceTexture {texture, pngBase64 | imagePath, namespace?} (resampled to the texture's size)\n" +
    "- retexture {archive, model, from, to, visual?} (point a model's sections at another .tif)\n" +
    "- tintVertexColors {archive, model, multiply:[r,g,b], add?:[r,g,b], visual?, texture?, alpha?}\n" +
    "- transformVertices {archive, model, translate?:[x,y,z], rotate?:[x,y,z] degrees, scale?: n | [x,y,z], pivot?, " +
    "chunks?:[geometryOffset], vertices?:{geometryOffset:[i...]}, matrix?} (model space; default pivot = centre of the affected vertices)\n" +
    "- setVertices {archive, model, geometryOffset, vertices:[{i, position?, uv?, color?:[r,g,b,a 0..255, 128=1.0], normal?}]} " +
    "or {archive, model, chunks:[{geometryOffset, vertices:[...]}]} (model space, as assets_model_mesh returns)\n" +
    "- batch {label?, ops:[{op...}, ...]} (several edits as one undo step; answers each op's result)\n" +
    "- movePoint {name, position:[x,y,z]} (an AIMAPS point; snaps to its AI grid cell)\n" +
    "- addAnimation {name, from?: clip (default seal_stand), fromSource?, frames?, duration?, after?} (a new package " +
    "clip copied from any clip, optionally resampled to `frames` intervals / given a duration in s; answers {result, name})\n" +
    "- deleteAnimation {name} / renameAnimation {name, newName} (package clips only)\n" +
    "- setAnimation {name, frames?, duration?, bones?:[{name, positions?:[[x,y,z]], rotations?:[[x,y,z,w]]}]} " +
    "(replace tracks: 1 key = static or one per sample, as assets_animation returns; frames resamples first)\n" +
    "- setAnimationKey {name, bone, frame | allFrames:true, rotation?:[x,y,z,w], position?:[x,y,z]} (one key; a static " +
    "track becomes animated)\n" +
    "- addClutter answers {result, newClutterIndex}; duplicateInstance answers {result, newGeoIndex}\n" +
    "- deleteFaces {archive, model, faces:{geometryOffset:[triangle...]}} (unused vertices and emptied chunks go too)\n" +
    "- duplicateFaces {archive, model, faces:{...}, translate?, rotate?, scale?, matrix?} (copies into new chunks)\n" +
    "- addMesh {archive, model, visual:\"N000_000\", texture:\"x.tif\", replace?, and one of: obj:\"<OBJ text>\" (Y up, " +
    "yUp?, scale?, offset?) | primitive:{kind:\"cube\"|\"plane\", size, center:[x,y,z]} | mesh:{positions:[[x,y,z]], " +
    "triangles:[[a,b,c]], normals?, uvs?, colors?}} (model space; a new texture gets its own section). " +
    "Topology ops answer {result, newChunks} with the new chunks' indices in assets_model_mesh order.\n" +
    "Positions are map units [x, y, z] (y is height; the ground is around y = -100..300 depending on map; read nearby " +
    "instances with assets_map_scene first). yaw is degrees about Y; omitted parts keep current values. " +
    "Indices come from assets_map_scene and shift after add/delete, so re-read before chaining. " +
    "Risk: changes the in-memory session only.")]
public sealed class AssetsEditTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("The edit as a JSON object with \"op\" (see the tool description).")] string opJson,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_EDIT", AssetSession = session, AssetJson = opJson }, ct));
}

[CdTool("assets_undo", ToolDomain.Media, ToolRisk.MutatesDatabase,
    "Undo the last edit in a session (or redo with redo=true). Risk: changes the in-memory session only.")]
public sealed class AssetsUndoTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Redo instead of undo.")] bool redo = false,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = redo ? "ASSET_REDO" : "ASSET_UNDO", AssetSession = session }, ct));
}

[CdTool("assets_save", ToolDomain.Media, ToolRisk.WritesDisk,
    "Write a session's changed archives. mode=zdb writes the package to outputPath (default: over the source, " +
    "keeping a one-time .bak); mode=pack writes a memory-card pack <NAME>.CDB (packName must start with 'm', max 7 " +
    "characters) to outputPath and, with folderCard (a PCSX2 folder memory card), installs it as " +
    "BASCUS-97275<NAME>/<NAME>.CDB. Prefer writing a copy (outputPath) over the retail file. Risk: writes disk.")]
public sealed class AssetsSaveTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("zdb or pack.")] string mode = "zdb",
        [Description("zdb: output .ZDB path (default over the source). pack: output folder.")] string outputPath = "",
        [Description("pack: name, e.g. mfrst.")] string packName = "",
        [Description("pack: optional PCSX2 folder memory card to install into.")] string folderCard = "",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_SAVE",
            AssetSession = session,
            AssetJson = JsonSerializer.Serialize(new
            {
                mode,
                outputPath = AssetsListModelsTool.Opt(outputPath),
                packName = AssetsListModelsTool.Opt(packName),
                folderCard = AssetsListModelsTool.Opt(folderCard),
            }),
        }, ct));
}

[CdTool("assets_export", ToolDomain.Media, ToolRisk.WritesDisk,
    "Convert a model to the other game. From a SOCOM 3 / Combined Assault session: a character, weapon, vehicle or " +
    "prop becomes a SOCOM II pack <outputDir>/<pack>/<PACK>.CDB (+ its ZEDs; the same bytes as the in-game-confirmed " +
    "Mods/SOCOMCA converters), optionally with CA clips retargeted into its MOTION_S.ZAR. Templates come from SOCOM II's " +
    "RUN folder (socom2Run, else found on any drive). Answers {path, pack, model, kind, library, triangles, textures, " +
    "palettes, clips, warnings, open:{archive, name}} - open the path with assets_open to see it. From a SOCOM II " +
    "session: the model as a SOCOM 3 / CA package. From a GTA: San Andreas session: a ped (with clips as " +
    "'<source>/<name>', e.g. ped.ifp/WALK_civi, retargeted onto alg_UN01), weapon, vehicle or object becomes a SOCOM II " +
    "pack (geometry encoded from triangles; vehicles as static props). Risk: writes disk.")]
public sealed class AssetsExportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        [Description("Model archive (as assets_list_models answers).")] string archive,
        [Description("Model name.")] string name,
        [Description("Pack name: 'm' + up to 6 letters/digits (default derived from the model).")] string pack = "",
        [Description("Model name in the converted package (default ca_<name> / gta_<name>).")] string modelName = "",
        [Description("Output folder (default Documents/CodeDesigner/Exports).")] string outputDir = "",
        [Description("Comma-separated clip names to retarget into the pack (characters; see assets_list_animations).")] string clips = "",
        [Description("SOCOM II RUN folder holding MP52.ZDB / MOTION_P.ZAR (default: searched).")] string socom2Run = "",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ASSET_EXPORT",
            AssetSession = session,
            AssetArchive = archive,
            AssetName = name,
            AssetJson = JsonSerializer.Serialize(new
            {
                pack = AssetsListModelsTool.Opt(pack),
                modelName = AssetsListModelsTool.Opt(modelName),
                outputDir = AssetsListModelsTool.Opt(outputDir),
                clips = string.IsNullOrWhiteSpace(clips) ? null : clips.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                socom2Run = AssetsListModelsTool.Opt(socom2Run),
            }),
        }, ct));
}

[CdTool("assets_close", ToolDomain.Media, ToolRisk.ReadOnly,
    "Close an asset session, discarding unsaved edits. Risk: read-only (drops in-memory edits).")]
public sealed class AssetsCloseTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Session id.")] string session,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_CLOSE", AssetSession = session }, ct));
}
