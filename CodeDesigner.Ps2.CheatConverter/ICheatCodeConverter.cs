using System.Collections.Generic;

namespace CodeDesigner.Ps2.CheatConverter
{
    /// <summary>
    /// Converts PS2 cheat codes between a device-specific format and the canonical
    /// unencrypted raw format (<see cref="RawCode"/> pairs).
    /// </summary>
    public interface ICheatCodeConverter
    {
        /// <summary>
        /// Decodes a block of device-formatted cheat code text into raw address/value pairs.
        /// Lines that cannot be decoded (comments, blank lines, unrecognised tokens) are silently skipped.
        /// </summary>
        /// <param name="input">Multi-line string of cheat codes in this converter's format.</param>
        /// <returns>List of decoded <see cref="RawCode"/> entries in the order they were encountered.</returns>
        IReadOnlyList<RawCode> Decode(string input);

        /// <summary>
        /// Encodes a sequence of raw address/value pairs into this converter's device format.
        /// </summary>
        /// <param name="codes">Raw codes to encode.</param>
        /// <returns>Multi-line string suitable for pasting into the target cheat device or file.</returns>
        string Encode(IEnumerable<RawCode> codes);
    }
}
