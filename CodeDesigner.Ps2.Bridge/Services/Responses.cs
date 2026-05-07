using System.Text.Json;
using CodeDesigner.Ps2.Bridge;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Static factory for the JSON response strings every handler returns.
    /// Pulled out of <see cref="SocketServer"/> so services don't need a
    /// reference to the server just to build a reply.
    /// </summary>
    public static class Responses
    {
        public static string Success(string message)
            => JsonSerializer.Serialize(new CommandResponse { Success = true, Message = message });

        public static string Error(string error)
            => JsonSerializer.Serialize(new CommandResponse { Success = false, Error = error });

        /// <summary>Serialize an arbitrary <see cref="CommandResponse"/>.</summary>
        public static string Json(CommandResponse response)
            => JsonSerializer.Serialize(response);
    }
}
