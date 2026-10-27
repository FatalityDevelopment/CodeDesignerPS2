using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using CodeDesigner.Ps2.Multiplayer.Servers;

namespace CodeDesigner.AI.Tests;

/// <summary>
/// A second player joining a SOCOM II game room: the host must keep its DME slot, the joiner must get the next free
/// one, and the CONNECT_ACCEPT / CONNECT_COMPLETE values and the host's CONNECT_NOTIFY must say so. The SOCOM II DME
/// client reads its own index from CONNECT_ACCEPT offset 3 and the client count from CONNECT_COMPLETE; a count of 1
/// made the joiner take over the host's lobby slot and froze both consoles.
/// </summary>
[TestClass]
public class DmeJoinSlotTests
{
    private sealed record Sent(RT_MSG_TYPE Type, byte[] Payload);

    private static (DmeClientState Slot, List<Sent> Inbox, (ushort PlayerId, ushort PlayerCount) Ids) Connect(
        DmeWorld world, string sessionKey, int accountId, string ip)
    {
        var reserved = world.Reserve(sessionKey, accountId);
        Assert.IsNotNull(reserved, $"MLS JoinGame reserve for {sessionKey}");

        var inbox = new List<Sent>();
        var state = new MediusClientState { AccessKey = reserved!.AccessKey, ClientCryptKey = Enumerable.Repeat((byte)accountId, 64).ToArray() };
        var slot  = DmeServer.BindConnection(world, state, ip, (type, payload) => { inbox.Add(new Sent(type, payload)); return Task.CompletedTask; });
        Assert.AreSame(reserved, slot, "the DME connect binds the slot MLS reserved");
        Assert.AreEqual(slot!.DmeId, state.DmeClientIndex);

        var ids = DmeServer.ConnectIds(slot);
        DmeServer.CompleteJoin(slot);
        return (slot, inbox, ids);
    }

    private static ushort U16(byte[] b, int at) => BitConverter.ToUInt16(b, at);

    [TestMethod]
    public void SecondPlayerJoin_GetsNextSlot_AndBothSidesSeeTwoPlayers()
    {
        bool notifications = DmeClientState.PlayerNotificationsEnabled;
        DmeClientState.PlayerNotificationsEnabled = true;
        var world = DmeServer.CreateWorld(mediusWorldId: 0x1234, appId: 10683, maxPlayers: 16);
        try
        {
            // Host: CreateGame then JoinGame of its own world → slot 0, alone.
            var host = Connect(world, "HOSTSESSIONKEY01", 1, "192.168.1.10");
            Assert.AreEqual(0, host.Slot.DmeId);
            Assert.AreEqual(((ushort)0, (ushort)1), host.Ids, "host CONNECT_ACCEPT (PlayerId, PlayerCount)");
            Assert.AreEqual(1, U16(ScertPacket.CreateConnectComplete(host.Ids.PlayerCount).Payload, 0),
                "host CONNECT_COMPLETE count (alone → session master; the hardware-confirmed value)");
            Assert.AreEqual(0, host.Inbox.Count, "nobody to announce to the host yet");

            // Joiner.
            var joiner = Connect(world, "JOINSESSIONKEY02", 2, "192.168.1.20");
            Assert.AreEqual(1, joiner.Slot.DmeId, "joiner takes the next free slot");
            Assert.AreEqual(0, host.Slot.DmeId, "host keeps slot 0");
            Assert.AreEqual(((ushort)1, (ushort)2), joiner.Ids, "joiner CONNECT_ACCEPT (PlayerId, PlayerCount)");

            var accept = ScertPacket.CreateConnectAcceptTcp(7, joiner.Ids.PlayerId, joiner.Ids.PlayerCount, null);
            Assert.AreEqual(23, accept.Payload.Length);
            Assert.AreEqual(1, U16(accept.Payload, 3), "own index @3 (client ctx+0x58)");
            var complete = ScertPacket.CreateConnectComplete(joiner.Ids.PlayerCount);
            Assert.AreEqual(2, U16(complete.Payload, 0), "joiner CONNECT_COMPLETE count (client ctx+0x5A) must not be 1");

            // The host is told about slot 1 — once, with the joiner's index, IP and key; never its own index.
            var notify = host.Inbox.Single();
            Assert.AreEqual(RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_NOTIFY, notify.Type);
            Assert.AreEqual(82, notify.Payload.Length, "v108 CONNECT_NOTIFY is exactly 82 bytes");
            Assert.AreEqual(1, U16(notify.Payload, 0), "notify carries the joiner's index, not the host's");
            Assert.AreEqual("192.168.1.20", System.Text.Encoding.ASCII.GetString(notify.Payload, 2, 16).TrimEnd('\0'));
            Assert.AreEqual(2, notify.Payload[18], "joiner's public key");

            // The joiner is not notified about itself.
            Assert.AreEqual(0, joiner.Inbox.Count);

            // Relay addressing: host → slot 1 reaches the joiner, tagged with source index 0.
            world.SendAppSingle(host.Slot, 1, new byte[] { 0xAA });
            var relayed = joiner.Inbox.Single();
            Assert.AreEqual(RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE, relayed.Type);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0xAA }, relayed.Payload);

            // A third player still gets a fresh slot.
            var third = Connect(world, "THIRDSESSIONKEY3", 3, "192.168.1.30");
            Assert.AreEqual(2, third.Slot.DmeId);
            Assert.AreEqual(3, third.Ids.PlayerCount);
            Assert.AreEqual(2, host.Inbox.Count(s => s.Type == RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_NOTIFY));
            Assert.AreEqual(2, U16(joiner.Inbox.Last().Payload, 0));
        }
        finally
        {
            world.Destroy();
            DmeClientState.PlayerNotificationsEnabled = notifications;
        }
    }

    [TestMethod]
    public void RepeatedJoinGame_KeepsEachPlayersOwnSlot()
    {
        var world = DmeServer.CreateWorld(mediusWorldId: 0x1235, appId: 10683, maxPlayers: 16);
        try
        {
            var host   = Connect(world, "HOSTSESSIONKEY01", 1, "10.0.0.1");
            var joiner = Connect(world, "JOINSESSIONKEY02", 2, "10.0.0.2");

            // A repeated JoinGame by the joiner (same session) keeps its slot instead of taking another.
            Assert.AreSame(joiner.Slot, world.Reserve("JOINSESSIONKEY02", 2));
            Assert.AreEqual(1, joiner.Slot.DmeId);
            Assert.AreEqual(0, host.Slot.DmeId);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, world.Clients.Keys.ToArray());
        }
        finally
        {
            world.Destroy();
        }
    }
}
