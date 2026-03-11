namespace CodeDesigner.Mips64LE.CheatConverter
{
    /// <summary>
    /// A single decoded PS2 memory-write cheat entry in raw (unencrypted) form.
    /// <para>
    /// <see cref="Address"/> carries the full 32-bit code word in the canonical
    /// GameShark/Action Replay hex format: the top 8 bits encode the code type
    /// (e.g. <c>0x00</c> = 8-bit write, <c>0x10</c> = 16-bit, <c>0x20</c> = 32-bit)
    /// and the lower 24 bits are the address offset from the PS2 EE user-memory base
    /// (<c>0x20000000</c>).  <see cref="Value"/> is the 32-bit data field.
    /// </para>
    /// </summary>
    public readonly record struct RawCode(uint Address, uint Value)
    {
        /// <summary>
        /// Formats the code as a standard two-field hex string, e.g. <c>200133AC 000003E8</c>.
        /// </summary>
        public override string ToString() => $"{Address:X8} {Value:X8}";
    }
}
