namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// How a region of memory should be displayed in the disassembler.
///
/// <para>
/// When no override exists for an address the disassembler falls back to the
/// default 4-byte instruction decoding.  An override of <see cref="Word"/> is
/// distinct from "no override": it forces the row to be rendered as a
/// <c>.word</c> data directive (with optional string resolution in the comment
/// column) instead of being decoded as a MIPS instruction.
/// </para>
///
/// <para>The byte size of each kind is fixed:</para>
/// <list type="bullet">
///   <item><see cref="Byte"/>  – 1 byte  (<c>.byte</c>)</item>
///   <item><see cref="Half"/>  – 2 bytes (<c>.half</c>)</item>
///   <item><see cref="Word"/>  – 4 bytes (<c>.word</c>)</item>
///   <item><see cref="DWord"/> – 8 bytes (<c>.dword</c>)</item>
///   <item><see cref="QWord"/> – 16 bytes (<c>.qword</c>)</item>
/// </list>
/// </summary>
public enum DataFormatKind
{
    Byte  = 0,
    Half  = 1,
    Word  = 2,
    DWord = 3,
    QWord = 4,
}
