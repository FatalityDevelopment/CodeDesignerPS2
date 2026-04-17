namespace CodeDesigner.Ps2.Ps2Dis;

/// <summary>
/// One label record read from a .pis file.
/// Each record contains the EE virtual address and the ASCII label name.
/// </summary>
public sealed class PisLabelRecord
{
    public uint   EeAddress { get; }
    public string Name      { get; }

    public PisLabelRecord(uint eeAddress, string name)
    {
        EeAddress = eeAddress;
        Name      = name;
    }
}
