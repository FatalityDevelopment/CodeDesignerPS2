using System.Net;
using System.Text;

namespace CodeDesigner.Ps2.Multiplayer.Protocol;

/// <summary>
/// Represents a raw decoded SCERT transport packet.
/// Wire format: [type: 1 byte][length: 2 bytes LE][payload: length bytes]
/// If the high bit of type is set, 4 hash bytes follow the header before payload (encrypted).
/// </summary>
public class ScertPacket
{
    public const int HeaderSize        = 3;
    public const int EncryptedHashSize = 4;

    public RT_MSG_TYPE MessageType { get; set; }
    public byte[]      Payload     { get; set; } = Array.Empty<byte>();
    public bool        IsEncrypted { get; set; }

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// SERVER_HELLO — carries the server's 128-byte RSA public modulus N (little-endian).
    /// Pass null / all-zeros to signal "no encryption" to the client.
    /// </summary>
    public static ScertPacket CreateHello(byte[]? rsaN = null) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_HELLO,
        // 128-byte RSA modulus; all-zero means no encryption
        Payload = rsaN ?? new byte[128],
    };

    /// <summary>
    /// SERVER_CRYPTKEY_PEER — server's RC4 session key (64 bytes).
    /// Send all-zero when encryption is disabled.
    /// </summary>
    public static ScertPacket CreateCryptKeyPeer(byte[] key) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_CRYPTKEY_PEER,
        Payload     = key,
    };

    /// <summary>
    /// SERVER_CRYPTKEY_GAME — same RC4 key used for game-layer traffic.
    /// </summary>
    public static ScertPacket CreateCryptKeyGame(byte[] key) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_CRYPTKEY_GAME,
        Payload     = key,
    };

    /// <summary>
    /// SERVER_CONNECT_ACCEPT_TCP — completes the SCERT connect handshake.
    /// clientIp is the client's remote IPv4 address (sent back in network byte order).
    /// </summary>
    public static ScertPacket CreateConnectAcceptTcp(uint scertId, uint playerId, ushort playerCount, IPAddress? clientIp)
    {
        using var ms = new MemoryStream(14);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        bw.Write(scertId);      // 4 bytes
        bw.Write(playerId);     // 4 bytes
        bw.Write(playerCount);  // 2 bytes
        // Client's IP in network (big-endian) byte order
        byte[] ip = (clientIp ?? IPAddress.Any).MapToIPv4().GetAddressBytes();
        ms.Write(ip, 0, 4);     // 4 bytes NBO
        return new ScertPacket
        {
            MessageType = RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_ACCEPT_TCP,
            Payload     = ms.ToArray(),
        };
    }

    /// <summary>SERVER_CONNECT_COMPLETE — signals session is fully open.</summary>
    public static ScertPacket CreateConnectComplete() => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_COMPLETE,
        Payload     = BitConverter.GetBytes((uint)0x0001),
    };

    /// <summary>SERVER_CONNECT_REQUIRE — used in newer Medius (>108) handshake.</summary>
    public static ScertPacket CreateConnectRequire() => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_REQUIRE,
        Payload     = Array.Empty<byte>(),
    };

    /// <summary>SERVER_ECHO — keepalive reply or version-probe reply.</summary>
    public static ScertPacket CreateServerEcho() => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_ECHO,
        Payload     = Array.Empty<byte>(),
    };

    /// <summary>Echo back a RT_MSG_CLIENT_ECHO payload.</summary>
    public static ScertPacket CreateEchoResponse(ReadOnlySpan<byte> echoData) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_ECHO,
        Payload     = echoData.ToArray(),
    };

    public static ScertPacket CreateForcedDisconnect(byte reason) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_FORCED_DISCONNECT,
        Payload     = [reason],
    };

    public static ScertPacket CreateServerApp(byte[] mediusPayload) => new()
    {
        MessageType = RT_MSG_TYPE.RT_MSG_SERVER_APP,
        Payload     = mediusPayload,
    };

    // -------------------------------------------------------------------------
    // Serialization
    // -------------------------------------------------------------------------

    /// <summary>Serialize this packet to a byte array (unencrypted).</summary>
    public byte[] Serialize()
    {
        var result = new byte[HeaderSize + Payload.Length];
        result[0] = (byte)MessageType;
        result[1] = (byte)(Payload.Length & 0xFF);
        result[2] = (byte)((Payload.Length >> 8) & 0xFF);
        Buffer.BlockCopy(Payload, 0, result, HeaderSize, Payload.Length);
        return result;
    }

    // -------------------------------------------------------------------------
    // Deserialization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Try to read one SCERT packet from <paramref name="buffer"/>.
    /// Returns null if not enough bytes yet; advances <paramref name="consumed"/>.
    /// </summary>
    public static ScertPacket? TryRead(byte[] buffer, int offset, int length, out int consumed)
    {
        consumed = 0;
        if (length < HeaderSize)
            return null;

        bool isEncrypted = (buffer[offset + 0] & 0x80) != 0;
        var  msgType     = (RT_MSG_TYPE)(buffer[offset + 0] & 0x7F);
        int  payloadLen  = buffer[offset + 1] | (buffer[offset + 2] << 8);

        int totalSize = HeaderSize + payloadLen;
        if (isEncrypted)
            totalSize += EncryptedHashSize;

        if (length < totalSize)
            return null;

        byte[] payload;
        int payloadOffset;
        if (isEncrypted)
        {
            payloadOffset = offset + HeaderSize + EncryptedHashSize;
        }
        else
        {
            payloadOffset = offset + HeaderSize;
        }
        payload = new byte[payloadLen];
        Buffer.BlockCopy(buffer, payloadOffset, payload, 0, payloadLen);

        consumed = totalSize;
        return new ScertPacket
        {
            MessageType = msgType,
            Payload     = payload,
            IsEncrypted = isEncrypted,
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    // (No private helpers needed — factory methods build payloads inline)
}
