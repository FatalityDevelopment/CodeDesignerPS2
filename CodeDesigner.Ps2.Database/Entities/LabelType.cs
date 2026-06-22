namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Semantic type of a <see cref="Label"/>.
/// Mirrors <c>CodeDesigner.Ps2.LabelManager.Models.LabelType</c>.
/// </summary>
public enum LabelType
{
    Address        = 0,
    Struct         = 1,
    Function       = 2,
    Array          = 3,
    ArrayOfPointers = 4,
    LinkedList     = 5,
    String         = 6,
    Pointer        = 7,
    Float          = 8,
    Value          = 9,
}
