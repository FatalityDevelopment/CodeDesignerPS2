namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>Severity level of a <see cref="CdsDiagnostic"/>.</summary>
    public enum CdsDiagnosticSeverity
    {
        /// <summary>Informational message — no action required.</summary>
        Info,
        /// <summary>Potential problem that still allows compilation.</summary>
        Warning,
        /// <summary>Hard error; the affected line will emit a zero word or be skipped.</summary>
        Error
    }

    /// <summary>
    /// A structured diagnostic message produced by <see cref="CdsParser"/> or
    /// <see cref="CdsPreprocessor"/> during source analysis.
    /// </summary>
    public sealed class CdsDiagnostic
    {
        /// <summary>Severity of the diagnostic.</summary>
        public CdsDiagnosticSeverity Severity { get; init; }

        /// <summary>1-based source line number where the issue was detected.</summary>
        public int LineNumber { get; init; }

        /// <summary>Human-readable description of the issue.</summary>
        public string Message { get; init; } = string.Empty;

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Severity} (line {LineNumber}): {Message}";
    }
}
