using System.Buffers.Binary;
using CodeDesigner.Ps2.Multiplayer.Dnas;

namespace CodeDesigner.AI.Tests;

/// <summary>
/// DnaSDK takes each title's original RSA-signed DNAS ticket from the captured packet files shipped with the
/// multiplayer server (Data/&lt;region&gt;/packets), not from GitHub. Pins that the local lookup finds SOCOM II's
/// S1 and that the ticket it yields verifies against SOCOM II's public RSA modulus.
/// </summary>
[TestClass]
public class DnasPacketTicketTests
{
    // SOCOM II (SCUS-97275) C1 lookup ID; its S1 is Data/us-gw/packets/daf6dd91c5458ad0_01080000.
    private static readonly byte[] Socom2LookupId = Convert.FromHexString("daf6dd91c5458ad0");

    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");

    [TestMethod]
    public void LocalRepository_FindsSocom2S1InPacketFiles()
    {
        var repository = new DnasRepRepository(DataDir, "us-gw");

        byte[] s1 = repository.FetchS1Template(BuildC1(Socom2LookupId));

        Assert.AreEqual(328, s1.Length);
        Assert.AreEqual(0x01080005u, BinaryPrimitives.ReadUInt32BigEndian(s1));
        Assert.AreEqual(Path.GetFullPath(DataDir), repository.Origin);
    }

    [TestMethod]
    public void LocalRepository_RelativePathResolvesNextToBinary()
    {
        var repository = new DnasRepRepository("Data");

        Assert.AreEqual(Path.GetFullPath(DataDir), repository.Origin);
    }

    [TestMethod]
    public void TicketStore_TakesSocom2TicketFromPacketsAndItVerifies()
    {
        string root = Path.Combine(Path.GetTempPath(), "dnas-ticket-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TicketStore(root, DataDir, "us-gw");

            byte[] ticket = store.GetOrFetch("SCUS97275", BuildC1(Socom2LookupId));

            byte[] modulus = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Moduli", "SCUS97275.BIN"));
            Assert.AreEqual(117, RSA.VerifyServerOperand(ticket, modulus).Length);
            CollectionAssert.AreEqual(ticket, File.ReadAllBytes(Path.Combine(root, "Signatures", "SCUS97275.BIN")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void LocalRepository_UnknownLookupIdIsNotFound()
    {
        var repository = new DnasRepRepository(DataDir);

        Assert.ThrowsException<FileNotFoundException>(() =>
            repository.FetchS1Template(BuildC1(Convert.FromHexString("0000000000000000"))));
    }

    /// <summary>A canonical 0108 C1 (40-byte header + 268-byte body) carrying the given lookup ID.</summary>
    private static C1Packet BuildC1(byte[] lookupId)
    {
        byte[] wire = new byte[308];
        BinaryPrimitives.WriteUInt32BigEndian(wire, 0x01080000);
        BinaryPrimitives.WriteInt32BigEndian(wire.AsSpan(0x24), 268);
        BinaryPrimitives.WriteUInt32BigEndian(wire.AsSpan(0x28), 0x00020005);
        lookupId.CopyTo(wire, 0x2C);
        return (C1Packet)DnasPackets.Parse(wire);
    }
}
