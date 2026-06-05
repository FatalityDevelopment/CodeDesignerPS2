using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Weapons;

// AI-driven PS2 weapon generation: pick an existing in-game weapon as a
// template, regenerate its texture from a user image, optionally tweak the
// mesh, and write the result back into the archive as a new entry. Backed
// by ZipperWeaponsService on the bridge.

[CdTool("weapons_list", ToolDomain.Media, ToolRisk.ReadOnly,
    "List every weapon entry in a ZED archive with vertex/triangle counts and bound texture names. " +
    "Use when picking a template that visually resembles a target image. Risk: read-only.")]
public sealed class WeaponsListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the WEAP_MDL.ZED (or similar) archive.")] string archivePath,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_LIST_WEAPONS",
            InputPath = archivePath,
        }, ct));
}

[CdTool("weapons_describe_template", ToolDomain.Media, ToolRisk.ReadOnly,
    "Get a structured DTO for one template weapon: per-chunk vertex/triangle/scale data, vertex " +
    "bounds, texture metadata, and a short explainer of the .mdl/.tif binary layout. Risk: read-only.")]
public sealed class WeaponsDescribeTemplateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the archive.")] string archivePath,
        [Description("Name of the weapon entry inside the archive.")] string weaponName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_GET_WEAPON_TEMPLATE",
            InputPath = archivePath,
            WeaponName = weaponName,
        }, ct));
}

[CdTool("weapons_thumbnail", ToolDomain.Media, ToolRisk.ReadOnly,
    "Render a template weapon's bound texture as a base64 PNG. Use to visually compare templates " +
    "against a user-supplied weapon image. Risk: read-only.")]
public sealed class WeaponsThumbnailTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the archive.")] string archivePath,
        [Description("Name of the weapon entry.")] string weaponName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_WEAPON_TEXTURE_PNG",
            InputPath = archivePath,
            WeaponName = weaponName,
        }, ct));
}

[CdTool("weapons_encode_texture", ToolDomain.Media, ToolRisk.ReadOnly,
    "Resample a PNG image into the chosen template weapon's TIM format (matching dimensions, bpp, " +
    "CLUT layout) and return the encoded .tif bytes as base64. Provide EITHER imagePath OR " +
    "imageBase64 — for an image attached in chat, base64-encode it and pass via imageBase64. " +
    "Use before weapons_create to preview the texture without committing. Risk: read-only.")]
public sealed class WeaponsEncodeTextureTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the archive containing the template.")] string archivePath,
        [Description("Name of the template weapon entry.")] string templateName,
        [Description("Absolute path to a PNG image on disk. Leave empty to use imageBase64.")] string imagePath = "",
        [Description("Base64-encoded PNG bytes. Use this for chat-attached images. Leave empty to use imagePath.")] string imageBase64 = "",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(imageBase64))
            return CdText.Message("weapons_encode_texture: provide imagePath OR imageBase64.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_ENCODE_WEAPON_TEXTURE",
            InputPath = archivePath,
            WeaponName = templateName,
            WeaponImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath,
            WeaponImageData = string.IsNullOrWhiteSpace(imageBase64) ? null : imageBase64,
        }, ct));
    }
}

[CdTool("weapons_encode_model", ToolDomain.Media, ToolRisk.ReadOnly,
    "Re-encode a template weapon's .mdl with optional per-chunk vertex transforms and texture-name " +
    "rebinds. Returns base64 model bytes. Use to preview mesh edits before weapons_create. " +
    "Edits JSON shape: { \"perChunk\": [{ \"chunkIndex\": 0, \"transform\": { \"scaleX\": 1.2, " +
    "\"translateY\": 100 }, \"newTextureName\": \"new.tif\" }] }. Risk: read-only.")]
public sealed class WeaponsEncodeModelTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the archive containing the template.")] string archivePath,
        [Description("Name of the template weapon entry.")] string templateName,
        [Description("JSON describing edits to apply. Pass empty string for an identity round-trip.")] string editsJson,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_ENCODE_WEAPON_MODEL",
            InputPath = archivePath,
            WeaponName = templateName,
            WeaponMeshEditsJson = string.IsNullOrWhiteSpace(editsJson) ? null : editsJson,
        }, ct));
}

[CdTool("weapons_create", ToolDomain.Media, ToolRisk.WritesDisk,
    "End-to-end orchestrator: pick a template, regenerate its texture from a user image (file path " +
    "OR base64 for chat-attached images) and optionally tweak the mesh, then add the result as new " +
    "entries (model + texture, plus palette if indexed) alongside the template inside the archive. " +
    "Originals are untouched. Risk: writes archive.")]
public sealed class WeaponsCreateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the archive (will be modified in place unless outputPath is given).")] string archivePath,
        [Description("Name of the template weapon entry to base the new one on.")] string templateName,
        [Description("Name for the new weapon entry (e.g. 'wpn_custom').")] string newName,
        [Description("Absolute path to a PNG image on disk. Leave empty to use imageBase64.")] string imagePath = "",
        [Description("Base64-encoded PNG bytes for the new texture. Use this for chat-attached images.")] string imageBase64 = "",
        [Description("Optional JSON of mesh edits; pass empty string to reuse template mesh as-is.")] string meshEditsJson = "",
        [Description("Optional output path; omit to write back to archivePath.")] string outputPath = "",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(imageBase64))
            return CdText.Message("weapons_create: provide imagePath OR imageBase64.");

        // Two-step orchestration so the bridge returns intermediate bytes
        // we can splice into a single write. We could also do it as one
        // monolithic bridge command, but the current shape lets callers
        // re-use the same encode results in dry-run flows.
        string encodeTexture = await bridge.SendAsync(new
        {
            Command = "ZIPPER_ENCODE_WEAPON_TEXTURE",
            InputPath = archivePath,
            WeaponName = templateName,
            WeaponImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath,
            WeaponImageData = string.IsNullOrWhiteSpace(imageBase64) ? null : imageBase64,
        }, ct);

        string encodeModel = await bridge.SendAsync(new
        {
            Command = "ZIPPER_ENCODE_WEAPON_MODEL",
            InputPath = archivePath,
            WeaponName = templateName,
            WeaponMeshEditsJson = string.IsNullOrWhiteSpace(meshEditsJson) ? null : meshEditsJson,
        }, ct);

        string? tifBase64 = ExtractField(encodeTexture, "tif");
        string? palBase64 = ExtractField(encodeTexture, "palette");
        string? mdlBase64 = ExtractField(encodeModel, "mdl");
        if (tifBase64 == null || mdlBase64 == null)
            return CdText.Message(
                "weapons_create: encoder did not return expected bytes. " +
                $"encode_texture response: {encodeTexture}\nencode_model response: {encodeModel}");

        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "ZIPPER_WRITE_WEAPON",
            InputPath = archivePath,
            OutputPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath,
            WeaponNewName = newName,
            WeaponModelData = mdlBase64,
            WeaponTextureData = tifBase64,
            WeaponPaletteData = palBase64,
        }, ct));
    }

    // Minimal JSON field extractor sufficient for the bridge response shape
    // (a CommandResponse with a stringified Data payload). We avoid pulling
    // in a JSON library at the tool layer because every other tool here is
    // a simple pass-through to the bridge.
    private static string? ExtractField(string bridgeJson, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bridgeJson);
            if (!doc.RootElement.TryGetProperty("data", out var dataElement)) return null;
            string? dataJson = dataElement.GetString();
            if (dataJson == null) return null;
            using var inner = System.Text.Json.JsonDocument.Parse(dataJson);
            if (!inner.RootElement.TryGetProperty(key, out var field)) return null;
            return field.ValueKind == System.Text.Json.JsonValueKind.Null ? null : field.GetString();
        }
        catch
        {
            return null;
        }
    }
}
