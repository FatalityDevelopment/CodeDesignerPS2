using System;
using System.Collections.Generic;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// A delegate that implements one Bridge command. Receives the parsed
    /// <see cref="CommandMessage"/>; returns the JSON response string.
    /// </summary>
    internal delegate string CommandHandler(CommandMessage command);

    /// <summary>
    /// Maps command names (case-insensitive) to handler delegates.
    /// <see cref="ICommandService"/> implementations populate the registry
    /// during <see cref="SocketServer"/> startup; the dispatcher then looks
    /// commands up here at runtime.
    /// </summary>
    internal sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandHandler> _handlers
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a handler for the given command name (or any number of
        /// aliases — useful for <c>STARTPCSX2</c> / <c>START_PCSX2</c> /
        /// <c>STARTPCSX2MODE</c> equivalents).
        /// </summary>
        public void Add(CommandHandler handler, params string[] names)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (names == null || names.Length == 0)
                throw new ArgumentException("At least one name required", nameof(names));
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Empty command name", nameof(names));
                if (_handlers.ContainsKey(name))
                    throw new InvalidOperationException($"Command '{name}' already registered");
                _handlers[name] = handler;
            }
        }

        /// <summary>Try to resolve a handler by command name.</summary>
        public bool TryGet(string commandName, out CommandHandler? handler)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                handler = null;
                return false;
            }
            return _handlers.TryGetValue(commandName, out handler!);
        }

        /// <summary>Number of registered command names (including aliases).</summary>
        public int Count => _handlers.Count;
    }
}
