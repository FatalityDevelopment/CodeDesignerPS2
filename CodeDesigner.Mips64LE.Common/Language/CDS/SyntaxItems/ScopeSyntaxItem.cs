namespace CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems
{
    /// <summary>
    /// Represents a <c>scope Name { … }</c> block that groups instructions and labels under
    /// a named scope for higher-level language features.
    /// <para>
    /// Labels defined inside the scope get the scope path prepended to their names, enabling
    /// reuse of the same local label name across different scopes and explicit cross-scope
    /// references via <c>ScopeName.labelName</c>.
    /// </para>
    /// <para>Scopes may be nested arbitrarily deep.</para>
    /// </summary>
    public class ScopeSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The unqualified name declared after the <c>scope</c> keyword.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The dot-separated full path of this scope, e.g., <c>outer.inner</c>.
        /// Equals <see cref="Name"/> for top-level scopes.
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>The syntax items contained directly inside this scope block.</summary>
        public List<CdsSyntaxItem> Body { get; set; } = new();
    }
}
