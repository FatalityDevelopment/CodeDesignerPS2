using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CodeDesigner.Ps2.MediusServer.Config;
using CodeDesigner.Ps2.MediusServer.Models;
using CodeDesigner.Ps2.MediusServer.Protocol;
using CodeDesigner.Ps2.MediusServer.Storage;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.MediusServer.Servers;

/// <summary>
/// MAS — Medius Authentication Server.
/// Clients connect here first to obtain a session key and authenticate or
/// register an account.  After a successful login the server returns an MLS
/// IP/port for the client to connect to next.
///
/// Default port: 10071 (TCP)
/// </summary>
public class MasServer : BaseMediusTcpServer
{
    private readonly MediusConfig  _config;
    private readonly InMemoryStorage _store;
    private readonly ILogger<MasServer> _log;

    public MasServer(MediusConfig config, InMemoryStorage store, ILogger<MasServer> log)
        : base(log, config)
    {
        _config    = config;
        _store     = store;
        _log       = log;
        ServerName = "MAS";
    }

    public Task StartAsync(CancellationToken ct) =>
        StartAsync(_config.BindAddress, _config.MasPort, ct);

    // -------------------------------------------------------------------------
    // Medius message dispatch
    // -------------------------------------------------------------------------

    protected override async Task HandleMediusMessageAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        if (packet.MessageClass == NetMessageClass.MessageClassLobby)
        {
            var typeId = (MediusLobbyMessageId)packet.MessageType;
            _log.LogDebug("[MAS] Lobby msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyMessageId.SessionBegin:
                    await HandleSessionBeginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AccountLogin:
                    await HandleAccountLoginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AnonymousLogin:
                    await HandleAnonymousLoginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AccountRegistration:
                    await HandleAccountRegistrationAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AccountLogout:
                    await HandleAccountLogoutAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.TicketLogin:
                    await HandleTicketLoginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.GetMyIP:
                    await HandleGetMyIpAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.Policy:
                    await HandlePolicyAsync(packet, state, tcp, ct);
                    break;

                default:
                    _log.LogDebug("[MAS] Unhandled lobby msg 0x{Type:X2}", packet.MessageType);
                    break;
            }
        }
        else if (packet.MessageClass == NetMessageClass.MessageClassLobbyExt)
        {
            var typeId = (MediusLobbyExtMessageId)packet.MessageType;
            _log.LogDebug("[MAS] LobbyExt msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyExtMessageId.ExtendedSessionBeginRequest:
                    await HandleExtendedSessionBeginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.DnasSignaturePost:
                    // DNAS ticket accepted unconditionally — no real DNAS validation
                    _log.LogInformation("[MAS] DnasSignaturePost received — accepted");
                    break;

                default:
                    _log.LogDebug("[MAS] Unhandled lobbyExt msg 0x{Type:X2}", packet.MessageType);
                    break;
            }
        }
    }

    // =========================================================================
    // Session
    // =========================================================================

    private async Task HandleSessionBeginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Payload: [messageId: 4 bytes reserved/tick][reserved bytes]
        // We just generate a new unique session key and return it.
        state.SessionKey = GenerateSessionKey();
        state.Status     = PlayerStatus.InAuthWorld;

        _log.LogInformation("[MAS] SessionBegin -> key={Key}", state.SessionKey);

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);  // StatusCode
        w.WriteString(state.SessionKey, 64);                // SessionKey (64-char max)

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.SessionBeginResponse, w.ToArray()), ct);
    }

    private async Task HandleExtendedSessionBeginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.SessionKey = GenerateSessionKey();
        state.Status     = PlayerStatus.InAuthWorld;

        _log.LogInformation("[MAS] ExtendedSessionBegin -> key={Key}", state.SessionKey);

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString(state.SessionKey, 64);

        // ExtendedSessionBeginResponse reuses SessionBeginResponse message ID
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.SessionBeginResponse, w.ToArray()), ct);
    }

    // =========================================================================
    // Authentication
    // =========================================================================

    private async Task HandleAccountLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        // Layout: sessionKey(64) username(64) password(64) [optional fields]
        string sessionKey = r.ReadString(64);
        string username   = r.ReadString(64);
        string password   = r.ReadString(64);

        _log.LogInformation("[MAS] AccountLogin: user={User}", username);

        MediusCallbackStatus status;
        MediusAccount? account = _store.GetAccountByName(username);

        if (account == null)
        {
            if (_config.AutoCreateAccounts)
            {
                account = _store.CreateAccount(username, HashPassword(password));
                _log.LogInformation("[MAS] Auto-created account '{User}' id={Id}", username, account.AccountId);
            }
            else
            {
                status = MediusCallbackStatus.MediusAccountNotFound;
                await SendLoginFailure(tcp, status, ct);
                return;
            }
        }
        else if (account.PasswordHash != HashPassword(password))
        {
            await SendLoginFailure(tcp, MediusCallbackStatus.MediusLoginFailed, ct);
            return;
        }

        if (account.IsBanned)
        {
            await SendLoginFailure(tcp, MediusCallbackStatus.MediusFail, ct);
            return;
        }

        state.AccountId   = account.AccountId;
        state.AccountName = account.AccountName;
        state.Status      = PlayerStatus.InAuthWorld;
        status            = MediusCallbackStatus.MediusSuccess;

        var w = new MediusWriter();
        w.Write((int)status);
        w.Write(account.AccountId);           // AccountId
        w.WriteString(account.AccountName, 64); // Username
        w.WriteString(state.SessionKey, 64);    // SessionKey handed back
        // MLS Connection Address info
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleAnonymousLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);

        _log.LogInformation("[MAS] AnonymousLogin");

        state.AccountId   = 0;
        state.AccountName = "Anonymous";
        state.Status      = PlayerStatus.InAuthWorld;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(0);                           // AccountId = 0 for anon
        w.WriteString("Anonymous", 64);
        w.WriteString(state.SessionKey, 64);
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AnonymousLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleAccountRegistrationAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        string username   = r.ReadString(64);
        string password   = r.ReadString(64);

        _log.LogInformation("[MAS] AccountRegistration: user={User}", username);

        if (_store.GetAccountByName(username) != null)
        {
            var wFail = new MediusWriter();
            wFail.Write((int)MediusCallbackStatus.MediusAccountLoggedIn);
            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountRegistrationResponse, wFail.ToArray()), ct);
            return;
        }

        var account = _store.CreateAccount(username, HashPassword(password));
        _log.LogInformation("[MAS] Registered account '{User}' id={Id}", username, account.AccountId);

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(account.AccountId);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountRegistrationResponse, w.ToArray()), ct);
    }

    private async Task HandleAccountLogoutAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        _log.LogInformation("[MAS] AccountLogout: {User}", state.AccountName);
        state.AccountId   = null;
        state.AccountName = string.Empty;
        state.Status      = PlayerStatus.Disconnected;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLogoutResponse, w.ToArray()), ct);
    }

    private async Task HandleTicketLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // TicketLogin payload: [sessionKey: 64][username: 64][ticketBlob: variable]
        // We accept any ticket — no real DNAS/ticket validation.
        var r = new MediusReader(packet.Payload);
        string sessionKey  = r.Remaining >= 64 ? r.ReadString(64) : string.Empty;
        string username    = r.Remaining >= 64 ? r.ReadString(64) : "Player";
        // remaining bytes = ticket blob (ignored)

        _log.LogInformation("[MAS] TicketLogin: user={User}", username);

        // Find or auto-create the account
        MediusAccount? account = _store.GetAccountByName(username);
        if (account == null)
        {
            account = _store.CreateAccount(username, string.Empty); // no password for ticket auth
            _log.LogInformation("[MAS] Auto-created account for ticket login: '{User}' id={Id}", username, account.AccountId);
        }

        state.AccountId   = account.AccountId;
        state.AccountName = account.AccountName;
        state.Status      = PlayerStatus.InAuthWorld;

        // TicketLoginResponse layout (mirrors AccountLoginResponse with extra MLS address):
        //   StatusCode:  int32  (4)
        //   AccountID:   int32  (4)
        //   AccountType: int32  (4)  = 0 (master)
        //   AccessKey:   string (64) = session key (reused as access token)
        //   SessionKey:  string (64)
        //   MLS address block (IP string 16 + port 2 + pad 2)
        //   WorldID:     int32  (4)
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(account.AccountId);
        w.Write((int)0);                      // AccountType = MasterAccount
        w.WriteString(state.SessionKey, 64);  // AccessKey
        w.WriteString(state.SessionKey, 64);  // SessionKey
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);
        w.Write((int)1);                      // WorldID = 1 (default lobby)

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.TicketLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleGetMyIpAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var endpoint = state.RemoteEndpoint;
        // Strip port if present
        var ip = endpoint.Contains(':') ? endpoint[..endpoint.LastIndexOf(':')] : endpoint;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString(ip, 16);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GetMyIPResponse, w.ToArray()), ct);
    }

    private async Task HandlePolicyAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        const string policyText = "Welcome to CodeDesigner Medius Server.\r\nPlay fair, have fun!";

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)1);  // last = true
        w.WriteString(policyText, 256);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PolicyResponse, w.ToArray()), ct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SendLoginFailure(TcpClient tcp, MediusCallbackStatus status, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)status);
        w.Write(0);              // AccountId
        w.WriteString("", 64);  // Username
        w.WriteString("", 64);  // SessionKey
        w.WritePadding(8);      // MLS address placeholder

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLoginResponse, w.ToArray()), ct);
    }

    private static string GenerateSessionKey()
    {
        return Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

// ---------------------------------------------------------------------------
// Extension helpers for MediusWriter (keeps them near where they are used)
// ---------------------------------------------------------------------------
internal static class MediusWriterExtensions
{
    /// <summary>
    /// Write an MLS connection address block:
    /// [ip string 16 bytes][port 2 bytes][pad 2 bytes]
    /// The PS2 client reads this to know where to connect for lobby.
    /// </summary>
    public static void WriteMlsAddress(this MediusWriter w, string ip, int port)
    {
        w.WriteString(ip, 16);
        w.Write((ushort)port);
        w.WritePadding(2);
    }
}
