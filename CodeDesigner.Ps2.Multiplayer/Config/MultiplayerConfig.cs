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
    public int MuisPort { get; set; } = 10071;

    /// <summary>MAS (Authentication) server TCP port.</summary>
    public int MasPort { get; set; } = 10073;

    /// <summary>MLS (Lobby) server TCP port.</summary>
    public int MlsPort { get; set; } = 10075;

    /// <summary>DME (Demonware Multiplayer Engine) in-game relay TCP port.</summary>
    public int DmePort { get; set; } = 10070;

    // ── DNAS ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// DNAS HTTPS server port (plain-HTTP debug fallback). The PS2 contacts the
    /// DNAS gateway over HTTPS; the real port is DnasHttpsPort (443).
    /// This port is kept for plain-HTTP debugging without SSL.
    /// </summary>
    public int DnasPort { get; set; } = 10060;

    /// <summary>
    /// DNAS HTTPS server port. PS2 contacts gate1.*.dnas.playstation.org:443.
    /// Requires elevated privileges on Linux (run as root or use iptables redirect).
    /// Set to 0 to disable the HTTPS listener.
    /// </summary>
    public int DnasHttpsPort { get; set; } = 443;

    /// <summary>
    /// DNAS DNS responder port (normally 53). The DNS server intercepts
    /// *.dnas.playstation.org queries and returns ServerIp.
    /// Requires elevated privileges on Linux (run as root or redirect port 53).
    /// Set to 0 to disable the built-in DNS responder.
    /// </summary>
    public int DnsDnasPort { get; set; } = 53;

    /// <summary>
    /// DNAS region sub-folder inside Data/. The server looks for response files
    /// under Data/<DnasRegion>/. Valid values: us-gw, eu-gw, gai-gw.
    /// </summary>
    public string DnasRegion { get; set; } = "us-gw";

    /// <summary>
    /// Path to the TLS certificate file (PEM or DER) for the DNAS HTTPS server.
    /// If empty, defaults to Certificates/dnas.crt relative to the app base directory.
    /// </summary>
    public string DnasCertPath { get; set; } = "";

    /// <summary>
    /// Path to the TLS private-key file (PEM) for the DNAS HTTPS server.
    /// If empty, defaults to Certificates/dnas.key relative to the app base directory.
    /// </summary>
    public string DnasKeyPath { get; set; } = "";

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

    // ── Custom DNS mappings ───────────────────────────────────────────────────

    /// <summary>
    /// Custom hostname → IP overrides served by the built-in DNS server.
    /// The DNS server answers A-record queries for any hostname in this map
    /// with the mapped IP, in addition to the built-in DNAS intercept rules.
    /// Example:
    ///   "muis.example.com": "192.168.2.89"
    /// </summary>
    public Dictionary<string, string> DnsMappings { get; set; } = new();

    /// <summary>When true, accounts are auto-created on first login (no separate registration).</summary>
    public bool AutoCreateAccounts { get; set; } = true;

    /// <summary>Client idle timeout in seconds.</summary>
    public int ClientTimeoutSeconds { get; set; } = 60;

    /// <summary>Name of the multiplayer universe.</summary>
    public string UniverseName { get; set; } = "My PS2 Multiplayer Server";
    /// <summary>The description of the universe that gets displayed to users.</summary>
    public string UniverseDescription { get; set; } = "Welcome to my PS2 multiplayer server!";
    /// <summary>The news about the universe that gets displayed to users.</summary>
    public string UniverseNews { get; set; } = "No news at the moment. Check back later!";
    public string UniversePatchVersion { get; set; } = "r0004";
    public string UniversePatchUrl { get; set; } = "https://example.com/patches/r0004/update.zip";

    // ── Binary injection ──────────────────────────────────────────────────────

    /// <summary>
    /// Custom binaries to push into the console over RT_MSG_SERVER_MEMORY_POKE once a
    /// player reaches the lobby (MLS). Each entry loads a .bin at its LoadAddress and,
    /// if HookAddress is set, writes a MIPS jump there so the injected code runs.
    /// Empty = nothing is injected (stock behaviour). This is the same channel SOCOM II
    /// servers use to push their online code module; here it carries YOUR binaries.
    /// </summary>
    public List<BinaryInjection> Injections { get; set; } = new();

    /// <summary>
    /// Folder watched for on-demand injection manifests (*.json with the same shape as
    /// a <see cref="BinaryInjection"/> entry). Drop a manifest here and it is sent to the
    /// connected console(s) immediately, then the manifest is consumed. Empty = disabled.
    /// </summary>
    public string InjectDropFolder { get; set; } = "";

    /// <summary>
    /// Path to a captured SERVER_MEMORY_POKE stream to replay verbatim once a player completes the
    /// MLS lobby exchange — i.e. SOCOM II's online-code download. Format: a sequence of
    /// [len:4 LE][pokePayload] records where each pokePayload is a complete [destAddr:4][size:4][data]
    /// block (as produced by dump_pokes.py from the retail universe-community-screen capture).
    /// The retail game ships WITHOUT online code; without this stream the client finishes the lobby
    /// handshake then times out ("Disconnected from Network"). Empty = disabled.
    /// </summary>
    public string LobbyPokeStreamFile { get; set; } = "";
}

/// <summary>
/// One binary to inject into the PS2 over SERVER_MEMORY_POKE. Addresses are EE virtual
/// addresses written as hex strings (e.g. "0x000A0000").
/// </summary>
public class BinaryInjection
{
    /// <summary>Disable without deleting the entry.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional label for logs.</summary>
    public string Name { get; set; } = "";

    /// <summary>Path to the .bin (absolute, or relative to the app base dir / Data/).</summary>
    public string File { get; set; } = "";

    /// <summary>EE address the blob is written to, hex (e.g. "0x000A0000").</summary>
    public string LoadAddress { get; set; } = "";

    /// <summary>Optional EE address to write a "J entry" hook at, hex. Null/empty = load only.</summary>
    public string? HookAddress { get; set; }

    /// <summary>Entry point the hook jumps to, hex. Null/empty = LoadAddress.</summary>
    public string? EntryPoint { get; set; }

    /// <summary>Bytes per SERVER_MEMORY_POKE write (retail SOCOM II uses 512).</summary>
    public int ChunkSize { get; set; } = 512;
}
