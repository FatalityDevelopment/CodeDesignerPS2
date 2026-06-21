using CodeDesigner.Ps2.Multiplayer.Crypto;

namespace CodeDesigner.Ps2.Multiplayer.Models;

public enum PlayerStatus
{
    Disconnected     = 0,
    InAuthWorld      = 1,
    InChatWorld      = 2,
    InGameWorld      = 3,
    InOtherUniverse  = 4,
}

/// <summary>
/// Tracks a single connected client's session state across the SCERT handshake
/// and into the Medius Lobby or Game layer.
/// </summary>
public class MediusClientState
{
    // -------------------------------------------------------------------------
    // Connection identity
    // -------------------------------------------------------------------------

    /// <summary>Unique connection ID assigned when the TCP socket is accepted.</summary>
    public string ConnectionId   { get; set; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>Remote endpoint string for logging.</summary>
    public string RemoteEndpoint { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // SCERT handshake state
    // -------------------------------------------------------------------------
    public bool HelloReceived        { get; set; }
    public bool CryptKeyReceived     { get; set; }
    public bool ConnectReceived      { get; set; }
    public bool ConnectReadyReceived { get; set; }
    public bool IsSessionEstablished { get; set; }

    /// <summary>Medius protocol version sent by the client in CLIENT_HELLO.</summary>
    public ushort MediusVersion { get; set; } = 108;

    /// <summary>AppId the client declared during CONNECT_TCP.</summary>
    public int AppId { get; set; }

    /// <summary>
    /// RC4 session key generated during handshake.
    /// 64 bytes. All-zero when encryption is disabled.
    /// </summary>
    public byte[] SessionRc4Key { get; set; } = new byte[64];

    /// <summary>Raw bytes the client sent in CLIENT_CRYPTKEY_PUBLIC.</summary>
    public byte[] ClientCryptKey { get; set; } = Array.Empty<byte>();

    /// <summary>RSA cipher constructed from the client's public key (for hash verification).</summary>
    public Ps2Rsa? ClientRsa { get; set; }

    /// <summary>SCERT client counter used in SERVER_CONNECT_ACCEPT_TCP.</summary>
    public uint ScertId { get; set; }

    // -------------------------------------------------------------------------
    // Medius session
    // -------------------------------------------------------------------------
    public string SessionKey   { get; set; } = string.Empty;
    public int?   AccountId    { get; set; }
    public string AccountName  { get; set; } = string.Empty;
    public PlayerStatus Status { get; set; } = PlayerStatus.Disconnected;

    /// <summary>Set once the MLS lobby online-code SERVER_MEMORY_POKE stream has been replayed to this client.</summary>
    public bool LobbyPokesSent { get; set; }

    // -------------------------------------------------------------------------
    // World placement
    // -------------------------------------------------------------------------
    public int? CurrentChannelId { get; set; }
    public int? CurrentGameId    { get; set; }
    public int? WorldId          { get; set; }

    // -------------------------------------------------------------------------
    // MUIS anti-cheat exchange state
    // -------------------------------------------------------------------------

    /// <summary>Round counter for the MUIS CHEAT_QUERY/MEMORY_POKE exchange (0=not started, 1-3=rounds, 4=done).</summary>
    public int    MuisCheatRound  { get; set; } = 0;
    /// <summary>MessageID from the PS2's GetUniverseInfo request; echoed back in the universe response.</summary>
    public byte[] MuisMsgId       { get; set; } = new byte[21];
    public uint   MuisInfoType    { get; set; } = 0xFFFFFFFF;
    public bool   MuisUseLobbyExt { get; set; } = true;

    // -------------------------------------------------------------------------
    // DME game server state
    // -------------------------------------------------------------------------

    /// <summary>ID of the DME world this client is participating in (set after DME auth).</summary>
    public int? DmeWorldId { get; set; }

    /// <summary>DME slot index within the world (set after DME auth).</summary>
    public int? DmeClientIndex { get; set; }

    /// <summary>True once the client has completed the DME authentication exchange.</summary>
    public bool IsDmeAuthenticated { get; set; }

    // -------------------------------------------------------------------------
    // Timing
    // -------------------------------------------------------------------------
    public DateTime ConnectedAt    { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt  { get; set; } = DateTime.UtcNow;
    public TimeSpan Timeout        { get; set; } = TimeSpan.FromSeconds(60);

    public bool IsTimedOut => DateTime.UtcNow - LastMessageAt > Timeout;
    public bool IsLoggedIn => AccountId.HasValue;

    public void Touch() => LastMessageAt = DateTime.UtcNow;
}
