namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// A single call-site record produced by the TRACECALLS command.
    /// Captures the EE program counter of the JAL/JALR instruction (From) and
    /// the call target address resolved after the branch delay slot executes (To).
    /// </summary>
    public class CallTraceEntry
    {
        /// <summary>
        /// EE address of the JAL or JALR instruction, as an 8-digit hex string
        /// normalised to KSEG0 (e.g. "80001234").
        /// </summary>
        public string From { get; set; } = string.Empty;

        /// <summary>
        /// EE call-target address resolved after the delay slot, as an 8-digit hex
        /// string normalised to KSEG0 (e.g. "80005678").
        /// </summary>
        public string To { get; set; } = string.Empty;
    }
}
