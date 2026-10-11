using System.Collections.Concurrent;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// Debug-API memory access to a logged-in lobby client: peek = RAW_MEMORY cheat queries (reply matched by
/// sequence id in <see cref="HandleCheatQueryResponseAsync"/>), poke = SERVER_MEMORY_POKE writes. Used by
/// <see cref="DebugApiServer"/> (localhost only) and Mods/server/socom_server.py.
/// </summary>
public partial class MlsServer
{
    /// <summary>Largest RAW_MEMORY read per query (the scanner uses the same cap).</summary>
    public const int MaxPeekChunk = 1024;

    private static readonly ConcurrentDictionary<(string Conn, int Seq), TaskCompletionSource<byte[]>> PendingPeeks = new();

    /// <summary>Connected lobby clients: connection id, account name (empty before login), remote endpoint.</summary>
    public IReadOnlyList<(string ConnectionId, string Account, string Endpoint)> LobbyClients() =>
        Clients.Values
            .Where(c => c.Client.Connected)
            .Select(c => (c.State.ConnectionId, c.State.AccountName ?? "", c.State.RemoteEndpoint))
            .ToList();

    /// <summary>
    /// Find a client by account name (case-insensitive) or connection id; with no selector, the only
    /// logged-in client. Throws <see cref="ArgumentException"/> with a usable message otherwise.
    /// </summary>
    public string ResolveClient(string? selector)
    {
        var all = Clients.Values.Where(c => c.Client.Connected).ToList();
        if (string.IsNullOrWhiteSpace(selector))
        {
            var loggedIn = all.Where(c => c.State.IsLoggedIn).ToList();
            return loggedIn.Count == 1 ? loggedIn[0].State.ConnectionId
                : throw new ArgumentException($"{loggedIn.Count} logged-in lobby clients; pass client=<account or connection id>");
        }
        var hit = all.FirstOrDefault(c => c.State.ConnectionId == selector);
        if (hit.State == null)
            hit = all.FirstOrDefault(c => string.Equals(c.State.AccountName, selector, StringComparison.OrdinalIgnoreCase));
        return hit.State?.ConnectionId ?? throw new ArgumentException($"no lobby client '{selector}'");
    }

    /// <summary>Read <paramref name="length"/> bytes of the client's EE memory (split into 1 KB queries).</summary>
    public async Task<byte[]> PeekAsync(string connectionId, uint address, int length, TimeSpan timeout, CancellationToken ct)
    {
        if (!Clients.TryGetValue(connectionId, out var c)) throw new ArgumentException($"client {connectionId} is gone");
        var result = new byte[length];
        for (int off = 0; off < length; off += MaxPeekChunk)
        {
            int n = Math.Min(MaxPeekChunk, length - off);
            int seq = c.State.CheatSeq++;
            var waiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingPeeks[(connectionId, seq)] = waiter;
            try
            {
                await SendPacketAsync(c.Client, ScertPacket.CreateRawMemoryQuery(seq, address + (uint)off, n), c.State, ct);
                var data = await waiter.Task.WaitAsync(timeout, ct);
                Buffer.BlockCopy(data, 0, result, off, Math.Min(n, data.Length));
            }
            catch (TimeoutException)
            {
                throw new ArgumentException($"no reply for 0x{address + (uint)off:X8}+{n} within {timeout.TotalSeconds:0.#}s");
            }
            finally
            {
                PendingPeeks.TryRemove((connectionId, seq), out _);
            }
        }
        return result;
    }

    /// <summary>
    /// Lobby / game-lobby chat "from" any name: a ChatFwdMessage (Broadcast) sent to every session in the same
    /// Medius world as <paramref name="connectionId"/> (that player included). Lobby chat is Medius, not DME, so
    /// fake players can only talk there through the server. Returns the number of recipients.
    /// </summary>
    public async Task<int> InjectChatAsync(string connectionId, string fromName, string text, int fromAccountId, CancellationToken ct)
    {
        if (!Clients.TryGetValue(connectionId, out var c)) throw new ArgumentException($"client {connectionId} is gone");
        var fwd = new MediusWriter();
        fwd.WritePadding(MediusConstants.MESSAGEID_MAXLEN);
        fwd.WritePadding(3);
        fwd.Write(fromAccountId);
        fwd.WriteString(fromName, NameLen);
        fwd.Write((int)MediusChatMessageType.Broadcast);
        fwd.WriteString(text, ChatLen);
        var body = fwd.ToArray();

        var recipients = Clients.Values.Where(x => x.Client.Connected && x.State.IsSessionEstablished
                                                   && x.State.WorldId == c.State.WorldId).ToList();
        foreach (var (rState, rTcp) in recipients)
            await SendMediusAsync(rTcp, MediusPacket.BuildLobby(MediusLobbyMessageId.ChatFwdMessage, body), rState, ct);
        return recipients.Count;
    }

    /// <summary>Write <paramref name="data"/> into the client's EE memory with SERVER_MEMORY_POKE (512-byte chunks).</summary>
    public Task<bool> PokeAsync(string connectionId, uint address, byte[] data, CancellationToken ct) =>
        InjectIntoClientAsync(connectionId, address, data, hookAddr: null, entryAddr: null, chunkSize: 512, ct);
}
