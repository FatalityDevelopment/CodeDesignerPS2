using CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems;

namespace CodeDesigner.Mips64LE.Common.Language.CDS
{
    /// <summary>
    /// Reads and writes <c>.cds</c> files (Code Designer Script).
    /// <para>
    /// Wraps <see cref="CdsParser"/> and <see cref="CdsCompiler"/> and adds helpers for
    /// common file-based workflows.
    /// </para>
    /// </summary>
    public sealed class CdsFile : IDisposable
    {
        private const string FileExtension = ".cds";

        private readonly string _path;
        private readonly CdsParser _parser;

        /// <summary>Create a file handle for an existing or future <c>.cds</c> file.</summary>
        /// <param name="path">Absolute or relative path (must end with <c>.cds</c>).</param>
        public CdsFile(string path)
        {
            _path = path;
            _parser = new CdsParser();
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Parse the <c>.cds</c> file at <see cref="_path"/> and return the syntax-item tree.
        /// </summary>
        /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
        public List<CdsSyntaxItem> Read()
        {
            if (!File.Exists(_path))
                throw new FileNotFoundException($"CDS file not found: {_path}");

            string source = File.ReadAllText(_path);
            string basePath = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? string.Empty;
            return _parser.Parse(source, basePath);
        }

        /// <summary>
        /// Parse the supplied <paramref name="sourceCode"/> as if it were the contents of this
        /// file and return the syntax-item tree.
        /// </summary>
        public List<CdsSyntaxItem> ParseSource(string sourceCode)
        {
            string basePath = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? string.Empty;
            return _parser.Parse(sourceCode, basePath);
        }

        /// <summary>
        /// Parse and compile the file, returning a flat list of (address, word) pairs suitable
        /// for writing to PS2 RAM or a cheat-code engine.
        /// </summary>
        public List<(uint Address, uint Word)> Compile()
        {
            return CdsCompiler.Compile(Read());
        }

        /// <summary>
        /// Parse and compile in-memory source, returning (address, word) pairs.
        /// </summary>
        public List<(uint Address, uint Word)> CompileSource(string sourceCode)
        {
            return CdsCompiler.Compile(ParseSource(sourceCode));
        }

        /// <summary>
        /// Returns whether the path refers to a valid <c>.cds</c> file that currently exists
        /// on disk.
        /// </summary>
        public bool Exists() =>
            _path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
            && File.Exists(_path);

        /// <summary>
        /// Serialise a list of compiled (address, word) pairs as a cheat-code style text file
        /// where each line is <c>XXXXXXXX YYYYYYYY</c>.
        /// </summary>
        public static string ToCheatCode(IEnumerable<(uint Address, uint Word)> pairs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (addr, word) in pairs)
                sb.AppendLine($"{addr:X8} {word:X8}");
            return sb.ToString();
        }

        public void Dispose() { /* nothing to release */ }
    }
}
