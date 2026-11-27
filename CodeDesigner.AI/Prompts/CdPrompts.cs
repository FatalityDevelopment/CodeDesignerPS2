using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Prompts;

/// <summary>
/// Reusable workflow prompts. Like Resources, these keep multi-step guidance out of
/// tool descriptions and load only when the user invokes the prompt.
/// </summary>
[McpServerPromptType]
public class CdPrompts
{
    [McpServerPrompt(Name = "session_bootstrap")]
    [Description("How to start a CodeDesigner session.")]
    public static string SessionBootstrap() =>
        "Call cd_session_init first (pass a gameId if known). Then call cd_tool_index to see the tools " +
        "available in the active profile. Read cd://docs/rules for operating rules. Relaunch with a " +
        "different --profile (core/lite/re/packets/patching/cheats/media/multi-agent/admin) to access other tool domains.";

    [McpServerPrompt(Name = "packet_reversing")]
    [Description("Guided SOCOM/Medius packet reverse-engineering workflow.")]
    public static string PacketReversing() =>
        "Goal: reverse a network flow. Steps (capture in Ps2Emulator's MCP, decode here): (1) ps2_network; " +
        "(2) ps2_capture with a filter and bytes:true while the in-game action is reproduced; (3) scan the " +
        "summaries; (4) net_disassemble / medius_decode only for frames of interest; (5) label_add findings and " +
        "progress_save. See resource cd://docs/packet-workflow for detail.";
}
