using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class NetworkService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public NetworkService(IBridgeContext ctx) => _ctx = ctx;

        /// <summary>
        /// Port map for SCERT detection and the "Server" column. Loaded once
        /// from the running multiplayer server's <c>config.json</c> at startup
        /// so user-overridden ports (e.g. MasPort 10077) show up as "MAS"
        /// instead of leaving the column blank. Falls back to defaults when
        /// the config is missing.
        /// </summary>
        private static readonly MediusPortMap s_portMap = MediusPortMap.LoadFromMultiplayerConfig();

        /// <summary>
        /// Next sequence number per TCP direction, learned from every drained capture (and from our
        /// own injections) so NetTcpContinue can place an injected segment on a live connection.
        /// </summary>
        private static readonly Dictionary<TcpFlowKey, uint> s_tcpNextSeq = new();

        static NetworkService() => Socom2GameCatalog.Install();

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleNetGetStatus(),                                                          "NETGETSTATUS");
            reg.Add(c => HandleNetCapture(c.NetCaptureEnable, c.NetCaptureDirection, c.NetCaptureMaxQueue), "NETCAPTURE");
            reg.Add(c => HandleNetRecvCapture(c.NetMaxPackets),                                         "NETRECVCAPTURE");
            reg.Add(c => HandleNetInject(c, tx: false),                                                 "NETINJECT");
            reg.Add(c => HandleNetInject(c, tx: true),                                                  "NETINJECTTX");
            reg.Add(c => HandleNetDisassemble(c.NetFrameData),                                          "NETDISASSEMBLE");
            reg.Add(c => HandleNetDissectMany(c.NetFrames),                                             "NETDISSECTMANY");
            reg.Add(c => HandleNetBuildFrame(c),                                                        "NETBUILDFRAME");
            reg.Add(c => HandleNetBuildMessage(c),                                                      "NETBUILDMESSAGE");
            reg.Add(_ => HandleNetCatalog(),                                                            "NETCATALOG");
            reg.Add(c => HandleNetSetFilter(c.NetFilterType, c.NetFilterProtocol, c.NetFilterPort, c.NetFilterIP), "NETSETFILTER");
            reg.Add(_ => HandleNetGetStats(),                                                            "NETGETSTATS");
            reg.Add(c => HandleNetDecryptScert(c.NetFrameData, c.NetWorkingKey),                         "NETDECRYPTSCERT");
            reg.Add(c => HandleMediusDecode(c.MediusPayload, c.MediusClass, c.MediusType),               "MEDIUSDECODE");
        }

        // ── NETGETSTATUS ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETGETSTATUS — queries the DEV9 network adapter status via
        /// the PINE MsgNetGetStatus (0x38) opcode.
        /// </summary>
        private string HandleNetGetStatus()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETGETSTATUS requires PINE mode (connect to PCSX2 first)");

            try
            {
                var s = _ctx.PineClient!.NetGetStatus();
                string adapterTypeName = s.AdapterType switch
                {
                    0 => "Unset",
                    1 => "PCAP_Bridged",
                    2 => "PCAP_Switched",
                    3 => "TAP",
                    4 => "Sockets",
                    _ => $"Unknown({s.AdapterType})",
                };
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "NETGETSTATUS",
                    NetStatus = new NetStatusInfo
                    {
                        Enabled     = s.Enabled,
                        AdapterType = s.AdapterType,
                        PS2Mac      = FormatMac(s.PS2Mac),
                        PS2IP       = FormatIP(s.PS2IP),
                    },
                    Message = $"DEV9 adapter: enabled={s.Enabled}, type={adapterTypeName}, MAC={FormatMac(s.PS2Mac)}, IP={FormatIP(s.PS2IP)}",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetGetStatus failed: {ex.Message}");
            }
        }

        // ── NETCAPTURE ───────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETCAPTURE — enables or disables the DEV9 packet capture
        /// via the PINE MsgNetCapture (0x39) opcode.
        /// </summary>
        private string HandleNetCapture(bool? enable, byte? direction, ushort? maxQueue)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETCAPTURE requires PINE mode (connect to PCSX2 first)");
            if (enable == null)
                return Responses.Error("NetCaptureEnable is required for NETCAPTURE");

            try
            {
                _ctx.PineClient!.NetCapture(enable.Value, direction ?? 0, maxQueue ?? 0);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "NETCAPTURE",
                    Message = enable.Value
                        ? $"Packet capture enabled (direction={direction ?? 0}, maxQueue={maxQueue ?? 0})"
                        : "Packet capture disabled",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetCapture failed: {ex.Message}");
            }
        }

        // ── NETRECVCAPTURE ───────────────────────────────────────────────────

        /// <summary>
        /// Handle NETRECVCAPTURE — drains captured packets from the ring buffer
        /// via the PINE MsgNetRecvCapture (0x3A) opcode.
        /// </summary>
        private string HandleNetRecvCapture(ushort? maxPackets)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETRECVCAPTURE requires PINE mode (connect to PCSX2 first)");

            try
            {
                var packets = _ctx.PineClient!.NetRecvCapture(maxPackets ?? 50);
                foreach (var p in packets) TrackTcp(p.Data);
                ulong baseTs = packets.Length > 0 ? packets[0].TimestampUs : 0;
                var (infos, rows) = DissectAll(packets.Select(p => (p.Data, p.Direction, p.TimestampUs)), baseTs);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETRECVCAPTURE",
                    CapturedPackets  = infos,
                    ParsedPacketRows = rows,
                    Message          = $"Drained {infos.Count} captured packet(s)",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetRecvCapture failed: {ex.Message}");
            }
        }

        // ── NETINJECT / NETINJECTTX ──────────────────────────────────────────

        /// <summary>
        /// Handle NETINJECT (PINE 0x3B: the frame arrives on the PS2 RX path, as if the network
        /// delivered it) and NETINJECTTX (PINE 0x3C: the frame leaves through the DEV9 adapter, as
        /// if the PS2 sent it). NetPayloadData / NetFixFrame / NetTcpContinue edit the frame first
        /// (see <see cref="PrepareFrame"/>); without them the bytes go out verbatim.
        /// </summary>
        private string HandleNetInject(CommandMessage c, bool tx)
        {
            string cmd = tx ? "NETINJECTTX" : "NETINJECT";
            if (_ctx.PineClient == null)
                return Responses.Error($"{cmd} requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(c.NetFrameData))
                return Responses.Error($"NetFrameData (base64) is required for {cmd}");

            try
            {
                byte[] frame = PrepareFrame(c, out string note);
                bool ok = tx ? _ctx.PineClient!.NetInjectTx(frame) : _ctx.PineClient!.NetInject(frame);
                if (ok) TrackTcp(frame);
                var summary = PacketDissector.Summarize(frame, tx ? 0 : 1, s_portMap);
                string what = summary.Info.Length > 0 ? $": {summary.Protocol} {summary.Info}" : "";
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = cmd,
                    InjectionSuccess = ok,
                    NetFrame         = Convert.ToBase64String(frame),
                    Message          = (ok
                        ? (tx ? $"Sent {frame.Length}-byte frame through the TX adapter" : $"Injected {frame.Length}-byte frame into the PS2 RX FIFO")
                        : (tx ? "Injection failed (adapter not initialised)" : "Injection failed (adapter not initialised or FIFO full)"))
                        + what + note,
                });
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return Responses.Error($"{cmd}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE {(tx ? "NetInjectTx" : "NetInject")} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply the frame edits shared by NETINJECT / NETINJECTTX / NETBUILDFRAME: swap in
        /// NetPayloadData (lengths and checksums are always fixed then), else fix lengths and
        /// checksums when NetFixFrame is set; NetTcpContinue moves a TCP segment onto the next
        /// seq/ack of its live flow.
        /// </summary>
        private static byte[] PrepareFrame(CommandMessage c, out string note)
        {
            note = "";
            byte[] frame = Convert.FromBase64String(c.NetFrameData!);
            if (!string.IsNullOrEmpty(c.NetPayloadData))
                frame = FrameBuilder.ReplacePayload(frame, Convert.FromBase64String(c.NetPayloadData));
            else if (c.NetFixFrame == true)
                FrameBuilder.FixLengthsAndChecksums(frame);

            if (c.NetTcpContinue == true && FrameBuilder.TryReadTcp(frame, out var seg))
            {
                lock (s_tcpNextSeq)
                {
                    if (s_tcpNextSeq.TryGetValue(seg.Flow, out uint seq))
                    {
                        uint? ack = s_tcpNextSeq.TryGetValue(seg.Flow.Reverse, out uint a) ? a : null;
                        FrameBuilder.SetTcpSeqAck(frame, seq, ack);
                        note = $" (TCP seq {seq}{(ack is uint av ? $", ack {av}" : "")} continues the live flow)";
                    }
                    else
                    {
                        note = " (no captured segment of this TCP flow yet, so seq/ack were left as they were)";
                    }
                }
            }
            return frame;
        }

        /// <summary>Remember where each TCP direction's next segment starts.</summary>
        private static void TrackTcp(byte[] frame)
        {
            if (!FrameBuilder.TryReadTcp(frame, out var seg)) return;
            lock (s_tcpNextSeq)
            {
                // Only move forward (modulo 2^32) so retransmits don't rewind the flow; a SYN restarts it.
                if (!s_tcpNextSeq.TryGetValue(seg.Flow, out uint cur) || (int)(seg.NextSeq - cur) > 0 || (seg.Flags & 0x02) != 0)
                    s_tcpNextSeq[seg.Flow] = seg.NextSeq;
            }
        }

        // ── NETBUILDFRAME / NETBUILDMESSAGE / NETCATALOG ─────────────────────

        /// <summary>
        /// Handle NETBUILDFRAME — apply the injection edits to NetFrameData without sending it, and
        /// return the frame plus its dissection so the composer can preview exactly what goes out.
        /// </summary>
        private static string HandleNetBuildFrame(CommandMessage c)
        {
            if (string.IsNullOrEmpty(c.NetFrameData))
                return Responses.Error("NetFrameData (base64) is required for NETBUILDFRAME");
            try
            {
                byte[] frame = PrepareFrame(c, out string note);
                var layers = PacketDissector.Dissect(frame, -1, s_portMap, out var summary);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETBUILDFRAME",
                    NetFrame         = Convert.ToBase64String(frame),
                    DissectionLayers = layers.ToList(),
                    ParsedPacketRow  = NetBuildRow(0, 0, 0, 0, frame, summary),
                    Message          = $"{frame.Length}-byte frame: {summary.Protocol} {summary.Info}{note}",
                });
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return Responses.Error($"NETBUILDFRAME: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle NETBUILDMESSAGE — encode one catalog message from field values and wrap it in a
        /// plaintext SCERT record (NetRtType; default CLIENT_APP_TOSERVER for lobby messages and
        /// CLIENT_APP_BROADCAST for DME/game messages). The result is a TCP/UDP payload for
        /// NETBUILDFRAME / NETINJECT's NetPayloadData.
        /// </summary>
        private static string HandleNetBuildMessage(CommandMessage c)
        {
            if (string.IsNullOrEmpty(c.NetMessageProtocol) || c.NetMessageId == null)
                return Responses.Error("NetMessageProtocol and NetMessageId are required for NETBUILDMESSAGE");
            var entry = Socom2GameCatalog.Entries.FirstOrDefault(e =>
                string.Equals(e.Protocol, c.NetMessageProtocol, StringComparison.OrdinalIgnoreCase) && e.Id == c.NetMessageId);
            if (entry == null)
                return Responses.Error($"No {c.NetMessageProtocol} message 0x{c.NetMessageId:X2} in the catalog");

            try
            {
                byte[] body = MessageCatalog.Encode(entry, c.NetMessageValues ?? new Dictionary<string, string>());
                RT_MSG_TYPE rt = entry.Protocol == MessageCatalog.Scert ? (RT_MSG_TYPE)entry.Id
                    : c.NetRtType is byte t ? (RT_MSG_TYPE)t
                    : entry.Protocol is MessageCatalog.Dme or MessageCatalog.Game ? RT_MSG_TYPE.RT_MSG_CLIENT_APP_BROADCAST
                    : RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER;
                byte[] record = MessageCatalog.WrapScert(rt, body, c.NetAppTarget ?? 0);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success        = true,
                    Command        = "NETBUILDMESSAGE",
                    NetMessageData = Convert.ToBase64String(record),
                    Message        = $"{entry.Protocol} {entry.Name} in {rt}: {record.Length} bytes",
                });
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                return Responses.Error($"NETBUILDMESSAGE {entry.Name}: {ex.Message}");
            }
        }

        /// <summary>Handle NETCATALOG — every message the multiplayer server defines, with field layouts.</summary>
        private static string HandleNetCatalog()
        {
            var entries = Socom2GameCatalog.Entries;
            return JsonSerializer.Serialize(new CommandResponse
            {
                Success    = true,
                Command    = "NETCATALOG",
                NetCatalog = entries.ToList(),
                Message    = string.Join(", ", entries.GroupBy(e => e.Protocol).Select(g => $"{g.Count()} {g.Key}")),
            });
        }

        // ── NETDISSECTMANY ───────────────────────────────────────────────────

        /// <summary>
        /// Handle NETDISSECTMANY — dissect a batch of frames (a loaded pcap) in one call, returning
        /// the same packet + row shapes as NETRECVCAPTURE.
        /// </summary>
        private static string HandleNetDissectMany(List<NetFrameInput>? frames)
        {
            if (frames == null)
                return Responses.Error("NetFrames is required for NETDISSECTMANY");
            try
            {
                var decoded = frames.Select(f => (Convert.FromBase64String(f.Data), f.Direction, f.TimestampUs)).ToList();
                var (infos, rows) = DissectAll(decoded, decoded.Count > 0 ? decoded[0].TimestampUs : 0);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETDISSECTMANY",
                    CapturedPackets  = infos,
                    ParsedPacketRows = rows,
                    Message          = $"Dissected {infos.Count} frame(s)",
                });
            }
            catch (FormatException)
            {
                return Responses.Error("NetFrames contains a frame that is not valid base64");
            }
        }

        /// <summary>Dissect frames once each into packet infos (layer trees) and table rows.</summary>
        private static (List<CapturedPacketInfo> Infos, List<ParsedPacketRow> Rows) DissectAll(
            IEnumerable<(byte[] Data, byte Direction, ulong TimestampUs)> frames, ulong baseTs)
        {
            var infos = new List<CapturedPacketInfo>();
            var rows  = new List<ParsedPacketRow>();
            int i = 0;
            foreach (var (data, dir, ts) in frames)
            {
                // Dissect every frame so the webview's tree + hex panels render instantly on click.
                var layers = PacketDissector.Dissect(data, dir, s_portMap, out var summary);
                infos.Add(new CapturedPacketInfo
                {
                    Direction     = dir,
                    DirectionName = dir == 0 ? "TX" : "RX",
                    TimestampUs   = ts,
                    Length        = data.Length,
                    Data          = Convert.ToBase64String(data),
                    Layers        = layers.ToList(),
                });
                rows.Add(NetBuildRow(i++, baseTs, dir, ts, data, summary));
            }
            return (infos, rows);
        }

        // ── NETDISASSEMBLE ───────────────────────────────────────────────────

        /// <summary>
        /// Handle NETDISASSEMBLE — dissect a raw Ethernet frame against the
        /// canonical <see cref="PacketDissector"/> and return both the
        /// structured layer tree (for UI / AI consumption) and a flattened
        /// text rendering (legacy compatibility).
        /// </summary>
        /// <remarks>
        /// Dissection runs entirely in-process on the bridge — no PINE
        /// round-trip — so this works against frames captured from any source
        /// (live capture, PCAP file, synthetic test packet) regardless of
        /// whether PCSX2 is connected.
        /// </remarks>
        private string HandleNetDisassemble(string? frameBase64)
        {
            if (string.IsNullOrEmpty(frameBase64))
                return Responses.Error("NetFrameData (base64) is required for NETDISASSEMBLE");

            try
            {
                byte[] frame  = Convert.FromBase64String(frameBase64);
                var    layers = PacketDissector.Dissect(frame, -1, s_portMap, out var summary);
                string text   = LayersToText(layers);
                // Also build a ParsedPacketRow so callers can hydrate the table columns
                // from this single response — same shape as NETRECVCAPTURE rows but
                // with index 0 / no relative timestamp.
                var row = NetBuildRow(0, 0, dir: 0, tsUs: 0, frame, summary);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETDISASSEMBLE",
                    DissectionLayers = layers.ToList(),
                    DissectionText   = text,
                    ParsedPacketRow  = row,
                    Message          = text,
                });
            }
            catch (FormatException)
            {
                return Responses.Error("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return Responses.Error($"NetDisassemble failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Flatten a layer tree into a Wireshark-style indented text block.
        /// Used to populate <c>DissectionText</c> for legacy consumers that
        /// still display the text blob (and for the MCP tool's default output).
        /// </summary>
        private static string LayersToText(IList<DissectedLayer> layers)
        {
            var sb = new StringBuilder();
            foreach (var layer in layers)
            {
                sb.AppendLine(layer.Label);
                foreach (var f in layer.Fields)
                    sb.AppendLine($"    {f.Name}: {f.Value}");
            }
            return sb.ToString().TrimEnd();
        }

        // ── NETDECRYPTSCERT ──────────────────────────────────────────────────

        /// <summary>
        /// Handle NETDECRYPTSCERT — decrypt every encrypted SCERT record inside
        /// a captured frame using the supplied 64-byte RC4 working key, then
        /// dissect the resulting plaintext frame and return the structured layer
        /// tree. Lets the AI read encrypted Medius traffic given a captured key.
        /// </summary>
        private string HandleNetDecryptScert(string? frameBase64, string? workingKeyBase64)
        {
            if (string.IsNullOrEmpty(frameBase64))
                return Responses.Error("NetFrameData (base64 frame) is required for NETDECRYPTSCERT");
            if (string.IsNullOrEmpty(workingKeyBase64))
                return Responses.Error("NetWorkingKey (base64 64-byte RC4 key) is required for NETDECRYPTSCERT");

            try
            {
                byte[] frame      = Convert.FromBase64String(frameBase64);
                byte[] workingKey = Convert.FromBase64String(workingKeyBase64);
                if (workingKey.Length != 64)
                    return Responses.Error($"NetWorkingKey must be 64 bytes (got {workingKey.Length})");

                int decrypted = PacketDissector.DecryptScertInPlace(frame, workingKey, s_portMap);
                var layers    = PacketDissector.Dissect(frame, direction: -1, ports: s_portMap);
                string text   = LayersToText(layers);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETDECRYPTSCERT",
                    DissectionLayers = layers.ToList(),
                    DissectionText   = text,
                    Message          = $"Decrypted {decrypted} SCERT record(s)",
                });
            }
            catch (FormatException)
            {
                return Responses.Error("NetFrameData or NetWorkingKey is not valid base64");
            }
            catch (Exception ex)
            {
                return Responses.Error($"NetDecryptScert failed: {ex.Message}");
            }
        }

        // ── MEDIUSDECODE ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle MEDIUSDECODE — decode a raw Medius payload (the bytes AFTER
        /// the class+type prefix) against the registered struct schema for the
        /// given class/type pair. Returns a single synthetic "Medius" layer
        /// containing the decoded fields. Useful when the caller already has
        /// the payload bytes (e.g. extracted from a save or constructed for a
        /// synthetic packet) and wants the field-level decode without round-
        /// tripping through Ethernet framing.
        /// </summary>
        private string HandleMediusDecode(string? payloadBase64, byte? msgClass, byte? msgType)
        {
            if (string.IsNullOrEmpty(payloadBase64))
                return Responses.Error("MediusPayload (base64) is required for MEDIUSDECODE");
            if (msgClass == null)
                return Responses.Error("MediusClass is required for MEDIUSDECODE");
            if (msgType == null)
                return Responses.Error("MediusType is required for MEDIUSDECODE");

            try
            {
                byte[] payload = Convert.FromBase64String(payloadBase64);
                int    key     = (msgType.Value << 8) | msgClass.Value;
                if (!MediusStructSchemas.Medius.TryGetValue(key, out var schema))
                    return Responses.Error($"No Medius schema registered for class=0x{msgClass.Value:X2} type=0x{msgType.Value:X2}");

                var fields = PacketDissector.ParseMediusFields(schema, payload);
                string className = Enum.IsDefined(typeof(NetMessageClass), msgClass.Value)
                    ? ((NetMessageClass)msgClass.Value).ToString().Replace("MessageClass", "", StringComparison.Ordinal)
                    : $"0x{msgClass.Value:X2}";
                var layer = new DissectedLayer
                {
                    Label  = $"Medius [{className}]  0x{msgType.Value:X2}",
                    Color  = "#ff9580",
                    Offset = 0,
                    Length = payload.Length,
                    Fields = fields,
                };

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "MEDIUSDECODE",
                    DissectionLayers = new List<DissectedLayer> { layer },
                    DissectionText   = LayersToText([layer]),
                    Message          = $"Decoded {fields.Count} field(s) for class=0x{msgClass.Value:X2} type=0x{msgType.Value:X2}",
                });
            }
            catch (FormatException)
            {
                return Responses.Error("MediusPayload is not valid base64");
            }
            catch (Exception ex)
            {
                return Responses.Error($"MediusDecode failed: {ex.Message}");
            }
        }

        // ── NETSETFILTER ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETSETFILTER — sets the capture filter via the PINE
        /// MsgNetSetFilter (0x3E) opcode.
        /// </summary>
        private string HandleNetSetFilter(byte? filterType, byte? protocol, ushort? port, string? ipStr)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETSETFILTER requires PINE mode (connect to PCSX2 first)");
            if (filterType == null)
                return Responses.Error("NetFilterType is required for NETSETFILTER");

            try
            {
                byte[]? ipBytes = null;
                if (filterType == 3 && !string.IsNullOrEmpty(ipStr))
                {
                    if (!System.Net.IPAddress.TryParse(ipStr, out var parsed))
                        return Responses.Error($"Invalid IP address: {ipStr}");
                    ipBytes = parsed.GetAddressBytes();
                }

                _ctx.PineClient!.NetSetFilter(filterType.Value, protocol ?? 0, port ?? 0, ipBytes);

                string desc = filterType.Value switch
                {
                    0 => "Filter cleared (all packets pass)",
                    1 => $"Filter set: protocol={protocol ?? 0}",
                    2 => $"Filter set: port={port ?? 0}",
                    3 => $"Filter set: IP={ipStr}",
                    _ => $"Filter set: type={filterType.Value}",
                };

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "NETSETFILTER",
                    Message = desc,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetSetFilter failed: {ex.Message}");
            }
        }

        // ── NETGETSTATS ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETGETSTATS — returns TX/RX counters and capture queue statistics
        /// via the PINE MsgNetGetStats (0x3F) opcode.
        /// </summary>
        private string HandleNetGetStats()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETGETSTATS requires PINE mode (connect to PCSX2 first)");

            try
            {
                var stats = _ctx.PineClient!.NetGetStats();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "NETGETSTATS",
                    NetStats = new NetStatsInfo
                    {
                        TxPackets        = stats.TxPackets,
                        RxPackets        = stats.RxPackets,
                        TxBytes          = stats.TxBytes,
                        RxBytes          = stats.RxBytes,
                        CaptureQueueSize = stats.CaptureQueueSize,
                        DroppedCount     = stats.DroppedCount,
                    },
                    Message = $"TX: {stats.TxPackets} pkts / {stats.TxBytes} bytes, " +
                              $"RX: {stats.RxPackets} pkts / {stats.RxBytes} bytes, " +
                              $"Queue: {stats.CaptureQueueSize}, Dropped: {stats.DroppedCount}",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetGetStats failed: {ex.Message}");
            }
        }

        // ── Network helpers ──────────────────────────────────────────────────

        private static string FormatMac(byte[] mac)
        {
            if (mac == null || mac.Length < 6) return "00:00:00:00:00:00";
            return $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";
        }

        private static string FormatIP(byte[] ip)
        {
            if (ip == null || ip.Length < 4) return "0.0.0.0";
            return $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
        }


        // ── ParsedPacketRow builder ──────────────────────────────────────────

        /// <summary>
        /// Build the table row for one frame from the dissector's <see cref="PacketSummary"/>, so the
        /// Server / Proto / Info columns name exactly the messages the layer tree decodes.
        /// </summary>
        private static ParsedPacketRow NetBuildRow(int idx, ulong baseTs, byte dir, ulong tsUs, byte[] raw, PacketSummary summary)
        {
            double relSec = baseTs == 0 ? 0.0 : (tsUs - baseTs) / 1_000_000.0;
            return new ParsedPacketRow
            {
                No     = idx + 1,
                Time   = relSec.ToString("F6"),
                Dir    = dir == 0 ? "TX" : "RX",
                Server = summary.Server,
                Src    = summary.Source,
                Dst    = summary.Destination,
                Proto  = summary.Protocol,
                Len    = raw.Length,
                Info   = summary.Info,
            };
        }
    }
}
