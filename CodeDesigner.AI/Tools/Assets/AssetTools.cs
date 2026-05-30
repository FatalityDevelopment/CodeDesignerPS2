using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Assets;

// Game-asset tools (ZAR/ZED/ZDB/TIF/GEO/MDL). Bridge command names are
// best-effort and may need reconciling with the backend protocol.

[CdTool("asset_archive_index", ToolDomain.Assets, ToolRisk.ReadOnly,
    "Index an archive's entries (names, sizes, types). Risk: read-only.")]
public sealed class ArchiveIndexTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Archive path or in-game id.")] string archive, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_ARCHIVE_INDEX", Archive = archive }, ct));
}

[CdTool("asset_archive_extract", ToolDomain.Assets, ToolRisk.WritesDisk,
    "Extract one entry from an archive to disk. Risk: writes disk.")]
public sealed class ArchiveExtractTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Archive path or id.")] string archive,
        [Description("Entry name/index to extract.")] string entry,
        [Description("Output path.")] string outputPath,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_ARCHIVE_EXTRACT", Archive = archive, Entry = entry, Output = outputPath }, ct));
}

[CdTool("asset_texture_inspect", ToolDomain.Assets, ToolRisk.ReadOnly,
    "Inspect a texture's format, dimensions, and palette. Risk: read-only.")]
public sealed class TextureInspectTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Texture path or id.")] string texture, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_TEXTURE_INSPECT", Texture = texture }, ct));
}

[CdTool("asset_model_inspect", ToolDomain.Assets, ToolRisk.ReadOnly,
    "Inspect a model's meshes, bones, and materials. Risk: read-only.")]
public sealed class ModelInspectTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Model path or id.")] string model, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSET_MODEL_INSPECT", Model = model }, ct));
}
