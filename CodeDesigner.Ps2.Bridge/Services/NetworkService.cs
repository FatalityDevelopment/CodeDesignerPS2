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

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleNetGetStatus(),                                                          "NETGETSTATUS");
            reg.Add(c => HandleNetCapture(c.NetCaptureEnable, c.NetCaptureDirection, c.NetCaptureMaxQueue), "NETCAPTURE");
            reg.Add(c => HandleNetRecvCapture(c.NetMaxPackets),                                         "NETRECVCAPTURE");
            reg.Add(c => HandleNetInject(c.NetFrameData),                                               "NETINJECT");
            reg.Add(c => HandleNetInjectTx(c.NetFrameData),                                             "NETINJECTTX");
            reg.Add(c => HandleNetDisassemble(c.NetFrameData),                                          "NETDISASSEMBLE");
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
                var portMap = s_portMap;
                var infos = packets.Select(p => new CapturedPacketInfo
                {
                    Direction     = p.Direction,
                    DirectionName = p.Direction == 0 ? "TX" : "RX",
                    TimestampUs   = p.TimestampUs,
                    Length        = p.Data.Length,
                    Data          = Convert.ToBase64String(p.Data),
                    // Dissect every drained frame so the webview's tree + hex
                    // panels render instantly on click without a round-trip.
                    Layers        = PacketDissector.Dissect(p.Data, p.Direction, portMap).ToList(),
                }).ToList();

                ulong baseTs = packets.Length > 0 ? packets[0].TimestampUs : 0;
                var rows = packets.Select((p, i) => NetBuildRow(i, baseTs, p.Direction, p.TimestampUs, p.Data)).ToList();

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

        // ── NETINJECT ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETINJECT — injects a raw Ethernet frame into the PS2 RX path
        /// via the PINE MsgNetInject (0x3B) opcode.
        /// </summary>
        private string HandleNetInject(string? frameBase64)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETINJECT requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(frameBase64))
                return Responses.Error("NetFrameData (base64) is required for NETINJECT");

            try
            {
                byte[] frame = Convert.FromBase64String(frameBase64);
                bool ok = _ctx.PineClient!.NetInject(frame);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETINJECT",
                    InjectionSuccess = ok,
                    Message          = ok
                        ? $"Frame ({frame.Length} bytes) injected into PS2 RX FIFO"
                        : "Injection failed (adapter not initialised or FIFO full)",
                });
            }
            catch (FormatException)
            {
                return Responses.Error("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetInject failed: {ex.Message}");
            }
        }

        // ── NETINJECTTX ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETINJECTTX — injects a raw Ethernet frame out through the TX
        /// adapter via the PINE MsgNetInjectTx (0x3C) opcode.
        /// </summary>
        private string HandleNetInjectTx(string? frameBase64)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("NETINJECTTX requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(frameBase64))
                return Responses.Error("NetFrameData (base64) is required for NETINJECTTX");

            try
            {
                byte[] frame = Convert.FromBase64String(frameBase64);
                bool ok = _ctx.PineClient!.NetInjectTx(frame);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "NETINJECTTX",
                    InjectionSuccess = ok,
                    Message          = ok
                        ? $"Frame ({frame.Length} bytes) sent through TX adapter"
                        : "Injection failed (adapter not initialised)",
                });
            }
            catch (FormatException)
            {
                return Responses.Error("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE NetInjectTx failed: {ex.Message}");
            }
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
                var    layers = PacketDissector.Dissect(frame, direction: -1, ports: s_portMap);
                string text   = LayersToText(layers);
                // Also build a ParsedPacketRow so the pcap-load path can hydrate
                // the table columns from this single response — same shape as
                // NETRECVCAPTURE rows but with index 0 / no relative timestamp.
                var row = NetBuildRow(0, 0, dir: 0, tsUs: 0, frame);

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


        // ── ParsedPacketRow builder (used by NETRECVCAPTURE) ─────────────────

        /// <summary>
        /// Build a summary row for one captured packet.
        /// Reuses the canonical <see cref="PacketDissector"/> for SCERT and
        /// Medius identification so the row's "Server" / "Info" columns match
        /// the structured layer tree the same call would produce. Inline
        /// Ethernet/IPv4/TCP parsing is kept for src/dst extraction because
        /// those fields don't need the full Medius decode and the row builder
        /// runs once per packet on every drain.
        /// </summary>
        private static ParsedPacketRow NetBuildRow(int idx, ulong baseTs, byte dir, ulong tsUs, byte[] raw)
        {
            string dirLabel = dir == 0 ? "TX" : "RX";
            double relSec   = baseTs == 0 ? 0.0 : (tsUs - baseTs) / 1_000_000.0;
            string timeStr  = relSec.ToString("F6");

            if (raw.Length < 14)
                return new ParsedPacketRow { No = idx + 1, Time = timeStr, Dir = dirLabel, Proto = "???", Len = raw.Length, Info = "Too short" };

            int etherType = (raw[12] << 8) | raw[13];

            // ARP
            if (etherType == 0x0806 && raw.Length >= 42)
            {
                int op    = (raw[20] << 8) | raw[21];
                string si = $"{raw[28]}.{raw[29]}.{raw[30]}.{raw[31]}";
                string ti = $"{raw[38]}.{raw[39]}.{raw[40]}.{raw[41]}";
                string info = op == 1
                    ? $"Who has {ti}? Tell {si}"
                    : $"{si} is at {FmtMac6(raw, 22)}";
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = FmtMac6(raw, 6), Dst = FmtMac6(raw, 0),
                    Proto = "ARP", Len = raw.Length, Info = info };
            }

            if (etherType != 0x0800 || raw.Length < 34)
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = FmtMac6(raw, 6), Dst = FmtMac6(raw, 0),
                    Proto = $"ETH(0x{etherType:X4})", Len = raw.Length,
                    Info = $"EtherType 0x{etherType:X4}" };

            // IPv4
            int ihl        = (raw[14] & 0xF) * 4;
            int ipTotalLen = (raw[16] << 8) | raw[17];
            int ipProto    = raw[23];
            string srcIP   = $"{raw[26]}.{raw[27]}.{raw[28]}.{raw[29]}";
            string dstIP   = $"{raw[30]}.{raw[31]}.{raw[32]}.{raw[33]}";
            int payOff     = 14 + ihl;

            var portMap = s_portMap;

            // TCP
            if (ipProto == 6 && raw.Length >= payOff + 20)
            {
                int sp      = (raw[payOff]     << 8) | raw[payOff + 1];
                int dp      = (raw[payOff + 2] << 8) | raw[payOff + 3];
                int tcpHdr  = ((raw[payOff + 12] >> 4) & 0xF) * 4;
                int flags   = raw[payOff + 13];
                var fl      = new List<string>(6);
                if ((flags & 0x02) != 0) fl.Add("SYN");
                if ((flags & 0x10) != 0) fl.Add("ACK");
                if ((flags & 0x01) != 0) fl.Add("FIN");
                if ((flags & 0x04) != 0) fl.Add("RST");
                if ((flags & 0x08) != 0) fl.Add("PSH");

                string server   = portMap.ServerFromPorts(sp, dp);
                int tcpPayOff   = payOff + tcpHdr;
                // Use IP total length to exclude Ethernet padding from TCP payload length.
                int tcpPayLen   = ipTotalLen - ihl - tcpHdr;

                bool isScertPort = portMap.ScertPorts.Contains(sp) || portMap.ScertPorts.Contains(dp);
                if (server.Length > 0 && tcpPayLen > 0 && isScertPort)
                {
                    var tcpPayload = raw.AsSpan(tcpPayOff, tcpPayLen).ToArray();
                    // Direction-filter heuristic: TX (0) shouldn't carry SERVER_*;
                    // RX (1) shouldn't carry CLIENT_*. Skips garbage matches in
                    // payloads that aren't actually SCERT.
                    var scert = PacketDissector.ParseScert(tcpPayload).Where(m =>
                        !((dir == 0 && m.MsgTypeName.StartsWith("RT_MSG_SERVER_", StringComparison.Ordinal))
                       || (dir == 1 && m.MsgTypeName.StartsWith("RT_MSG_CLIENT_", StringComparison.Ordinal)))
                    ).ToList();

                    if (scert.Count > 0)
                    {
                        int port = portMap.PortNames.ContainsKey(dp) ? dp : sp;
                        string info = $"[{server}:{port}] {scert[0].MsgTypeName}";
                        if (scert[0].Medius is { } m) info += $"({m.MsgTypeName})";
                        else if (scert[0].IsEncrypted) info += " (encrypted)";
                        if (scert.Count > 1) info += $" (+{scert.Count - 1} more)";
                        return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                            Server = server, Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                            Proto = "SCERT", Len = raw.Length, Info = info };
                    }
                }

                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Server = server, Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                    Proto = "TCP", Len = raw.Length,
                    Info = $"{srcIP}:{sp} → {dstIP}:{dp}  TCP [{string.Join(",", fl)}]" };
            }

            // UDP
            if (ipProto == 17 && raw.Length >= payOff + 8)
            {
                int sp = (raw[payOff]     << 8) | raw[payOff + 1];
                int dp = (raw[payOff + 2] << 8) | raw[payOff + 3];
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                    Proto = "UDP", Len = raw.Length,
                    Info = $"{srcIP}:{sp} → {dstIP}:{dp}  UDP" };
            }

            // ICMP
            if (ipProto == 1 && raw.Length >= payOff + 2)
            {
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = srcIP, Dst = dstIP,
                    Proto = "ICMP", Len = raw.Length,
                    Info = $"{srcIP} → {dstIP}  ICMP" };
            }

            return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                Src = srcIP, Dst = dstIP,
                Proto = $"IP({ipProto})", Len = raw.Length,
                Info = $"{srcIP} → {dstIP}" };
        }

        private static string FmtMac6(byte[] b, int o)
            => b.Length >= o + 6
                ? $"{b[o]:X2}:{b[o+1]:X2}:{b[o+2]:X2}:{b[o+3]:X2}:{b[o+4]:X2}:{b[o+5]:X2}"
                : "00:00:00:00:00:00";
    }
}
