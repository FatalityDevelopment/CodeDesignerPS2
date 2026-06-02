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
        "different --profile (core/lite/re/live-debug/packets/patching/cheats/media/pcsx2/multi-agent/admin) to access other tool domains.";

    [McpServerPrompt(Name = "packet_reversing")]
    [Description("Guided SOCOM/Medius packet reverse-engineering workflow.")]
    public static string PacketReversing() =>
        "Goal: reverse a network flow. Steps: (1) packet_status; (2) packet_capture_start with a filter; " +
        "(3) reproduce the in-game action; (4) packet_capture_stop; (5) packet_capture_query(summaryOnly:true) " +
        "to scan; (6) packet_decode/packet_read only for packets of interest; (7) label_upsert findings and " +
        "progress_save. See resource cd://docs/packet-workflow for detail.";
}
