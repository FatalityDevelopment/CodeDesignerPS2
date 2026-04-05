using System.Net;
using System.Net.Sockets;
using CodeDesigner.Ps2.MediusServer.Config;
using CodeDesigner.Ps2.MediusServer.Models;
using CodeDesigner.Ps2.MediusServer.Protocol;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.MediusServer.Servers;

/// <summary>
/// MUIS — Medius Universe Information Server.
///
/// This is the very first server SOCOM II contacts during boot. It returns the
/// MAS IP address and port so the game knows where to authenticate.
///
/// Default port: 10075 (TCP)
///
/// Protocol:
///   Full standard SCERT handshake (handled by base class)
///   Client sends LobbyExt:GetUniverse_ExtraInfo (0x03)
///   Server replies LobbyExt:UniverseStatusList_ExtraInfoResponse (0x04)
/// </summary>
public class MuisServer : BaseMediusTcpServer
{
    private readonly MediusConfig       _config;
    private readonly ILogger<MuisServer> _log;

    public MuisServer(MediusConfig config, ILogger<MuisServer> log)
        : base(log, config)
    {
        _config    = config;
        _log       = log;
        ServerName = "MUIS";
    }

    public Task StartAsync(CancellationToken ct) =>
        StartAsync(_config.BindAddress, _config.MuisPort, ct);

    // =========================================================================
    // Medius message dispatch
    // =========================================================================

    protected override async Task HandleMediusMessageAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        if (packet.MessageClass == NetMessageClass.MessageClassLobbyExt)
        {
            var typeId = (MediusLobbyExtMessageId)packet.MessageType;
            _log.LogDebug("[MUIS] LobbyExt msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyExtMessageId.GetUniverse_ExtraInfo:
                    await HandleGetUniverseInfoAsync(packet, state, tcp, ct);
                    break;

                default:
                    _log.LogDebug("[MUIS] Unhandled LobbyExt msg 0x{T:X2}", packet.MessageType);
                    break;
            }
        }
        else if (packet.MessageClass == NetMessageClass.MessageClassLobby)
        {
            var typeId = (MediusLobbyMessageId)packet.MessageType;
            _log.LogDebug("[MUIS] Lobby msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyMessageId.GetUniverse:
                    await HandleGetUniverseInfoAsync(packet, state, tcp, ct);
                    break;

                default:
                    _log.LogDebug("[MUIS] Unhandled Lobby msg 0x{T:X2}", packet.MessageType);
                    break;
            }
        }
        else
        {
            _log.LogDebug("[MUIS] Unhandled class {Cls} msg 0x{T:X2}", packet.MessageClass, packet.MessageType);
        }
    }

    // =========================================================================
    // Universe Info
    // =========================================================================

    private async Task HandleGetUniverseInfoAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Request payload: [MessageID: 4][InfoType: 4]
        var r = new MediusReader(packet.Payload);
        uint  messageId = r.Remaining >= 4 ? r.ReadUInt32() : 0;
        uint  infoType  = r.Remaining >= 4 ? r.ReadUInt32() : 0xFFFFFFFF;

        _log.LogInformation("[MUIS] GetUniverseInfo from {Ep} — returning MAS={Ip}:{Port}",
            state.RemoteEndpoint, _config.ServerIp, _config.MasPort);

        // Build MediusUniverseVariableInformationResponse
        // Response payload layout (matching PS2 Medius wire format):
        //   MessageID:           uint32  (4)
        //   StatusCode:          int32   (4)
        //   InfoFilter:          uint32  (4)  — echo back infoType
        //   UniverseID:          int32   (4)
        //   ExtendedInfo:        string  (64)
        //   UniverseName:        string  (64)
        //   UniverseDescription: string  (256)
        //   SvoURL:              string  (256)
        //   DNS:                 string  (256) — MAS hostname or IP
        //   Port:                int32   (4)
        //   EndOfList:           byte    (1)

        var w = new MediusWriter();
        w.Write(messageId);                           // MessageID
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(infoType);                            // InfoFilter (echo)
        w.Write((int)1);                              // UniverseID = 1
        w.WriteString("",              64);           // ExtendedInfo
        w.WriteString("SOCOM II",      64);           // UniverseName
        w.WriteString("Fan server",    256);          // UniverseDescription
        w.WriteString("",              256);          // SvoURL
        w.WriteString(_config.ServerIp, 256);         // DNS = our IP
        w.Write(_config.MasPort);                     // Port
        w.Write((byte)1);                             // EndOfList = true

        // Determine which message type to use as response:
        // If request came as LobbyExt:GetUniverse_ExtraInfo → respond with LobbyExt:UniverseStatusList_ExtraInfoResponse
        // If request came as Lobby:GetUniverse → respond with Lobby:GetUniverseResponse
        MediusPacket response;
        if (packet.MessageClass == NetMessageClass.MessageClassLobbyExt)
            response = MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.UniverseStatusList_ExtraInfoResponse, w.ToArray());
        else
            response = MediusPacket.BuildLobby(MediusLobbyMessageId.GetUniverseResponse, w.ToArray());

        await SendMediusAsync(tcp, response, ct);
    }
}
