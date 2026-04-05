using System.Text;

namespace CodeDesigner.Ps2.MediusServer.Protocol;

/// <summary>
/// Reads Medius binary fields from a byte buffer using the endianness that the PS2 sends.
/// All multi-byte integers are little-endian.
/// Strings are null-terminated ASCII, padded to a fixed length.
/// </summary>
public class MediusReader
{
    private readonly byte[] _data;
    private int _pos;

    public MediusReader(byte[] data) { _data = data; _pos = 0; }
    public MediusReader(ReadOnlySpan<byte> data) { _data = data.ToArray(); _pos = 0; }

    public int Position => _pos;
    public int Remaining => _data.Length - _pos;

    public byte   ReadByte()  { var v = _data[_pos]; _pos++; return v; }
    public sbyte  ReadSByte() { return (sbyte)ReadByte(); }

    public short  ReadInt16()
    {
        var v = (short)(_data[_pos] | (_data[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public ushort ReadUInt16()
    {
        var v = (ushort)(_data[_pos] | (_data[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public int ReadInt32()
    {
        var v = _data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24);
        _pos += 4;
        return v;
    }

    public uint ReadUInt32()
    {
        var v = (uint)(_data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24));
        _pos += 4;
        return v;
    }

    /// <summary>Read a fixed-length null-terminated ASCII string.</summary>
    public string ReadString(int maxLength)
    {
        int end = _pos + maxLength;
        int nullIdx = -1;
        for (int i = _pos; i < end && i < _data.Length; i++)
        {
            if (_data[i] == 0) { nullIdx = i; break; }
        }
        int strLen = nullIdx >= 0 ? nullIdx - _pos : maxLength;
        var result = Encoding.ASCII.GetString(_data, _pos, strLen);
        _pos += maxLength;
        return result;
    }

    public byte[] ReadBytes(int count)
    {
        var v = new byte[count];
        Buffer.BlockCopy(_data, _pos, v, 0, count);
        _pos += count;
        return v;
    }

    public void Skip(int count) { _pos += count; }
}

/// <summary>
/// Writes Medius binary fields in the encoding expected by the PS2 client.
/// </summary>
public class MediusWriter
{
    private readonly MemoryStream _ms;
    private readonly BinaryWriter _bw;

    public MediusWriter()
    {
        _ms = new MemoryStream();
        _bw = new BinaryWriter(_ms, Encoding.ASCII, leaveOpen: true);
    }

    public void Write(byte v)   => _bw.Write(v);
    public void Write(sbyte v)  => _bw.Write(v);
    public void Write(short v)  => _bw.Write(v);
    public void Write(ushort v) => _bw.Write(v);
    public void Write(int v)    => _bw.Write(v);
    public void Write(uint v)   => _bw.Write(v);

    /// <summary>Write a fixed-length null-terminated ASCII string (padded with 0x00).</summary>
    public void WriteString(string s, int maxLength)
    {
        var encoded = Encoding.ASCII.GetBytes(s ?? string.Empty);
        int writeLen = Math.Min(encoded.Length, maxLength - 1);
        _bw.Write(encoded, 0, writeLen);
        // Pad with zeroes to fill the fixed-length slot
        for (int i = writeLen; i < maxLength; i++)
            _bw.Write((byte)0);
    }

    public void Write(byte[] data) => _bw.Write(data);

    public void WritePadding(int count)
    {
        for (int i = 0; i < count; i++)
            _bw.Write((byte)0);
    }

    public byte[] ToArray()
    {
        _bw.Flush();
        return _ms.ToArray();
    }
}
