using System.Collections.Generic;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Routes incoming <see cref="CommandMessage"/>s to whichever
    /// <see cref="ICommandService"/> registered them. SocketServer constructs
    /// one of these at startup, registers every service, and delegates from
    /// its <c>ProcessCommand</c> path.
    /// </summary>
    internal sealed class CommandDispatcher
    {
        private readonly CommandRegistry _registry = new();

        /// <summary>Register every service's handlers in one call.</summary>
        public CommandDispatcher(IEnumerable<ICommandService> services)
        {
            foreach (var svc in services) svc.Register(_registry);
        }

        /// <summary>
        /// Try to dispatch <paramref name="command"/>. Returns the JSON
        /// response when a handler was found, or <c>null</c> if no service
        /// owns this command name (the caller's switch fallback can take it).
        /// </summary>
        public string? TryDispatch(CommandMessage command)
        {
            if (string.IsNullOrEmpty(command?.Command)) return null;
            return _registry.TryGet(command.Command, out var handler) && handler != null
                ? handler(command)
                : null;
        }
    }
}
