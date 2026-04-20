using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using CodeDesigner.Ps2.Multiplayer.Storage;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

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
    private readonly MultiplayerConfig  _config;
    private readonly InMemoryStorage _store;
    private readonly ILogger<MasServer> _log;

    public MasServer(MultiplayerConfig config, InMemoryStorage store, ILogger<MasServer> log)
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
                    _log.LogInformation("[MAS] Unhandled lobby msg 0x{Type:X2} payload={Hex}",
                        packet.MessageType, BitConverter.ToString(packet.Payload).Replace("-", " "));
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
                    _log.LogInformation("[MAS] Unhandled lobbyExt msg 0x{Type:X2} payload={Hex}",
                        packet.MessageType, BitConverter.ToString(packet.Payload).Replace("-", " "));
                    break;
            }
        }
        else
        {
            _log.LogInformation("[MAS] Unknown msg class=0x{Class:X2} type=0x{Type:X2} payload={Hex}",
                (byte)packet.MessageClass, packet.MessageType, BitConverter.ToString(packet.Payload).Replace("-", " "));
        }
    }

    // =========================================================================
    // Session
    // =========================================================================

    private async Task HandleSessionBeginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Old Medius payload: [messageId: 21][sessionType: 4][sessionFlags: 4][appId: 4][...]
        // Echo messageId back in response (required by old Medius protocol).
        byte[] msgId = packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

        state.SessionKey = GenerateSessionKey();
        state.Status     = PlayerStatus.InAuthWorld;

        _log.LogInformation("[MAS] SessionBegin msgId={MsgId} -> key={Key}",
            BitConverter.ToString(msgId).Replace("-", ""), state.SessionKey);

        // Old Medius SessionBeginResponse: [messageId: 21][status: 4][sessionKey: 17]
        var w = new MediusWriter();
        w.Write(msgId);                                                       // messageId echo (21 bytes)
        w.Write((int)MediusCallbackStatus.MediusSuccess);                     // StatusCode (4 bytes)
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);  // SessionKey (17 bytes)

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.SessionBeginResponse, w.ToArray()), ct);
    }

    private async Task HandleExtendedSessionBeginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        byte[] msgId = packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

        state.SessionKey = GenerateSessionKey();
        state.Status     = PlayerStatus.InAuthWorld;

        _log.LogInformation("[MAS] ExtendedSessionBegin -> key={Key}", state.SessionKey);

        var w = new MediusWriter();
        w.Write(msgId);                                                       // messageId echo (21 bytes)
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);  // SessionKey (17 bytes)

        // ExtendedSessionBeginResponse reuses SessionBeginResponse message ID
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.SessionBeginResponse, w.ToArray()), ct);
    }

    // =========================================================================
    // Authentication
    // =========================================================================

    private async Task HandleAccountLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        // Old Medius layout: [messageId: 21][sessionKey: 17][username: 32][password: 32]
        byte[] msgId      = r.ReadBytes(MediusConstants.MESSAGEID_MAXLEN);
        string sessionKey = r.ReadString(MediusConstants.SESSIONKEY_MAXLEN);
        string username   = r.ReadString(MediusConstants.ACCOUNTNAME_MAXLEN);
        string password   = r.ReadString(MediusConstants.PASSWORD_MAXLEN);

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
                await SendLoginFailure(tcp, status, msgId, ct);
                return;
            }
        }
        else if (account.PasswordHash != HashPassword(password))
        {
            await SendLoginFailure(tcp, MediusCallbackStatus.MediusLoginFailed, msgId, ct);
            return;
        }

        if (account.IsBanned)
        {
            await SendLoginFailure(tcp, MediusCallbackStatus.MediusFail, msgId, ct);
            return;
        }

        state.AccountId   = account.AccountId;
        state.AccountName = account.AccountName;
        state.Status      = PlayerStatus.InAuthWorld;
        status            = MediusCallbackStatus.MediusSuccess;

        // Old Medius AccountLoginResponse: [messageId:21][status:4][accountId:4][accountName:32][sessionKey:17][mlsAddr:20]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)status);
        w.Write(account.AccountId);
        w.WriteString(account.AccountName, MediusConstants.ACCOUNTNAME_MAXLEN);
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleAnonymousLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        // Old Medius layout: [messageId: 21][sessionKey: 17]
        byte[] msgId = r.ReadBytes(MediusConstants.MESSAGEID_MAXLEN);

        _log.LogInformation("[MAS] AnonymousLogin");

        state.AccountId   = 0;
        state.AccountName = "Anonymous";
        state.Status      = PlayerStatus.InAuthWorld;

        // Old Medius AnonymousLoginResponse: [messageId:21][status:4][accountId:4][accountName:32][sessionKey:17][mlsAddr:20]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(0);                                                           // AccountId = 0 for anon
        w.WriteString("Anonymous", MediusConstants.ACCOUNTNAME_MAXLEN);
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AnonymousLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleAccountRegistrationAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        // Old Medius layout: [messageId: 21][sessionKey: 17][username: 32][password: 32]
        byte[] msgId      = r.ReadBytes(MediusConstants.MESSAGEID_MAXLEN);
        string sessionKey = r.ReadString(MediusConstants.SESSIONKEY_MAXLEN);
        string username   = r.ReadString(MediusConstants.ACCOUNTNAME_MAXLEN);
        string password   = r.ReadString(MediusConstants.PASSWORD_MAXLEN);

        _log.LogInformation("[MAS] AccountRegistration: user={User}", username);

        if (_store.GetAccountByName(username) != null)
        {
            // Old Medius AccountRegistrationResponse: [messageId:21][status:4][accountId:4]
            var wFail = new MediusWriter();
            wFail.Write(msgId);
            wFail.Write((int)MediusCallbackStatus.MediusAccountLoggedIn);
            wFail.Write(0);  // accountId placeholder
            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountRegistrationResponse, wFail.ToArray()), ct);
            return;
        }

        var account = _store.CreateAccount(username, HashPassword(password));
        _log.LogInformation("[MAS] Registered account '{User}' id={Id}", username, account.AccountId);

        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(account.AccountId);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountRegistrationResponse, w.ToArray()), ct);
    }

    private async Task HandleAccountLogoutAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        byte[] msgId = packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

        _log.LogInformation("[MAS] AccountLogout: {User}", state.AccountName);
        state.AccountId   = null;
        state.AccountName = string.Empty;
        state.Status      = PlayerStatus.Disconnected;

        // Old Medius AccountLogoutResponse: [messageId:21][status:4]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLogoutResponse, w.ToArray()), ct);
    }

    private async Task HandleTicketLoginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Old Medius TicketLogin payload: [messageId:21][sessionKey:17][username:32][ticketBlob:rest]
        // We accept any ticket — no real DNAS/ticket validation.
        var r = new MediusReader(packet.Payload);
        byte[] msgId      = r.ReadBytes(MediusConstants.MESSAGEID_MAXLEN);
        string sessionKey = r.Remaining >= MediusConstants.SESSIONKEY_MAXLEN  ? r.ReadString(MediusConstants.SESSIONKEY_MAXLEN)  : string.Empty;
        string username   = r.Remaining >= MediusConstants.ACCOUNTNAME_MAXLEN ? r.ReadString(MediusConstants.ACCOUNTNAME_MAXLEN) : "Player";
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

        // Old Medius TicketLoginResponse: [messageId:21][status:4][accountId:4][accountType:4]
        //   [accessKey:17][sessionKey:17][mlsAddr:20][worldId:4]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(account.AccountId);
        w.Write((int)0);                                                       // AccountType = MasterAccount
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);  // AccessKey (17 bytes)
        w.WriteString(state.SessionKey, MediusConstants.SESSIONKEY_MAXLEN);  // SessionKey (17 bytes)
        w.WriteMlsAddress(_config.ServerIp, _config.MlsPort);
        w.Write((int)1);                                                       // WorldID = 1

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.TicketLoginResponse, w.ToArray()), ct);
    }

    private async Task HandleGetMyIpAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        byte[] msgId = packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

        var endpoint = state.RemoteEndpoint;
        // Strip port if present
        var ip = endpoint.Contains(':') ? endpoint[..endpoint.LastIndexOf(':')] : endpoint;

        // Old Medius GetMyIPResponse: [messageId:21][status:4][ip:16]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString(ip, MediusConstants.IP_MAXLEN);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GetMyIPResponse, w.ToArray()), ct);
    }

    private async Task HandlePolicyAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        byte[] msgId = packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

        const string policyText = "Welcome to CodeDesigner Medius Server.\r\nPlay fair, have fun!";

        // Old Medius PolicyResponse: [messageId:21][status:4][endFlag:1][policy:256]
        var w = new MediusWriter();
        w.Write(msgId);
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)1);  // last = true
        w.WriteString(policyText, MediusConstants.POLICY_MAXLEN);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PolicyResponse, w.ToArray()), ct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SendLoginFailure(TcpClient tcp, MediusCallbackStatus status, byte[]? msgId, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write(msgId ?? new byte[MediusConstants.MESSAGEID_MAXLEN]);   // messageId echo (21 bytes)
        w.Write((int)status);
        w.Write(0);                                                       // AccountId
        w.WriteString("", MediusConstants.ACCOUNTNAME_MAXLEN);           // AccountName
        w.WriteString("", MediusConstants.SESSIONKEY_MAXLEN);            // SessionKey
        w.WritePadding(20);                                               // MLS address placeholder

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
