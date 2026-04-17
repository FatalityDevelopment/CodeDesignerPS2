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

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success         = true,
                    Command         = "NETRECVCAPTURE",
                    CapturedPackets = infos,
                    Message         = $"Drained {infos.Count} captured packet(s)",
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
    }
}
