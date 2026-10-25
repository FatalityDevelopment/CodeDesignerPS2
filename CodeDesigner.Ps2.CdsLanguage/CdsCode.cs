namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// One raw cheat code pair produced by <see cref="CdsCompiler.CompileToCodes"/>,
    /// tagged with the source line it came from.
    /// </summary>
    /// <param name="CodeWord">First word (type nibble + address, or a code-specific header).</param>
    /// <param name="ValueWord">Second word (value or code-specific operand).</param>
    /// <param name="LineNumber">1-based source line.</param>
    /// <param name="LineText">Trimmed source text of that line.</param>
    public readonly record struct CdsCode(uint CodeWord, uint ValueWord, int LineNumber, string LineText)
    {
        /// <summary>The code in the usual "AAAAAAAA VVVVVVVV" text form.</summary>
        public override string ToString() => $"{CodeWord:X8} {ValueWord:X8}";
    }
}
