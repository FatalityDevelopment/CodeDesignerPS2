using System.Buffers.Binary;
using System.Text;
using CodeDesigner.Ps2.HddManager;

namespace CodeDesigner.Ps2.TestProject.HddManager;

[TestClass]
public sealed class TestPs2HddImage
{
    private const int SectorSize = 512;
    private const int ApaHeaderSize = 1024;
    private const int PfsSuperSector = 8192;
    private const int PfsZoneSize = 0x2000;
    private const uint ApaMagic = 0x00415041;
    private const uint PfsMagic = 0x50465300;
    private const uint SegdMagic = 0x53454744;
    private const ushort FioModeDirectory = 0x1000;
    private const ushort FioModeRegular = 0x2000;

    [TestMethod]
    public void ListAndExportFilesReadsPs2SdkPfsLayout()
    {
        string workDir = Path.Combine(Path.GetTempPath(), "codedesigner-hdd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            string hddPath = Path.Combine(workDir, "test.raw");
            string exportDir = Path.Combine(workDir, "export");
            const string partitionId = "PP.SCUS-97275..SOCOM_II";
            byte[] fileBytes = Encoding.ASCII.GetBytes("hello from pfs");

            CreateSyntheticHdd(hddPath, partitionId, fileBytes);

            using var hdd = Ps2HddImage.Open(hddPath);
            var files = hdd.ListFiles(partitionId);

            Assert.AreEqual(1, files.Length);
            Assert.AreEqual("HELLO.TXT", files[0].Name);
            Assert.AreEqual(fileBytes.Length, files[0].Size);
            Assert.IsFalse(files[0].IsDirectory);

            hdd.ExportFile(partitionId, "HELLO.TXT", exportDir);
            CollectionAssert.AreEqual(fileBytes, File.ReadAllBytes(Path.Combine(exportDir, "HELLO.TXT")));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    private static void CreateSyntheticHdd(string hddPath, string partitionId, byte[] fileBytes)
    {
        const uint partitionStart = 2048;
        const uint partitionLength = 32768;

        using var fs = new FileStream(hddPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        fs.SetLength(((long)partitionStart + partitionLength) * SectorSize);

        var mbr = new byte[ApaHeaderSize];
        WriteApaHeader(mbr, "__mbr", start: 0, length: partitionStart, type: 0x1337, next: partitionStart, prev: 0);
        fs.Position = 0;
        fs.Write(mbr);

        var part = new byte[ApaHeaderSize];
        WriteApaHeader(part, partitionId, partitionStart, partitionLength, type: 0x0100, next: 0, prev: 0);
        fs.Position = (long)partitionStart * SectorSize;
        fs.Write(part);

        long partBase = (long)partitionStart * SectorSize;
        WriteSuperblock(fs, partBase);
        WriteRootInode(fs, partBase);
        WriteRootDirectory(fs, partBase);
        WriteFileInode(fs, partBase, fileBytes.Length);

        fs.Position = partBase + 6L * PfsZoneSize;
        fs.Write(fileBytes);
    }

    private static void WriteApaHeader(byte[] header, string id, uint start, uint length, ushort type, uint next, uint prev)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), ApaMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x08), next);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x0C), prev);
        WriteAscii(header, 0x10, id, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x40), start);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x44), length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x48), type);
    }

    private static void WriteSuperblock(FileStream fs, long partBase)
    {
        Span<byte> sb = stackalloc byte[SectorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(sb, PfsMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(sb.Slice(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(sb.Slice(16), PfsZoneSize);
        WriteBlockInfo(sb.Slice(24), number: 2, subpart: 0, count: 1);
        WriteBlockInfo(sb.Slice(32), number: 3, subpart: 0, count: 1);

        fs.Position = partBase + (long)PfsSuperSector * SectorSize;
        fs.Write(sb);
    }

    private static void WriteRootInode(FileStream fs, long partBase)
    {
        var inode = CreateInode(
            inodeNumber: 3,
            dataNumber: 4,
            mode: (ushort)(FioModeDirectory | 0x01FF),
            attr: 0x00A0,
            size: SectorSize);

        fs.Position = partBase + 3L * PfsZoneSize;
        fs.Write(inode);
    }

    private static void WriteRootDirectory(FileStream fs, long partBase)
    {
        var dir = new byte[PfsZoneSize];
        int offset = 0;

        WriteDentry(dir, ref offset, inode: 3, name: ".", mode: FioModeDirectory, allocatedLength: 12);
        WriteDentry(dir, ref offset, inode: 3, name: "..", mode: FioModeDirectory, allocatedLength: 12);
        WriteDentry(dir, ref offset, inode: 5, name: "HELLO.TXT", mode: FioModeRegular, allocatedLength: 20);

        fs.Position = partBase + 4L * PfsZoneSize;
        fs.Write(dir);
    }

    private static void WriteFileInode(FileStream fs, long partBase, int fileSize)
    {
        var inode = CreateInode(
            inodeNumber: 5,
            dataNumber: 6,
            mode: (ushort)(FioModeRegular | 0x01FF),
            attr: 0x0080,
            size: fileSize);

        fs.Position = partBase + 5L * PfsZoneSize;
        fs.Write(inode);
    }

    private static byte[] CreateInode(uint inodeNumber, uint dataNumber, ushort mode, ushort attr, long size)
    {
        var inode = new byte[1024];
        BinaryPrimitives.WriteUInt32LittleEndian(inode.AsSpan(4), SegdMagic);
        WriteBlockInfo(inode.AsSpan(8), inodeNumber, 0, 1);
        WriteBlockInfo(inode.AsSpan(24), inodeNumber, 0, 1);
        WriteBlockInfo(inode.AsSpan(40), inodeNumber, 0, 1);
        WriteBlockInfo(inode.AsSpan(48), dataNumber, 0, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(inode.AsSpan(0x3B8), mode);
        BinaryPrimitives.WriteUInt16LittleEndian(inode.AsSpan(0x3BA), attr);
        BinaryPrimitives.WriteInt64LittleEndian(inode.AsSpan(0x3D8), size);
        BinaryPrimitives.WriteUInt32LittleEndian(inode.AsSpan(0x3E0), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(inode.AsSpan(0x3E4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(inode.AsSpan(0x3E8), 1);
        return inode;
    }

    private static void WriteDentry(byte[] dir, ref int offset, uint inode, string name, ushort mode, ushort allocatedLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dir.AsSpan(offset), inode);
        dir[offset + 5] = (byte)name.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(offset + 6), (ushort)(mode | allocatedLength));
        Encoding.ASCII.GetBytes(name, dir.AsSpan(offset + 8, name.Length));
        offset += allocatedLength;
    }

    private static void WriteBlockInfo(Span<byte> span, uint number, ushort subpart, ushort count)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span, number);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), subpart);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), count);
    }

    private static void WriteAscii(byte[] buffer, int offset, string value, int maxLength)
    {
        Encoding.ASCII.GetBytes(value, buffer.AsSpan(offset, Math.Min(value.Length, maxLength)));
    }
}
