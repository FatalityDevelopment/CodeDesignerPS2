namespace CodeDesigner.Ps2.Multiplayer.Config;

public class MultiplayerConfig
{
    // ── Network ──────────────────────────────────────────────────────────────

    /// <summary>IP address to bind to. 0.0.0.0 = all interfaces.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Announced server IP returned to clients (your LAN/WAN IP).</summary>
    public string ServerIp { get; set; } = "127.0.0.1";

    // ── Medius ports ─────────────────────────────────────────────────────────

    /// <summary>MUIS (Universe Information) server TCP port. PS2 games connect here first.</summary>
    public int MuisPort { get; set; } = 10075;

    /// <summary>MAS (Authentication) server TCP port.</summary>
    public int MasPort { get; set; } = 10071;

    /// <summary>MLS (Lobby) server TCP port.</summary>
    public int MlsPort { get; set; } = 10073;

    /// <summary>DME (Demonware Multiplayer Engine) in-game relay TCP port.</summary>
    public int DmePort { get; set; } = 10070;

    // ── DNAS ─────────────────────────────────────────────────────────────────

    /// <summary>DNAS authentication server TCP port.</summary>
    public int DnasPort { get; set; } = 10060;

    /// <summary>
    /// DNAS region sub-folder inside Data/. The server looks for response files
    /// under Data/<DnasRegion>/. Valid values: us-gw, eu-gw, gai-gw.
    /// </summary>
    public string DnasRegion { get; set; } = "us-gw";

    // ── NAT ───────────────────────────────────────────────────────────────────

    /// <summary>NAT punchthrough helper UDP port.</summary>
    public int NatPort { get; set; } = 10069;

    // ── Game ─────────────────────────────────────────────────────────────────

    /// <summary>Maximum clients per server before new connections are rejected.</summary>
    public int MaxClients { get; set; } = 1000;

    /// <summary>
    /// Supported AppIds. SOCOM II NTSC-U = 10683, PAL = 10684.
    /// Empty list = accept any AppId.
    /// </summary>
    public List<int> AllowedAppIds { get; set; } = new() { 10683, 10684 };

    /// <summary>When true, accounts are auto-created on first login (no separate registration).</summary>
    public bool AutoCreateAccounts { get; set; } = true;

    /// <summary>Client idle timeout in seconds.</summary>
    public int ClientTimeoutSeconds { get; set; } = 60;
}
