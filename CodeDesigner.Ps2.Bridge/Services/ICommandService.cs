namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// One service per Bridge "domain" (memory, execution, cheats, …).
    /// <para>
    /// Each implementation registers its handlers with a
    /// <see cref="CommandRegistry"/> via <see cref="Register"/>. Replaces the
    /// "everything is a partial class of <see cref="SocketServer"/>" pattern,
    /// where every new handler made the god-class slightly fatter.
    /// </para>
    /// <para>
    /// <b>Architecture</b>:
    /// </para>
    /// <code>
    /// SocketServer (network plumbing)
    ///     │
    ///     ├─ owns ─▶ CommandDispatcher  ─▶  CommandRegistry  ◀──┐
    ///     │                                                      │ Register()
    ///     └─ implements ▶ IBridgeContext  ──────────────────────┐│
    ///                                                           ▼▼
    ///                          ┌────────────────────────────────────┐
    ///                          │  Per-domain ICommandService impls  │
    ///                          │  - MemoryService                   │
    ///                          │  - ExecutionService                │
    ///                          │  - CheatsService …                 │
    ///                          └────────────────────────────────────┘
    /// </code>
    /// <para>
    /// <b>Recipe for migrating a handler file</b>:
    /// </para>
    /// <list type="number">
    ///   <item>Create <c>Bridge/Services/&lt;Domain&gt;Service.cs</c>.
    ///         Make it <c>internal sealed</c> implementing
    ///         <see cref="ICommandService"/>; take an <see cref="IBridgeContext"/>
    ///         in the constructor (and any peer services you call into, e.g.
    ///         a <c>RegisterService</c> reference for handlers that need it).</item>
    ///   <item>Move the <c>Handle*</c> methods over verbatim. Replace:
    ///     <list type="bullet">
    ///       <item><c>Scheduler</c>/<c>PineClient</c>/<c>Client</c>/<c>IsPineMode</c>
    ///             → <c>_ctx.Client</c>, <c>_ctx.PineClient</c>, etc.</item>
    ///       <item><c>CreateSuccessResponse</c>/<c>CreateErrorResponse</c>
    ///             → <see cref="Responses.Success"/>/<see cref="Responses.Error"/>.
    ///             For arbitrary CommandResponse objects, <see cref="Responses.Json"/>.</item>
    ///       <item><c>TranslateClientAddress</c>, <c>NormalizePcToKseg0</c>,
    ///             <c>NormalizeBreakpointForClient</c>, <c>NormalizeBreakpointToPhysical</c>
    ///             → static <see cref="AddressTranslator"/> equivalents.</item>
    ///       <item>Cross-handler calls (e.g. <c>HandleGetRegisters</c> from
    ///             <c>RunUntilPc</c>) → constructor-injected reference to the
    ///             peer service.</item>
    ///     </list></item>
    ///   <item>In <see cref="Register"/>, map each command name (and its aliases)
    ///         to a delegate. <c>reg.Add(handler, "FOO", "FOO_ALIAS")</c>.</item>
    ///   <item>Add the new service to <see cref="SocketServer"/>'s constructor
    ///         array.</item>
    ///   <item>Delete the corresponding <c>case</c> branches from
    ///         <see cref="SocketServer"/>'s <c>ProcessCommand</c> switch.</item>
    ///   <item>Delete the legacy <c>Handlers/&lt;Name&gt;Handlers.cs</c> file
    ///         once nothing outside it references its private helpers.
    ///         (Some files house helper methods other unmigrated handlers
    ///         depend on — keep the file but strip the moved <c>Handle*</c>
    ///         methods, or move the helpers into a static utility class first.)</item>
    /// </list>
    /// <para>
    /// <b>Shared state</b> belongs on <see cref="IBridgeContext"/>; per-service
    /// state (e.g. <c>CheatsService._cdsCheatEngine</c>) is a private field.
    /// </para>
    /// <para>
    /// <b>Testing</b>: services depend on <see cref="IBridgeContext"/> rather
    /// than the concrete <see cref="SocketServer"/>, so unit tests can supply
    /// a fake context with mocked clients and an in-memory DB. This is the
    /// main reason for the indirection: the old super-class made testing
    /// handlers in isolation effectively impossible.
    /// </para>
    /// </summary>
    internal interface ICommandService
    {
        /// <summary>
        /// Register this service's command handlers with the dispatcher.
        /// Called once at <see cref="SocketServer"/> startup.
        /// </summary>
        void Register(CommandRegistry registry);
    }
}
