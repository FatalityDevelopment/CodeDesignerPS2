using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Exposes the bridge-held GS framebuffer to JSON-RPC consumers (the MCP
    /// server / AI). The high-rate path for the live UI runs over a separate
    /// binary socket on <see cref="GSStreamForwarder"/>; this service is the
    /// on-demand pull path for low-frequency vision queries.
    ///
    /// <para>
    /// <b>GETGSFRAME</b> — returns the most recent frame the bridge has
    /// received from PCSX2's GSStreamServer, base64-encoded. Latency is
    /// minimal — the frame is already in memory, no PCSX2 round-trip — but
    /// the caller is expected to throttle (e.g. 1–2 fps for AI vision).
    /// </para>
    /// <para>
    /// <b>GSSTREAMSTATUS</b> — returns runtime stats (PCSX2-side via the
    /// PINE control plane, plus bridge-side subscriber and drop counts).
    /// </para>
    /// </summary>
    internal sealed class GsStreamService : ICommandService
    {
        private readonly IBridgeContext   _ctx;
        private readonly GSStreamForwarder _forwarder;

        public GsStreamService(IBridgeContext ctx, GSStreamForwarder forwarder)
        {
            _ctx = ctx;
            _forwarder = forwarder;
        }

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGetGsFrame(),    "GETGSFRAME");
            reg.Add(_ => HandleGsStreamStatus(), "GSSTREAMSTATUS");
        }

        // ── GETGSFRAME ───────────────────────────────────────────────────────

        private string HandleGetGsFrame()
        {
            var frame = _forwarder.GetLatestFrame();
            if (frame == null)
            {
                return Responses.Error(_forwarder.IsConnectedToPcsx2
                    ? "GETGSFRAME: connected to PCSX2 GSStreamServer but no frame received yet"
                    : "GETGSFRAME: bridge is not connected to PCSX2 GSStreamServer (is PINE attached and the PCSX2 build current?)");
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETGSFRAME",
                GsFrame = new GsFrameInfo
                {
                    FrameIndex  = frame.FrameIndex,
                    Width       = frame.Width,
                    Height      = frame.Height,
                    Codec       = frame.Codec,
                    DataBase64  = Convert.ToBase64String(frame.Payload),
                    ReceivedUtc = frame.ReceivedUtc.ToString("o"),
                },
            });
        }

        // ── GSSTREAMSTATUS ───────────────────────────────────────────────────

        private string HandleGsStreamStatus()
        {
            // Bridge-side stats are always available.
            string bridgeMsg = $"forwarder port={_forwarder.ListenerPort}, " +
                               $"connected_to_pcsx2={_forwarder.IsConnectedToPcsx2}, " +
                               $"subscribers={_forwarder.SubscriberCount}, " +
                               $"frames_in={_forwarder.FramesReceived}, " +
                               $"frames_out={_forwarder.FramesForwarded}, " +
                               $"subscriber_drops={_forwarder.SubscriberDrops}";

            // PCSX2-side stats are best-effort — a missing PineClient or an
            // older PCSX2 build (pre-GSStreamServer) just omits them.
            string pcsx2Msg = "pcsx2 stats unavailable (no PINE client attached)";
            var pine = _ctx.PineClient;
            if (pine != null)
            {
                try
                {
                    var s = pine.GSStreamGetStatus();
                    pcsx2Msg = $"pcsx2 active={s.Active}, port={s.Port}, " +
                               $"subscribers={s.Subscribers}, delivered={s.FramesDelivered}, dropped={s.FramesDropped}";
                }
                catch (Exception ex)
                {
                    pcsx2Msg = $"pcsx2 stats unavailable ({ex.Message})";
                }
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GSSTREAMSTATUS",
                Message = $"{bridgeMsg}; {pcsx2Msg}",
            });
        }
    }
}
