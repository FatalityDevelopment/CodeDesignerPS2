using System.Text;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using CodeDesigner.Ps2.Multiplayer.Servers;
using CodeDesigner.Ps2.Multiplayer.Storage;

namespace CodeDesigner.AI.Tests;

/// <summary>
/// Wire-format pins for the MLS lobby replies the SOCOM II (SCUS-97275 r0004) client parses — offsets come from
/// the client's own response callbacks in the r0004 EE dump (see MlsServer.Lobby.cs / MlsServer.Chat.cs).
/// </summary>
[TestClass]
public class MlsLobbyWireTests
{
    private static MediusPacket Request(string messageId, int bodyLength)
    {
        var payload = new byte[bodyLength];
        Encoding.ASCII.GetBytes(messageId).CopyTo(payload, 0);
        return MediusPacket.BuildLobby(MediusLobbyMessageId.FindWorldByName, payload);
    }

    private static int Int(byte[] b, int at) => BitConverter.ToInt32(b, at);

    private static string Str(byte[] b, int at, int len)
    {
        int end = Array.IndexOf(b, (byte)0, at, len);
        return Encoding.ASCII.GetString(b, at, (end < 0 ? at + len : end) - at);
    }

    [TestMethod]
    public void FindWorldByName_ParsesV108Layout_NameAt0x26_TypeAt0x68()
    {
        // MessageID[21] | SessionKey[17] | Name[64] @0x26 | pad2 | WorldType @0x68 = 108 bytes (the client's sender).
        var payload = new byte[108];
        Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOP.3").CopyTo(payload, 0);
        Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOP").CopyTo(payload, 21);
        Encoding.ASCII.GetBytes("US EAST").CopyTo(payload, 0x26);
        BitConverter.GetBytes(MlsServer.FindWorldTypeLobby).CopyTo(payload, 0x68);

        var (name, type) = MlsServer.ParseFindWorldByName(payload);

        Assert.AreEqual("US EAST", name);
        Assert.AreEqual(MlsServer.FindWorldTypeLobby, type);
    }

    [TestMethod]
    public void FindWorldByNameResponse_Is0x9CBytes_WithWorldIdAt0x44()
    {
        var packet = Request("ABCDEFGHIJKLMNOP.7", 108);

        var body = MlsServer.BuildFindWorldByNameResponse(packet, MediusCallbackStatus.MediusSuccess, 10683,
            worldId: 0x44, worldName: "US EAST", worldStatus: 2, playerCount: 3, maxPlayers: 256, gameWorldCount: 1);

        Assert.AreEqual(MlsServer.FindWorldByNameResponseSize, body.Length);
        Assert.AreEqual("ABCDEFGHIJKLMNOP.7", Str(body, 0, 21), "MessageID echo");
        Assert.AreEqual(0, Int(body, 0x18), "StatusCode @0x18");
        Assert.AreEqual(0x44, Int(body, 0x44), "MediusWorldID @0x44 (what the client's callback reads)");
        Assert.AreEqual("US EAST", Str(body, 0x48, 64));
        Assert.AreEqual(3, Int(body, 0x8C));
        Assert.AreEqual(256, Int(body, 0x90));
        Assert.AreEqual(1, body[0x98], "EndOfList");
    }

    [TestMethod]
    public void FindWorldByNameResponse_NotFound_KeepsFullSizeAndStatus()
    {
        var packet = Request("ABCDEFGHIJKLMNOP.8", 108);

        var body = MlsServer.BuildFindWorldByNameResponse(packet, MediusCallbackStatus.MediusChannelNotFound, 0, 0, "", 0, 0, 0, 0);

        Assert.AreEqual(MlsServer.FindWorldByNameResponseSize, body.Length);
        Assert.AreEqual((int)MediusCallbackStatus.MediusChannelNotFound, Int(body, 0x18));
        Assert.AreEqual(0, Int(body, 0x44));
    }

    [TestMethod]
    public void AddToBuddyListConfirmationResponse_Is0x48Bytes_RequestAccepted()
    {
        var packet = Request("ABCDEFGHIJKLMNOP.9", 48);

        var body = MlsServer.BuildAddToBuddyListConfirmationResponse(packet, MediusCallbackStatus.MediusRequestAccepted, 7, "Masquarr");

        Assert.AreEqual(MlsServer.AddToBuddyListConfirmationResponseSize, body.Length);
        Assert.AreEqual(2, Int(body, 0x18), "the client's callback only proceeds on MediusRequestAccepted (2)");
        Assert.AreEqual(7, Int(body, 0x1C));
        Assert.AreEqual("Masquarr", Str(body, 0x20, 32));
    }

    [TestMethod]
    public void SpectatorJoin_UsesSpectatorPassword_NotGamePassword()
    {
        var game = new MediusGame { Password = "players", SpectatorPassword = "" };
        Assert.IsTrue(MlsServer.PasswordAccepted(game, spectator: true, presented: ""), "open spectator seats");
        Assert.IsFalse(MlsServer.PasswordAccepted(game, spectator: false, presented: ""), "players still need the game password");
        Assert.IsTrue(MlsServer.PasswordAccepted(game, spectator: false, presented: "players"));

        game.SpectatorPassword = "watch";
        Assert.IsFalse(MlsServer.PasswordAccepted(game, spectator: true, presented: "players"));
        Assert.IsTrue(MlsServer.PasswordAccepted(game, spectator: true, presented: "watch"));
    }

    [TestMethod]
    public void DmeWorld_ReserveMarksSpectators_AndCountsThem()
    {
        var world = new DmeWorld(mediusWorldId: 0x4402, appId: 10683, maxPlayers: 24);
        try
        {
            var player = world.Reserve("SESSION-A", 1);
            var watcher = world.Reserve("SESSION-B", 2, spectator: true);
            Assert.IsNotNull(player);
            Assert.IsNotNull(watcher);
            Assert.IsFalse(player!.IsSpectator);
            Assert.IsTrue(watcher!.IsSpectator);
            Assert.AreEqual(0, world.SpectatorCount, "only connected slots count");

            player.IsConnected = true;
            watcher.IsConnected = true;
            Assert.AreEqual(2, world.ConnectedCount);
            Assert.AreEqual(1, world.SpectatorCount);
        }
        finally
        {
            world.Destroy();
        }
    }

    [TestMethod]
    public void Presence_VirtualPlayers_StayUntilRemoved()
    {
        var presence = new PresenceRegistry();
        presence.AddVirtual(42, 0x44, "US EAST");
        presence.Connected(43, 0x44, "US EAST");

        CollectionAssert.AreEquivalent(new[] { 42, 43 }, presence.AccountsInLobby(0x44));
        Assert.IsTrue(presence.IsVirtual(42));
        Assert.IsFalse(presence.IsVirtual(43));

        presence.Disconnected(42);   // a stray disconnect must not drop a virtual player
        presence.Disconnected(43);
        CollectionAssert.AreEquivalent(new[] { 42 }, presence.AccountsInLobby(0x44));

        Assert.IsTrue(presence.RemoveVirtual(42));
        Assert.IsFalse(presence.RemoveVirtual(42));
        Assert.AreEqual(0, presence.OnlineCount);
    }
}
