using System.Text;

namespace CodeDesigner.Ps2.FileFormats.Ps2Dis;

/// <summary>
/// One raw label record read from a .pis file.
/// The 0x18-byte record is PS2Dis internal format; we preserve it verbatim
/// so callers can decode it or pass it to a porter later.
/// </summary>
public sealed class PisLabelRecord
{
    public uint   EeAddress { get; }
    public byte[] Raw       { get; }  // always PisFile.RecordSize (0x18) bytes

    public string RawHex => BitConverter.ToString(Raw).Replace("-", "");

    public PisLabelRecord(uint eeAddress, byte[] raw)
    {
        EeAddress = eeAddress;
        Raw       = raw;
    }
}
