using System.Net;
using CodeDesigner.Ps2.Multiplayer.Crypto;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.AI.Tests;

/// <summary>
/// Smoke tests for the canonical packet dissector. The dissector replaces
/// the ~3000-line TypeScript dissector that previously lived only in the
/// packet-viewer webview; these tests pin the schema parsing so the AI/MCP
/// path produces the same field names the UI consumed.
/// </summary>
[TestClass]
public class PacketDissectorTests
{
    private static readonly byte[] PS2Mac    = [0x00, 0x04, 0x1F, 0x11, 0x22, 0x33];
    private static readonly byte[] ServerMac = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];
    private static readonly byte[] PS2IP     = [192, 168, 1, 100];
    private static readonly byte[] ServerIP  = [192, 168, 1, 1];

    [TestMethod]
    public void Dissect_ArpRequest_ProducesEthernetAndArpLayers()
    {
        // Build an ARP request: who has ServerIP? tell PS2.
        var frame = new List<byte>();
        frame.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }); // dst MAC = broadcast
        frame.AddRange(PS2Mac);                                           // src MAC
        frame.AddRange(new byte[] { 0x08, 0x06 });                         // EtherType = ARP
        // ARP body
        frame.AddRange(new byte[] { 0x00, 0x01 });                        // HTYPE Ethernet
        frame.AddRange(new byte[] { 0x08, 0x00 });                        // PTYPE IPv4
        frame.Add(6); frame.Add(4);                                       // HLEN/PLEN
        frame.AddRange(new byte[] { 0x00, 0x01 });                        // OPER = request
        frame.AddRange(PS2Mac);                                           // sender MAC
        frame.AddRange(PS2IP);                                            // sender IP
        frame.AddRange(new byte[] { 0, 0, 0, 0, 0, 0 });                  // target MAC = unknown
        frame.AddRange(ServerIP);                                         // target IP

        var layers = PacketDissector.Dissect(frame.ToArray());

        Assert.AreEqual(2, layers.Count);
        Assert.AreEqual("Ethernet II", layers[0].Label);
        Assert.AreEqual("ARP",         layers[1].Label);

        var ethType = layers[0].Fields.First(f => f.Name == "Type").Value;
        StringAssert.Contains(ethType, "ARP");

        var op = layers[1].Fields.First(f => f.Name == "Opcode").Value;
        StringAssert.Contains(op, "Request");

        var targetIp = layers[1].Fields.First(f => f.Name == "Target IP").Value;
        Assert.AreEqual("192.168.1.1", targetIp);
    }

    [TestMethod]
    public void Dissect_ScertClientConnectTcp_DecodesAppIdAndWorldId()
    {
        // Build a SCERT RT_MSG_CLIENT_CONNECT_TCP for SOCOM II (App Id 20034)
        // sent PS2 → MAS (port 10078). Verifies the SCERT layer + struct decode.
        var scertPayload = new List<byte>();
        scertPayload.AddRange(BitConverter.GetBytes((uint)42));    // World Id = 42
        scertPayload.AddRange(BitConverter.GetBytes((uint)20034)); // App Id
        scertPayload.AddRange(new byte[64]);                       // RSA Key (zeros)

        var scertRecord = new List<byte>();
        scertRecord.Add(0x00);                                            // RT_MSG_CLIENT_CONNECT_TCP
        scertRecord.AddRange(BitConverter.GetBytes((ushort)scertPayload.Count)); // LE u16 plen
        scertRecord.AddRange(scertPayload);

        var frame = BuildIpv4TcpFrame(scertRecord.ToArray(), srcPort: 49152, dstPort: 10078);

        var layers = PacketDissector.Dissect(frame, direction: 0);
        var labels = layers.Select(l => l.Label).ToList();

        Assert.IsTrue(labels.Any(l => l == "Ethernet II"));
        Assert.IsTrue(labels.Any(l => l == "Internet Protocol Version 4"));
        Assert.IsTrue(labels.Any(l => l.StartsWith("Transmission Control Protocol")));
        Assert.IsTrue(labels.Any(l => l.StartsWith("SCERT [MAS]")), $"expected SCERT [MAS] layer, got: {string.Join(", ", labels)}");
        Assert.IsTrue(labels.Any(l => l.StartsWith("SCERT Payload —")));

        var scertLayer = layers.First(l => l.Label.StartsWith("SCERT [MAS]"));
        var msgType    = scertLayer.Fields.First(f => f.Name == "Message Type").Value;
        StringAssert.Contains(msgType, "RT_MSG_CLIENT_CONNECT_TCP");

        var payloadLayer = layers.First(l => l.Label.StartsWith("SCERT Payload —"));
        var appId       = payloadLayer.Fields.First(f => f.Name == "App Id").Value;
        StringAssert.Contains(appId, "Socom 2 US Navy Seals");
        var worldId     = payloadLayer.Fields.First(f => f.Name == "World Id").Value;
        StringAssert.Contains(worldId, "42");
    }

    [TestMethod]
    public void Dissect_LayersSerializeWithLowercaseJsonPropertyNames()
    {
        // The webview's TS interfaces consume `label`/`color`/`fields`/`offset`/
        // `length` and `name`/`value`. The new C# DTOs need [JsonPropertyName]
        // attributes so the wire format stays stable while parsing moves to the
        // bridge — this pins that contract.
        var frame = new List<byte>();
        frame.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        frame.AddRange(PS2Mac);
        frame.AddRange(new byte[] { 0x08, 0x06 });
        frame.AddRange(new byte[] { 0x00, 0x01, 0x08, 0x00, 6, 4, 0x00, 0x01 });
        frame.AddRange(PS2Mac); frame.AddRange(PS2IP);
        frame.AddRange(new byte[] { 0, 0, 0, 0, 0, 0 }); frame.AddRange(ServerIP);

        var layers = PacketDissector.Dissect(frame.ToArray());
        var json   = System.Text.Json.JsonSerializer.Serialize(layers);

        StringAssert.Contains(json, "\"label\":");
        StringAssert.Contains(json, "\"color\":");
        StringAssert.Contains(json, "\"fields\":");
        StringAssert.Contains(json, "\"offset\":");
        StringAssert.Contains(json, "\"length\":");
        StringAssert.Contains(json, "\"name\":");
        StringAssert.Contains(json, "\"value\":");
        // Ensure PascalCase keys do NOT leak through (would break the webview).
        Assert.IsFalse(json.Contains("\"Label\":"), $"unexpected PascalCase key in JSON: {json}");
    }

    [TestMethod]
    public void Dissect_RtMsgClientAppToServer_DecodesMediusAccountLogin()
    {
        // Build a Medius AccountLogin (Lobby class=0x01, type=0x07) wrapped in
        // RT_MSG_CLIENT_APP_TOSERVER (0x0B) on MAS port. Verifies the Medius
        // sub-layer is emitted with the Account Name field decoded.
        var mediusPayload = new List<byte>();
        // Message Id (21 bytes, NUL-padded)
        var msgId = new byte[MediusConstants.MESSAGEID_MAXLEN];
        msgId[0] = (byte)'M'; msgId[1] = (byte)'1';
        mediusPayload.AddRange(msgId);
        // Session Key (17 bytes, NUL-padded)
        mediusPayload.AddRange(new byte[MediusConstants.SESSIONKEY_MAXLEN]);
        // Account Name (32 bytes, ASCII "tyler\0...")
        var name = new byte[MediusConstants.ACCOUNTNAME_MAXLEN];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("tyler");
        Array.Copy(nameBytes, name, nameBytes.Length);
        mediusPayload.AddRange(name);
        // Password (32 bytes, zeros — not asserting on this)
        mediusPayload.AddRange(new byte[MediusConstants.PASSWORD_MAXLEN]);

        var scertPayload = new List<byte>();
        scertPayload.Add((byte)NetMessageClass.MessageClassLobby);          // class = 0x01
        scertPayload.Add((byte)MediusLobbyMessageId.AccountLogin);          // type  = 0x07
        scertPayload.AddRange(mediusPayload);

        var scertRecord = new List<byte>();
        scertRecord.Add((byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER);      // 0x0B
        scertRecord.AddRange(BitConverter.GetBytes((ushort)scertPayload.Count));
        scertRecord.AddRange(scertPayload);

        var frame = BuildIpv4TcpFrame(scertRecord.ToArray(), srcPort: 49152, dstPort: 10078);

        var layers = PacketDissector.Dissect(frame, direction: 0);
        var medius = layers.FirstOrDefault(l => l.Label.StartsWith("Medius [Lobby]"));
        Assert.IsNotNull(medius, $"expected Medius layer, got: {string.Join(", ", layers.Select(l => l.Label))}");

        StringAssert.Contains(medius.Label, "AccountLogin");
        var accountName = medius.Fields.First(f => f.Name == "Account Name").Value;
        Assert.AreEqual("tyler", accountName);
    }

    [TestMethod]
    public void DecryptScertInPlace_RoundTrip_DecodesAccountLogin()
    {
        // Round-trip an encrypted SCERT record carrying a Medius AccountLogin:
        //   1. encrypt the payload with PS2_RC4 + a synthetic session key
        //   2. embed it in a real Ethernet/IPv4/TCP frame on the MAS port
        //   3. call DecryptScertInPlace + Dissect — assert the Medius layer
        //      surfaces "tyler" as Account Name (i.e. crypto + dissection round-trips end-to-end)
        var workingKey = new byte[64];
        for (int i = 0; i < 64; i++) workingKey[i] = (byte)(i * 7 + 3);

        // Medius AccountLogin payload (class=0x01, type=0x07)
        var mediusPayload = new byte[MediusConstants.MESSAGEID_MAXLEN
                                   + MediusConstants.SESSIONKEY_MAXLEN
                                   + MediusConstants.ACCOUNTNAME_MAXLEN
                                   + MediusConstants.PASSWORD_MAXLEN];
        int p = 0;
        mediusPayload[p] = (byte)'M'; mediusPayload[p + 1] = (byte)'1';
        p += MediusConstants.MESSAGEID_MAXLEN + MediusConstants.SESSIONKEY_MAXLEN;
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("tyler");
        Array.Copy(nameBytes, 0, mediusPayload, p, nameBytes.Length);

        var scertPlain = new byte[2 + mediusPayload.Length];
        scertPlain[0] = (byte)NetMessageClass.MessageClassLobby;
        scertPlain[1] = (byte)MediusLobbyMessageId.AccountLogin;
        Array.Copy(mediusPayload, 0, scertPlain, 2, mediusPayload.Length);

        // Encrypt the SCERT payload using PS2_RC4
        PS2_RC4.Encrypt(workingKey, CipherContext.RcClientSession,
            scertPlain, out byte[] cipher, out byte[] hash);

        // Build the on-wire SCERT record: [type|0x80][len LE:2][hash:4][cipher payload]
        byte msgType = (byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER;
        var scertRecord = new List<byte> { (byte)(msgType | 0x80) };
        scertRecord.AddRange(BitConverter.GetBytes((ushort)cipher.Length));
        scertRecord.AddRange(hash);
        scertRecord.AddRange(cipher);

        var frame = BuildIpv4TcpFrame(scertRecord.ToArray(), srcPort: 49152, dstPort: 10078);

        // Dissect BEFORE decryption — the Medius decode should fail because
        // the bytes are gibberish; we only check that the encrypted flag is reported.
        var preLayers = PacketDissector.Dissect(frame, direction: 0);
        var preScert  = preLayers.FirstOrDefault(l => l.Label.StartsWith("SCERT [MAS]"));
        Assert.IsNotNull(preScert);
        StringAssert.Contains(preScert.Fields.First(f => f.Name == "Encrypted").Value, "Yes");

        // Decrypt + re-dissect — Medius layer should now decode correctly
        int decrypted = PacketDissector.DecryptScertInPlace(frame, workingKey);
        Assert.AreEqual(1, decrypted);

        var postLayers = PacketDissector.Dissect(frame, direction: 0);
        var medius = postLayers.FirstOrDefault(l => l.Label.StartsWith("Medius [Lobby]"));
        Assert.IsNotNull(medius, $"expected Medius layer after decrypt, got: {string.Join(", ", postLayers.Select(l => l.Label))}");
        StringAssert.Contains(medius.Label, "AccountLogin");
        Assert.AreEqual("tyler", medius.Fields.First(f => f.Name == "Account Name").Value);
    }

    [TestMethod]
    public void ParseMediusFields_AccountLoginSchema_DecodesNamedFields()
    {
        // Sanity check the MEDIUSDECODE-equivalent direct API — given a raw
        // Medius payload + the AccountLogin schema, fields decode by name.
        var schema = MediusStructSchemas.Medius[(0x07 << 8) | 0x01];
        var payload = new byte[MediusConstants.MESSAGEID_MAXLEN
                             + MediusConstants.SESSIONKEY_MAXLEN
                             + MediusConstants.ACCOUNTNAME_MAXLEN
                             + MediusConstants.PASSWORD_MAXLEN];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("alice");
        Array.Copy(nameBytes, 0,
            payload,
            MediusConstants.MESSAGEID_MAXLEN + MediusConstants.SESSIONKEY_MAXLEN,
            nameBytes.Length);

        var fields = PacketDissector.ParseMediusFields(schema, payload);
        Assert.AreEqual("alice", fields.First(f => f.Name == "Account Name").Value);
    }

    [TestMethod]
    [DataRow((byte)0x62, "TicketLogin")]
    [DataRow((byte)0x63, "TicketLoginResponse")]
    [DataRow((byte)0x64, "GetUniverse")]
    [DataRow((byte)0x65, "GetUniverseResponse")]
    public void Dissect_LobbyClassPs2OverlapBytes_ResolveToPs2Names(byte msgType, string expectedName)
    {
        // Regression: SOCOM II's TicketLogin (Lobby class=0x01, type=0x62)
        // shares its byte with the newer InvitePlayerToClan. Without the
        // PS2 override in MediusTypeName, Enum.GetName picks the clan name
        // alphabetically — so a real TicketLogin packet shows up labelled
        // "InvitePlayerToClan(MediusLobbyMessageId)" in dissection, which
        // looks like the client is sending clan invites before login.
        //
        // Pin the four overlapping bytes to their PS2 meaning since this
        // codebase emulates PS2 traffic.
        var mediusPayload = new byte[MediusConstants.MESSAGEID_MAXLEN
                                   + MediusConstants.SESSIONKEY_MAXLEN
                                   + MediusConstants.ACCOUNTNAME_MAXLEN
                                   + 256];
        var scertPayload = new byte[2 + mediusPayload.Length];
        scertPayload[0] = (byte)NetMessageClass.MessageClassLobby;
        scertPayload[1] = msgType;
        Array.Copy(mediusPayload, 0, scertPayload, 2, mediusPayload.Length);

        var scertRecord = new List<byte> { (byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER };
        scertRecord.AddRange(BitConverter.GetBytes((ushort)scertPayload.Length));
        scertRecord.AddRange(scertPayload);

        var frame = BuildIpv4TcpFrame(scertRecord.ToArray(), srcPort: 49152, dstPort: 10078);

        var layers = PacketDissector.Dissect(frame, direction: 0);
        var medius = layers.FirstOrDefault(l => l.Label.StartsWith("Medius [Lobby]"));
        Assert.IsNotNull(medius, $"expected Medius layer, got: {string.Join(", ", layers.Select(l => l.Label))}");
        StringAssert.Contains(medius.Label, expectedName, $"label was: {medius.Label}");
        Assert.IsFalse(medius.Label.Contains("InvitePlayerToClan") || medius.Label.Contains("CheckMyClanInvitations"),
            $"PS2 byte 0x{msgType:X2} mis-labelled with modern-Medius name: {medius.Label}");
    }

    [TestMethod]
    public void Dissect_EncryptedAppRecord_DoesNotEmitMisleadingMediusLabel()
    {
        // Regression for the exact bug from a SOCOM II MUIS capture:
        //   [MUIS] RAW recv 45 bytes: 8B 26 00 6A 0D 58 6E C7 45 0E ...
        //   [MUIS] CLIENT_APP_TOSERVER (38 bytes) ... (encrypted)
        // Decrypted on the server, this was a GetUniverse_ExtraInfo. On the
        // wire, the FIRST TWO cipher bytes after the hash were (0xC7, 0x45),
        // which the old dissector read as (class=0xC7, type=0x45) and
        // surfaced as "AddToBuddyListFwdConfirmationRequest0" — making the
        // capture look like the client was sending buddy-list traffic during
        // first contact with the server.
        //
        // Reuse those exact wire bytes so this regression test replays the
        // real bug deterministically (no dependency on RNG-y ciphertext).
        byte[] cipherPayload = new byte[38];
        cipherPayload[0] = 0xC7;  // class byte > 5 → heuristic rejects decode
        cipherPayload[1] = 0x45;  // type byte that used to surface as buddy-list name
        // (remaining bytes intentionally left zero)

        var record = new List<byte> {
            (byte)((byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER | 0x80), // encrypted
        };
        record.AddRange(BitConverter.GetBytes((ushort)cipherPayload.Length));
        record.AddRange(new byte[] { 0x6A, 0x0D, 0x58, 0x6E });          // 4-byte hash (from log)
        record.AddRange(cipherPayload);

        var frame  = BuildIpv4TcpFrame(record.ToArray(), srcPort: 49152, dstPort: 10071);
        var layers = PacketDissector.Dissect(frame, direction: 0);

        var scert = layers.FirstOrDefault(l => l.Label.StartsWith("SCERT [MUIS]"));
        Assert.IsNotNull(scert, $"expected SCERT layer, got: {string.Join(", ", layers.Select(l => l.Label))}");

        StringAssert.Contains(scert.Label, "(encrypted)",
            $"label was: {scert.Label}");
        Assert.IsFalse(scert.Label.Contains("AddToBuddyList"),
            $"encrypted record mis-labelled with the original bug's bogus name: {scert.Label}");
        Assert.IsFalse(layers.Any(l => l.Label.StartsWith("Medius [")),
            $"unexpected Medius sub-layer for encrypted record: {string.Join(", ", layers.Select(l => l.Label))}");
    }

    // ── Frame-building helper ───────────────────────────────────────────────

    /// <summary>
    /// Build a complete Ethernet+IPv4+TCP frame carrying <paramref name="tcpPayload"/>
    /// from PS2 → server. Zero-checksums (the dissector doesn't validate them).
    /// </summary>
    private static byte[] BuildIpv4TcpFrame(byte[] tcpPayload, ushort srcPort, ushort dstPort)
    {
        int ipHdr  = 20;
        int tcpHdr = 20;
        int ipLen  = ipHdr + tcpHdr + tcpPayload.Length;

        var frame = new List<byte>(14 + ipLen);

        // Ethernet
        frame.AddRange(ServerMac);                       // dst MAC
        frame.AddRange(PS2Mac);                          // src MAC
        frame.AddRange(new byte[] { 0x08, 0x00 });       // EtherType = IPv4

        // IPv4
        frame.Add(0x45);                                 // Version=4, IHL=5
        frame.Add(0x00);                                 // DSCP/ECN
        frame.AddRange(IpAddrBE((ushort)ipLen));         // Total Length (BE)
        frame.AddRange(new byte[] { 0x12, 0x34 });       // Identification
        frame.AddRange(new byte[] { 0x40, 0x00 });       // Flags=DF / Fragment offset = 0
        frame.Add(64);                                   // TTL
        frame.Add(6);                                    // Protocol = TCP
        frame.AddRange(new byte[] { 0x00, 0x00 });       // Header checksum (zero — dissector doesn't validate)
        frame.AddRange(PS2IP);                           // src
        frame.AddRange(ServerIP);                        // dst

        // TCP
        frame.AddRange(IpAddrBE(srcPort));
        frame.AddRange(IpAddrBE(dstPort));
        frame.AddRange(new byte[] { 0, 0, 0, 1 });       // seq
        frame.AddRange(new byte[] { 0, 0, 0, 0 });       // ack
        frame.Add(0x50);                                 // data offset = 5 (20 bytes)
        frame.Add(0x18);                                 // flags = PSH | ACK
        frame.AddRange(new byte[] { 0xFF, 0xFF });       // window
        frame.AddRange(new byte[] { 0x00, 0x00 });       // checksum
        frame.AddRange(new byte[] { 0x00, 0x00 });       // urgent

        frame.AddRange(tcpPayload);
        return frame.ToArray();
    }

    private static byte[] IpAddrBE(ushort v) => [(byte)(v >> 8), (byte)(v & 0xFF)];
}
