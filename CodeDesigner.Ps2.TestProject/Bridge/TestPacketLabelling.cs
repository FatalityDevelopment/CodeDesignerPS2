using System.Buffers.Binary;
using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Services;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.TestProject.Bridge
{
    /// <summary>
    /// The packet viewer labels packets from <see cref="MessageCatalog"/>, which is built from the
    /// multiplayer server's own definitions (SCERT / Medius / MGCL / DME enums) and the SOCOM II
    /// game catalog the bridge embeds from Mods/SOCOM2/packets. These tests check that the catalog
    /// covers every definition and that the dissector labels every catalog message by name.
    /// </summary>
    [TestClass]
    public class TestPacketLabelling
    {
        private const int MlsPort = 10075;
        private const int DmePort = 10070;

        [ClassInitialize]
        public static void Init(TestContext _) => Socom2GameCatalog.Install();

        [TestMethod]
        public void Catalog_CoversEveryServerDefinition()
        {
            var entries = Socom2GameCatalog.Entries;
            int Count(string protocol) => entries.Count(e => e.Protocol == protocol);

            Assert.AreEqual(Enum.GetValues<RT_MSG_TYPE>().Distinct().Count(), Count(MessageCatalog.Scert), "SCERT");
            Assert.AreEqual(Enum.GetValues<MediusLobbyMessageId>().Distinct().Count(), Count(MessageCatalog.Lobby), "Lobby");
            Assert.AreEqual(Enum.GetValues<MediusLobbyExtMessageId>().Distinct().Count(), Count(MessageCatalog.LobbyExt), "LobbyExt");
            Assert.AreEqual(Enum.GetValues<MediusDmeMessageIds>().Distinct().Count(), Count(MessageCatalog.Mgcl), "MGCL");
            Assert.AreEqual(Enum.GetValues<DmeAppMessageId>().Distinct().Count(), Count(MessageCatalog.Dme), "DME");

            // The embedded JSON must be the repo's current files, every entry included.
            string dir = Path.Combine(RepoRoot(), "Mods", "SOCOM2", "packets");
            using var packets = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "packets.json")));
            using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "objects.json")));
            int packetCount = packets.RootElement.GetProperty("packets").GetArrayLength();
            int classCount  = objects.RootElement.GetProperty("classes").EnumerateObject().Count();
            Assert.IsTrue(packetCount > 0 && classCount > 0);
            Assert.AreEqual(packetCount, Count(MessageCatalog.Game), "Game messages (packets.json)");
            Assert.AreEqual(classCount, Count(MessageCatalog.Object), "Object classes (objects.json)");

            // Names are what the viewer shows, so they must be unique within a protocol.
            foreach (var g in entries.GroupBy(e => e.Protocol))
                Assert.AreEqual(g.Count(), g.Select(e => e.Name).Distinct().Count(), $"duplicate {g.Key} names");
        }

        [TestMethod]
        public void EveryCatalogMessage_IsLabelledByName()
        {
            var failures = new List<string>();
            foreach (var entry in Socom2GameCatalog.Entries)
            {
                if (entry.Protocol == MessageCatalog.Object) continue;   // covered by EveryObjectClass_IsLabelled
                var values = new Dictionary<string, string>();
                if (entry.Protocol == MessageCatalog.Dme)
                {
                    // Object messages need a class the game catalog knows and its full record.
                    var cls = Socom2GameCatalog.Game.Classes.Values.First();
                    if (entry.Id == (byte)DmeAppMessageId.ObjectUpdate) values["Class"] = cls.Type.ToString();
                    if (entry.Id == (byte)DmeAppMessageId.ObjectCreate)
                    {
                        values["Class"]   = cls.Type.ToString();
                        values["Payload"] = Convert.ToHexString(new byte[cls.RecordSize]);
                    }
                }

                var summary = Summarize(entry, values);
                if (!summary.Messages.Any(m => m.Contains(entry.Name, StringComparison.Ordinal)))
                    failures.Add($"{entry.Protocol} 0x{entry.Id:X2} {entry.Name} -> [{summary.Protocol}] {summary.Info}");
            }
            Assert.AreEqual(0, failures.Count, "Unlabelled messages:\n" + string.Join("\n", failures));
        }

        [TestMethod]
        public void EveryObjectClass_IsLabelled()
        {
            var update = Socom2GameCatalog.Entries.Single(e => e.Protocol == MessageCatalog.Dme && e.Id == (byte)DmeAppMessageId.ObjectUpdate);
            var create = Socom2GameCatalog.Entries.Single(e => e.Protocol == MessageCatalog.Dme && e.Id == (byte)DmeAppMessageId.ObjectCreate);
            foreach (var cls in Socom2GameCatalog.Game.Classes.Values)
            {
                // Update with every field (index + value), then a create carrying the whole record.
                var body = new List<byte>();
                foreach (var f in cls.Fields) { body.Add((byte)f.Index); body.AddRange(new byte[f.Size]); }
                var up = Summarize(update, new()
                {
                    ["Class"] = cls.Type.ToString(), ["Field Count"] = cls.Fields.Count.ToString(),
                    ["Object Id"] = "3", ["Payload"] = Convert.ToHexString(body.ToArray()),
                });
                CollectionAssert.Contains(up.Messages.ToList(), $"ObjectUpdate {cls.Name} #3", up.Info);

                var cr = Summarize(create, new()
                {
                    ["Class"] = cls.Type.ToString(), ["Object Id"] = "7", ["Object Name"] = "TestObject",
                    ["Payload"] = Convert.ToHexString(new byte[cls.RecordSize]),
                });
                CollectionAssert.Contains(cr.Messages.ToList(), "ObjectCreate TestObject #7", cr.Info);
            }
        }

        [TestMethod]
        public void GameMessage_FieldsRoundTrip()
        {
            var chat = Socom2GameCatalog.Entries.Single(e => e.Protocol == MessageCatalog.Game && e.Name == "chat");
            byte[] app = MessageCatalog.Encode(chat, new Dictionary<string, string> { ["channel"] = "2", ["text"] = "hello" });
            byte[] frame = BuildTcpFrame(DmePort, MessageCatalog.WrapScert(RT_MSG_TYPE.RT_MSG_CLIENT_APP_BROADCAST, app));

            var layers = PacketDissector.Dissect(frame, 0, MediusPortMap.Defaults, out var summary);
            Assert.AreEqual("Game", summary.Protocol);
            Assert.AreEqual("DME", summary.Server);
            var game = layers.Single(l => l.Label.StartsWith("Game [SOCOM II]", StringComparison.Ordinal));
            Assert.AreEqual("11", game.Fields.Single(f => f.Name == "size").Value, "auto size = len(text)+6");
            Assert.AreEqual("2", game.Fields.Single(f => f.Name == "channel").Value);
            Assert.AreEqual("\"hello\"", game.Fields.Single(f => f.Name == "text").Value);
        }

        [TestMethod]
        public void DmeRelay_AppSingleFromServer_IsDecoded()
        {
            // Server → client relay: CLIENT_APP_SINGLE [source u16][payload] (DmeWorld.Forward).
            var app = new List<byte> { 0x00, (byte)DmeAppMessageId.ObjectDestroy, 0x21, 0x00, 0x00, (byte)DmeAppMessageId.EndOfBatch };
            byte[] frame = BuildTcpFrame(DmePort, MessageCatalog.WrapScert(RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE, app.ToArray(), target: 1), fromServer: true);
            var summary = PacketDissector.Summarize(frame, 1);
            CollectionAssert.AreEqual(new[] { "ObjectDestroy #33", "EndOfBatch" }, summary.Messages.ToArray());
            Assert.AreEqual("DME", summary.Protocol);
        }

        [TestMethod]
        public void FrameBuilder_ReplacePayload_FixesLengthsAndChecksums()
        {
            byte[] template = BuildTcpFrame(MlsPort, [1, 2, 3]);
            byte[] frame = FrameBuilder.ReplacePayload(template, new byte[101]);
            Assert.AreEqual(14 + 20 + 20 + 101, frame.Length);
            Assert.AreEqual(20 + 20 + 101, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(16)));
            Assert.AreEqual(0xFFFF, OnesSum(frame.AsSpan(14, 20)), "IPv4 header checksum");

            // TCP checksum over pseudo-header + segment sums to 0xFFFF when valid.
            var pseudo = new List<byte>();
            pseudo.AddRange(frame.AsSpan(26, 8).ToArray());
            pseudo.AddRange(new byte[] { 0, 6, 0, 20 + 101 });
            pseudo.AddRange(frame.AsSpan(34).ToArray());
            Assert.AreEqual(0xFFFF, OnesSum(pseudo.ToArray()), "TCP checksum");

            FrameBuilder.SetTcpSeqAck(frame, 0x01020304, 0x0A0B0C0D);
            Assert.IsTrue(FrameBuilder.TryReadTcp(frame, out var seg));
            Assert.AreEqual(0x01020304u, seg.Seq);
            Assert.AreEqual(0x0A0B0C0Du, seg.Ack);
            Assert.AreEqual(0x01020304u + 101, seg.NextSeq);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static PacketSummary Summarize(CatalogEntry entry, Dictionary<string, string> values)
        {
            byte[] body = MessageCatalog.Encode(entry, values);
            RT_MSG_TYPE rt = entry.Protocol switch
            {
                MessageCatalog.Scert => (RT_MSG_TYPE)entry.Id,
                MessageCatalog.Dme or MessageCatalog.Game => RT_MSG_TYPE.RT_MSG_CLIENT_APP_BROADCAST,
                _ => RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER,
            };
            int port = entry.Protocol is MessageCatalog.Dme or MessageCatalog.Game ? DmePort : MlsPort;
            return PacketDissector.Summarize(BuildTcpFrame(port, MessageCatalog.WrapScert(rt, body)), 0);
        }

        /// <summary>Ethernet + IPv4 + TCP frame between the PS2 and a server port.</summary>
        private static byte[] BuildTcpFrame(int serverPort, byte[] payload, bool fromServer = false)
        {
            var f = new byte[14 + 20 + 20];
            f[12] = 0x08;
            f[14] = 0x45;
            f[22] = 64;
            f[23] = 6;
            byte[] ps2 = [192, 168, 1, 100], server = [192, 168, 1, 1];
            (fromServer ? server : ps2).CopyTo(f, 26);
            (fromServer ? ps2 : server).CopyTo(f, 30);
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(34), (ushort)(fromServer ? serverPort : 49152));
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(36), (ushort)(fromServer ? 49152 : serverPort));
            f[46] = 0x50;
            f[47] = 0x18;
            return FrameBuilder.ReplacePayload(f, payload);
        }

        private static int OnesSum(ReadOnlySpan<byte> data)
        {
            long sum = 0;
            for (int i = 0; i + 1 < data.Length; i += 2) sum += (data[i] << 8) | data[i + 1];
            if ((data.Length & 1) != 0) sum += data[^1] << 8;
            while ((sum >> 16) != 0) sum = (sum & 0xFFFF) + (sum >> 16);
            return (int)sum;
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "CodeDesigner.sln"))) return dir.FullName;
            Assert.Inconclusive("Repository root (CodeDesigner.sln) not found above the test output folder");
            return "";
        }
    }
}
