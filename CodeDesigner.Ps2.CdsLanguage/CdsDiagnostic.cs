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

        /// <summary>1-based start column where the issue begins.</summary>
        public int ColumnNumber { get; init; } = 1;

        /// <summary>1-based end line number for the diagnostic span.</summary>
        public int EndLineNumber { get; init; }

        /// <summary>
        /// 1-based exclusive end column for the diagnostic span.
        /// Defaults to one character past <see cref="ColumnNumber"/> when omitted.
        /// </summary>
        public int EndColumnNumber { get; init; }

        /// <summary>Optional stable diagnostic code.</summary>
        public string? Code { get; init; }

        /// <summary>Human-readable description of the issue.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>Original source line, when available.</summary>
        public string? SourceLine { get; init; }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Severity} (line {LineNumber}, col {ColumnNumber}): {Message}";
    }
}
