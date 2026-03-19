namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Describes one ELF (or IRX) file found on a disc image, returned by LISTDISCELFS.
    /// </summary>
    public class DiscElfEntry
    {
        /// <summary>Absolute disc path, e.g. "/SCUS_971.98;1" or "/MODULES/CDVD.IRX;1".</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Filename without the path prefix or version suffix, e.g. "SCUS_971.98".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        public uint Size { get; set; }

        /// <summary>True when this is the BOOT2 executable listed in SYSTEM.CNF.</summary>
        public bool IsBootElf { get; set; }
    }
}
