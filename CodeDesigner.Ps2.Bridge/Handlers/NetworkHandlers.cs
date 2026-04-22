using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── NETGETSTATUS ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETGETSTATUS — queries the DEV9 network adapter status via
        /// the PINE MsgNetGetStatus (0x38) opcode.
        /// </summary>
        private string HandleNetGetStatus()
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETGETSTATUS requires PINE mode (connect to PCSX2 first)");

            try
            {
                var s = PineClient!.NetGetStatus();
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
                return CreateErrorResponse($"PINE NetGetStatus failed: {ex.Message}");
            }
        }

        // ── NETCAPTURE ───────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETCAPTURE — enables or disables the DEV9 packet capture
        /// via the PINE MsgNetCapture (0x39) opcode.
        /// </summary>
        private string HandleNetCapture(bool? enable, byte? direction, ushort? maxQueue)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETCAPTURE requires PINE mode (connect to PCSX2 first)");
            if (enable == null)
                return CreateErrorResponse("NetCaptureEnable is required for NETCAPTURE");

            try
            {
                PineClient!.NetCapture(enable.Value, direction ?? 0, maxQueue ?? 0);
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
                return CreateErrorResponse($"PINE NetCapture failed: {ex.Message}");
            }
        }

        // ── NETRECVCAPTURE ───────────────────────────────────────────────────

        /// <summary>
        /// Handle NETRECVCAPTURE — drains captured packets from the ring buffer
        /// via the PINE MsgNetRecvCapture (0x3A) opcode.
        /// </summary>
        private string HandleNetRecvCapture(ushort? maxPackets)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETRECVCAPTURE requires PINE mode (connect to PCSX2 first)");

            try
            {
                var packets = PineClient!.NetRecvCapture(maxPackets ?? 50);
                var infos = packets.Select(p => new CapturedPacketInfo
                {
                    Direction     = p.Direction,
                    DirectionName = p.Direction == 0 ? "TX" : "RX",
                    TimestampUs   = p.TimestampUs,
                    Length        = p.Data.Length,
                    Data          = Convert.ToBase64String(p.Data),
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
                return CreateErrorResponse($"PINE NetRecvCapture failed: {ex.Message}");
            }
        }

        // ── NETINJECT ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETINJECT — injects a raw Ethernet frame into the PS2 RX path
        /// via the PINE MsgNetInject (0x3B) opcode.
        /// </summary>
        private string HandleNetInject(string? frameBase64)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETINJECT requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(frameBase64))
                return CreateErrorResponse("NetFrameData (base64) is required for NETINJECT");

            try
            {
                byte[] frame = Convert.FromBase64String(frameBase64);
                bool ok = PineClient!.NetInject(frame);
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
                return CreateErrorResponse("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE NetInject failed: {ex.Message}");
            }
        }

        // ── NETINJECTTX ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETINJECTTX — injects a raw Ethernet frame out through the TX
        /// adapter via the PINE MsgNetInjectTx (0x3C) opcode.
        /// </summary>
        private string HandleNetInjectTx(string? frameBase64)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETINJECTTX requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(frameBase64))
                return CreateErrorResponse("NetFrameData (base64) is required for NETINJECTTX");

            try
            {
                byte[] frame = Convert.FromBase64String(frameBase64);
                bool ok = PineClient!.NetInjectTx(frame);
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
                return CreateErrorResponse("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE NetInjectTx failed: {ex.Message}");
            }
        }

        // ── NETDISASSEMBLE ───────────────────────────────────────────────────

        /// <summary>
        /// Handle NETDISASSEMBLE — dissects a raw Ethernet frame and returns a
        /// human-readable text summary via the PINE MsgNetDisassemble (0x3D) opcode.
        /// </summary>
        private string HandleNetDisassemble(string? frameBase64)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETDISASSEMBLE requires PINE mode (connect to PCSX2 first)");
            if (string.IsNullOrEmpty(frameBase64))
                return CreateErrorResponse("NetFrameData (base64) is required for NETDISASSEMBLE");

            try
            {
                byte[] frame = Convert.FromBase64String(frameBase64);
                string text = PineClient!.NetDisassemble(frame);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success        = true,
                    Command        = "NETDISASSEMBLE",
                    DissectionText = text,
                    Message        = text,
                });
            }
            catch (FormatException)
            {
                return CreateErrorResponse("NetFrameData is not valid base64");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE NetDisassemble failed: {ex.Message}");
            }
        }

        // ── NETSETFILTER ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETSETFILTER — sets the capture filter via the PINE
        /// MsgNetSetFilter (0x3E) opcode.
        /// </summary>
        private string HandleNetSetFilter(byte? filterType, byte? protocol, ushort? port, string? ipStr)
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETSETFILTER requires PINE mode (connect to PCSX2 first)");
            if (filterType == null)
                return CreateErrorResponse("NetFilterType is required for NETSETFILTER");

            try
            {
                byte[]? ipBytes = null;
                if (filterType == 3 && !string.IsNullOrEmpty(ipStr))
                {
                    if (!System.Net.IPAddress.TryParse(ipStr, out var parsed))
                        return CreateErrorResponse($"Invalid IP address: {ipStr}");
                    ipBytes = parsed.GetAddressBytes();
                }

                PineClient!.NetSetFilter(filterType.Value, protocol ?? 0, port ?? 0, ipBytes);

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
                return CreateErrorResponse($"PINE NetSetFilter failed: {ex.Message}");
            }
        }

        // ── NETGETSTATS ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle NETGETSTATS — returns TX/RX counters and capture queue statistics
        /// via the PINE MsgNetGetStats (0x3F) opcode.
        /// </summary>
        private string HandleNetGetStats()
        {
            if (!IsPineMode)
                return CreateErrorResponse("NETGETSTATS requires PINE mode (connect to PCSX2 first)");

            try
            {
                var stats = PineClient!.NetGetStats();
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
                return CreateErrorResponse($"PINE NetGetStats failed: {ex.Message}");
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

        // ── Ethernet / SCERT protocol parser (used by NETRECVCAPTURE) ────────

        private static readonly Dictionary<int, string> s_mediusPorts = new()
        {
            [10071] = "MUIS", [10078] = "MAS", [10075] = "MLS",
            [10070] = "DME",  [10069] = "NAT", [443]   = "DNAS",
        };

        private static readonly Dictionary<int, string> s_rtMsgNames = new()
        {
            [0x00] = "RT_MSG_CLIENT_CONNECT_TCP",
            [0x01] = "RT_MSG_CLIENT_DISCONNECT",
            [0x02] = "RT_MSG_CLIENT_APP_BROADCAST",
            [0x03] = "RT_MSG_CLIENT_APP_SINGLE",
            [0x04] = "RT_MSG_CLIENT_APP_LIST",
            [0x05] = "RT_MSG_CLIENT_ECHO",
            [0x06] = "RT_MSG_SERVER_CONNECT_REJECT",
            [0x07] = "RT_MSG_SERVER_CONNECT_ACCEPT_TCP",
            [0x08] = "RT_MSG_SERVER_CONNECT_NOTIFY",
            [0x09] = "RT_MSG_SERVER_DISCONNECT_NOTIFY",
            [0x0A] = "RT_MSG_SERVER_APP",
            [0x0B] = "RT_MSG_CLIENT_APP_TOSERVER",
            [0x0C] = "RT_MSG_UDP_APP",
            [0x0D] = "RT_MSG_CLIENT_SET_RECV_FLAG",
            [0x0E] = "RT_MSG_CLIENT_SET_AGG_TIME",
            [0x0F] = "RT_MSG_CLIENT_FLUSH_ALL",
            [0x10] = "RT_MSG_CLIENT_FLUSH_SINGLE",
            [0x11] = "RT_MSG_SERVER_FORCED_DISCONNECT",
            [0x12] = "RT_MSG_CLIENT_CRYPTKEY_PUBLIC",
            [0x13] = "RT_MSG_SERVER_CRYPTKEY_PEER",
            [0x14] = "RT_MSG_SERVER_CRYPTKEY_GAME",
            [0x15] = "RT_MSG_CLIENT_CONNECT_TCP_AUX_UDP",
            [0x16] = "RT_MSG_CLIENT_CONNECT_AUX_UDP",
            [0x17] = "RT_MSG_CLIENT_CONNECT_READY_AUX_UDP",
            [0x18] = "RT_MSG_SERVER_INFO_AUX_UDP",
            [0x19] = "RT_MSG_SERVER_CONNECT_ACCEPT_AUX_UDP",
            [0x1A] = "RT_MSG_SERVER_CONNECT_COMPLETE",
            [0x1B] = "RT_MSG_CLIENT_CRYPTKEY_PEER",
            [0x1C] = "RT_MSG_SERVER_SYSTEM_MESSAGE",
            [0x1D] = "RT_MSG_SERVER_CHEAT_QUERY",
            [0x1E] = "RT_MSG_SERVER_MEMORY_POKE",
            [0x1F] = "RT_MSG_SERVER_ECHO",
            [0x20] = "RT_MSG_CLIENT_DISCONNECT_WITH_REASON",
            [0x21] = "RT_MSG_CLIENT_CONNECT_READY_TCP",
            [0x22] = "RT_MSG_SERVER_CONNECT_REQUIRE",
            [0x23] = "RT_MSG_CLIENT_CONNECT_READY_REQUIRE",
            [0x24] = "RT_MSG_CLIENT_HELLO",
            [0x25] = "RT_MSG_SERVER_HELLO",
            [0x26] = "RT_MSG_SERVER_STARTUP_INFO_NOTIFY",
            [0x27] = "RT_MSG_CLIENT_PEER_QUERY",
            [0x28] = "RT_MSG_SERVER_PEER_QUERY_NOTIFY",
            [0x29] = "RT_MSG_CLIENT_PEER_QUERY_LIST",
            [0x2A] = "RT_MSG_SERVER_PEER_QUERY_LIST_NOTIFY",
            [0x2B] = "RT_MSG_CLIENT_WALLCLOCK_QUERY",
            [0x2C] = "RT_MSG_SERVER_WALLCLOCK_QUERY_NOTIFY",
            [0x2D] = "RT_MSG_CLIENT_TIMEBASE_QUERY",
            [0x2E] = "RT_MSG_SERVER_TIMEBASE_QUERY_NOTIFY",
            [0x2F] = "RT_MSG_CLIENT_TOKEN_MESSAGE",
            [0x30] = "RT_MSG_SERVER_TOKEN_MESSAGE",
            [0x31] = "RT_MSG_CLIENT_SYSTEM_MESSAGE",
            [0x32] = "RT_MSG_CLIENT_APP_BROADCAST_QOS",
            [0x33] = "RT_MSG_CLIENT_APP_SINGLE_QOS",
            [0x34] = "RT_MSG_CLIENT_APP_LIST_QOS",
            [0x35] = "RT_MSG_CLIENT_MAX_MSGLEN",
            [0x36] = "RT_MSG_SERVER_MAX_MSGLEN",
            [0x3B] = "RT_MSG_CLIENT_MULTI_APP_TOSERVER",
            [0x3C] = "RT_MSG_SERVER_MULTI_APP_TOCLIENT",
        };

        private static ushort NetU16BE(byte[] b, int o)
            => b.Length > o + 1 ? (ushort)((b[o] << 8) | b[o + 1]) : (ushort)0;

        private static ushort NetU16LE(byte[] b, int o)
            => b.Length > o + 1 ? (ushort)(b[o] | (b[o + 1] << 8)) : (ushort)0;

        private static string NetFmtIP(byte[] b, int o)
            => b.Length >= o + 4 ? $"{b[o]}.{b[o+1]}.{b[o+2]}.{b[o+3]}" : "0.0.0.0";

        private static string NetFmtMac(byte[] b, int o)
            => b.Length >= o + 6
                ? $"{b[o]:X2}:{b[o+1]:X2}:{b[o+2]:X2}:{b[o+3]:X2}:{b[o+4]:X2}:{b[o+5]:X2}"
                : "00:00:00:00:00:00";

        private static string NetServerFromPorts(int sp, int dp)
            => s_mediusPorts.TryGetValue(dp, out var n) ? n
             : s_mediusPorts.TryGetValue(sp, out n)     ? n
             : string.Empty;

        /// <summary>
        /// Parse SCERT messages from a TCP payload slice of a raw Ethernet frame.
        /// Uses IP-derived <paramref name="payLen"/> to exclude Ethernet padding.
        /// Direction-validates each message: TX (0) cannot carry RT_MSG_SERVER_*;
        /// RX (1) cannot carry RT_MSG_CLIENT_*.
        /// </summary>
        private static List<string> NetParseScert(byte[] buf, int start, int payLen, int direction)
        {
            var names = new List<string>();
            int off = start;
            int end = Math.Min(start + payLen, buf.Length);
            while (off < end)
            {
                if (off + 3 > end) break;
                int rawByte   = buf[off];
                bool enc      = (rawByte & 0x80) != 0;
                int msgType   = rawByte & 0x7F;
                if (!s_rtMsgNames.TryGetValue(msgType, out var typeName)) break;
                int payloadLen = NetU16LE(buf, off + 1);
                int hdr        = enc ? 7 : 3;
                int total      = hdr + payloadLen;
                if (off + total > end) break;
                // Direction validation
                if (direction == 0 && typeName.StartsWith("RT_MSG_SERVER_")) { off += total; continue; }
                if (direction == 1 && typeName.StartsWith("RT_MSG_CLIENT_")) { off += total; continue; }
                names.Add(typeName);
                off += total;
            }
            return names;
        }

        private static ParsedPacketRow NetBuildRow(int idx, ulong baseTs, byte dir, ulong tsUs, byte[] raw)
        {
            string dirLabel = dir == 0 ? "TX" : "RX";
            double relSec   = baseTs == 0 ? 0.0 : (tsUs - baseTs) / 1_000_000.0;
            string timeStr  = relSec.ToString("F6");

            if (raw.Length < 14)
                return new ParsedPacketRow { No = idx + 1, Time = timeStr, Dir = dirLabel, Proto = "???", Len = raw.Length, Info = "Too short" };

            int etherType = NetU16BE(raw, 12);

            // ARP
            if (etherType == 0x0806 && raw.Length >= 42)
            {
                int op    = NetU16BE(raw, 20);
                string si = NetFmtIP(raw, 28);
                string ti = NetFmtIP(raw, 38);
                string info = op == 1
                    ? $"Who has {ti}? Tell {si}"
                    : $"{si} is at {NetFmtMac(raw, 22)}";
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = NetFmtMac(raw, 6), Dst = NetFmtMac(raw, 0),
                    Proto = "ARP", Len = raw.Length, Info = info };
            }

            if (etherType != 0x0800 || raw.Length < 34)
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = NetFmtMac(raw, 6), Dst = NetFmtMac(raw, 0),
                    Proto = $"ETH(0x{etherType:X4})", Len = raw.Length,
                    Info = $"EtherType 0x{etherType:X4}" };

            // IPv4
            int ihl        = (raw[14] & 0xF) * 4;
            int ipTotalLen = NetU16BE(raw, 16);
            int ipProto    = raw[23];
            string srcIP   = NetFmtIP(raw, 26);
            string dstIP   = NetFmtIP(raw, 30);
            int payOff     = 14 + ihl;

            // TCP
            if (ipProto == 6 && raw.Length >= payOff + 20)
            {
                int sp      = NetU16BE(raw, payOff);
                int dp      = NetU16BE(raw, payOff + 2);
                int tcpHdr  = ((raw[payOff + 12] >> 4) & 0xF) * 4;
                int flags   = raw[payOff + 13];
                var fl      = new List<string>(6);
                if ((flags & 0x02) != 0) fl.Add("SYN");
                if ((flags & 0x10) != 0) fl.Add("ACK");
                if ((flags & 0x01) != 0) fl.Add("FIN");
                if ((flags & 0x04) != 0) fl.Add("RST");
                if ((flags & 0x08) != 0) fl.Add("PSH");

                string server   = NetServerFromPorts(sp, dp);
                int tcpPayOff   = payOff + tcpHdr;
                // Use IP total length to exclude Ethernet padding
                int tcpPayLen   = ipTotalLen - ihl - tcpHdr;

                if (server.Length > 0 && tcpPayLen > 0)
                {
                    var scert = NetParseScert(raw, tcpPayOff, tcpPayLen, dir);
                    if (scert.Count > 0)
                    {
                        int port = s_mediusPorts.ContainsKey(dp) ? dp : sp;
                        string info = $"[{server}:{port}] {scert[0]}";
                        if (scert.Count > 1) info += $" (+{scert.Count - 1} more)";
                        return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                            Server = server, Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                            Proto = "SCERT", Len = raw.Length, Info = info };
                    }
                }

                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Server = server, Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                    Proto = "TCP", Len = raw.Length,
                    Info = $"{srcIP}:{sp} \u2192 {dstIP}:{dp}  TCP [{string.Join(",", fl)}]" };
            }

            // UDP
            if (ipProto == 17 && raw.Length >= payOff + 8)
            {
                int sp = NetU16BE(raw, payOff);
                int dp = NetU16BE(raw, payOff + 2);
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = $"{srcIP}:{sp}", Dst = $"{dstIP}:{dp}",
                    Proto = "UDP", Len = raw.Length,
                    Info = $"{srcIP}:{sp} \u2192 {dstIP}:{dp}  UDP" };
            }

            // ICMP
            if (ipProto == 1 && raw.Length >= payOff + 2)
            {
                return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                    Src = srcIP, Dst = dstIP,
                    Proto = "ICMP", Len = raw.Length,
                    Info = $"{srcIP} \u2192 {dstIP}  ICMP" };
            }

            return new ParsedPacketRow { No = idx+1, Time = timeStr, Dir = dirLabel,
                Src = srcIP, Dst = dstIP,
                Proto = $"IP({ipProto})", Len = raw.Length,
                Info = $"{srcIP} \u2192 {dstIP}" };
        }
    }
}
