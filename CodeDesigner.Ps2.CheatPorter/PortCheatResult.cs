using System.Collections.Generic;

namespace CodeDesigner.Ps2.CheatPorter
{
    /// <summary>
    /// Result for a single cheat code line produced by <see cref="CheatPorter.Port"/>.
    /// </summary>
    public class PortCheatResult
    {
        /// <summary>The #Name label of the cheat block this line belongs to.</summary>
        public string BlockName { get; set; } = string.Empty;

        /// <summary>Any non-label, non-code description text found in the cheat block above the codes. Null when absent.</summary>
        public string? BlockText { get; set; }

        /// <summary>Original GameShark code line, e.g. "20123456 000003E8".</summary>
        public string OriginalCode { get; set; } = string.Empty;

        /// <summary>Ported code line with the updated address, e.g. "20124ABC 000003E8". Null when not found.</summary>
        public string? NewCode { get; set; }

        /// <summary>Whether a match was found in the target dump.</summary>
        public bool Found { get; set; }

        /// <summary>Description of the method and window size used, e.g. "HexPattern-48".</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>0–100 confidence score. 100 = unique match; lower when multiple candidates were found.</summary>
        public int Confidence { get; set; }

        /// <summary>True when more than one candidate address was found in the target dump.</summary>
        public bool Ambiguous { get; set; }

        /// <summary>All candidate new code lines when Ambiguous is true.</summary>
        public List<string>? Candidates { get; set; }
    }
}
