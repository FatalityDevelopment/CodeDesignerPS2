using System.Text;
using CodeDesigner.Ps2.Multiplayer.Crypto;

namespace CodeDesigner.Ps2.Multiplayer.Protocol;

/// <summary>
/// Decodes raw Ethernet frames into a Wireshark-style layer tree, with
/// field-level decoding of the SCERT / Medius application protocols.
///
/// This is the canonical implementation — the VS Code packet-viewer webview
/// renders the layers this returns instead of re-implementing the protocol
/// in TypeScript. The MCP <c>net_disassemble</c> tool surfaces the same
/// output so AI agents can reason about captured packets.
///
/// Use <see cref="Dissect(byte[], int, MediusPortMap?)"/> as the entry point.
/// Pass a <see cref="MediusPortMap"/> built from the live Multiplayer config
/// so user-overridden ports are recognised; otherwise stock defaults apply.
/// </summary>
public static class PacketDissector
{
    // RT_MSG_TYPE values that carry a Medius APP payload (class + type bytes).
    private static readonly HashSet<byte> s_appMsgTypes =
    [
        (byte)RT_MSG_TYPE.RT_MSG_SERVER_APP,
        (byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER,
        (byte)RT_MSG_TYPE.RT_MSG_CLIENT_MULTI_APP_TOSERVER,
        (byte)RT_MSG_TYPE.RT_MSG_SERVER_MULTI_APP_TOCLIENT,
    ];

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Dissect a raw Ethernet frame into protocol layers.
    /// </summary>
    /// <param name="frame">Raw Ethernet frame bytes (starting at the destination MAC).</param>
    /// <param name="direction">0 = TX (PS2→Host), 1 = RX (Host→PS2), -1 = unknown.</param>
    /// <param name="ports">Resolved port map. Defaults to <see cref="MediusPortMap.Defaults"/>.</param>
    /// <returns>Ordered list of layers from outermost (Ethernet) to innermost (Medius payload).</returns>
    public static IList<DissectedLayer> Dissect(byte[] frame, int direction = -1, MediusPortMap? ports = null)
    {
        var portMap = ports ?? MediusPortMap.Defaults;
        var layers  = new List<DissectedLayer>();
        if (frame.Length < 14) return layers;

        // -- Ethernet II --
        string dstMac    = FmtMac(frame, 0);
        string srcMac    = FmtMac(frame, 6);
        ushort etherType = ReadU16BE(frame, 12);
        layers.Add(new DissectedLayer
        {
            Label  = "Ethernet II",
            Color  = "#5b9bd5",
            Offset = 0,
            Length = 14,
            Fields =
            [
                new("Destination", dstMac),
                new("Source",      srcMac),
                new("Type",        $"0x{etherType:X4} ({EtherTypeName(etherType)})"),
            ],
        });

        int off = 14;

        if (etherType == 0x0806)
        {
            DissectArp(frame, off, layers);
            return layers;
        }
        if (etherType != 0x0800) return layers; // IPv4 only beyond here

        if (!TryDissectIPv4(frame, off, direction, portMap, layers, out _, out _, out _, out _))
            return layers;

        return layers;
    }

    /// <summary>
    /// Parse SCERT messages from a TCP payload slice.
    /// Exposed separately so callers that have already decrypted a payload
    /// (or extracted it from a non-PCAP source) can run the SCERT parser
    /// directly without going through Ethernet/IP framing.
    /// </summary>
    /// <param name="tcpPayload">TCP payload bytes (already offset past the TCP header).</param>
    /// <param name="frameBase">Absolute offset of <paramref name="tcpPayload"/>[0] within the original frame, for frame-relative <see cref="ParsedScertMessage.FrameOffset"/> reporting.</param>
    public static IList<ParsedScertMessage> ParseScert(byte[] tcpPayload, int frameBase = 0)
    {
        var msgs = new List<ParsedScertMessage>();
        int off  = 0;
        while (off < tcpPayload.Length)
        {
            // Need at least 3 bytes: type(1) + length(2)
            if (off + 3 > tcpPayload.Length) break;

            byte rawTypeByte = tcpPayload[off];
            bool isEncrypted = (rawTypeByte & 0x80) != 0;
            byte msgType     = (byte)(rawTypeByte & 0x7F);

            // Sanity: type must be a known SCERT value; bail if it looks like garbage.
            if (!Enum.IsDefined(typeof(RT_MSG_TYPE), msgType)) break;

            ushort payloadLen = ReadU16LE(tcpPayload, off + 1);
            int    hdrSize   = isEncrypted ? 7 : 3;
            int    totalSize = hdrSize + payloadLen;

            // Don't over-read beyond the buffer (fragmented segment — stop here).
            if (off + totalSize > tcpPayload.Length) break;

            uint? hash = isEncrypted ? ReadU32BE(tcpPayload, off + 3) : null;

            ParsedMediusMessage? medius          = null;
            IList<DissectedField>? scertFields   = null;
            bool isAppMsg = s_appMsgTypes.Contains(msgType);

            if (isAppMsg && payloadLen >= 2)
            {
                int  appOff   = off + hdrSize;
                byte msgClass = tcpPayload[appOff];
                byte mType    = tcpPayload[appOff + 1];

                // Plausibility filter: when the record is still encrypted on
                // the wire, the bytes at appOff/appOff+1 are RC4 ciphertext —
                // reading them as (class, type) used to produce spectacularly
                // misleading labels (e.g. GetUniverse_ExtraInfo showing as
                // "AddToBuddyListFwdConfirmationRequest0" because cipher byte
                // 0x45 happens to match that enum value).
                //
                // Ciphertext bytes are uniformly random, so the chance of a
                // false positive (class ∈ {0..5} AND type defined in the
                // matching enum) is ≲ 1%. Records that this dissector callee
                // has explicitly decrypted via DecryptScertInPlace still pass
                // — that helper leaves the encrypted flag set so the +7 byte
                // header offset stays valid, but the now-plaintext class+type
                // ARE valid, so this check accepts them.
                bool plausible = !isEncrypted || LooksLikePlausibleMediusHeader(msgClass, mType);
                if (plausible)
                {
                    int  structKey = (mType << 8) | msgClass;
                    var  mediusPayload = tcpPayload.AsSpan(appOff + 2, payloadLen - 2).ToArray();
                    var  payloadFields = MediusStructSchemas.Medius.TryGetValue(structKey, out var def)
                        ? ParseMediusFields(def, mediusPayload)
                        : new List<DissectedField>();
                    medius = new ParsedMediusMessage
                    {
                        MsgClass      = msgClass,
                        MsgClassName  = MsgClassName(msgClass),
                        MsgType       = mType,
                        MsgTypeName   = MediusTypeName(msgClass, mType),
                        FrameOffset   = frameBase + appOff,
                        PayloadFields = payloadFields,
                    };
                }
            }
            else if (!isAppMsg && payloadLen > 0 && !isEncrypted)
            {
                if (MediusStructSchemas.ScertPayloads.TryGetValue(msgType, out var scertStruct))
                {
                    var scertPayload = tcpPayload.AsSpan(off + hdrSize, payloadLen).ToArray();
                    scertFields = ParseMediusFields(scertStruct, scertPayload);
                }
            }

            msgs.Add(new ParsedScertMessage
            {
                RawTypeByte        = rawTypeByte,
                MsgType            = (RT_MSG_TYPE)msgType,
                MsgTypeName        = ((RT_MSG_TYPE)msgType).ToString(),
                IsEncrypted        = isEncrypted,
                PayloadLen         = payloadLen,
                Hash               = hash,
                Medius             = medius,
                ScertPayloadFields = scertFields,
                FrameOffset        = frameBase + off,
                FrameLength        = totalSize,
            });

            off += totalSize;
        }
        return msgs;
    }

    /// <summary>
    /// Walk SCERT records inside <paramref name="frame"/>'s TCP payload and
    /// decrypt every record whose encrypted flag (0x80) is set, using
    /// <see cref="PS2_RC4"/> with the supplied 64-byte working key. Decryption
    /// is in-place — the buffer holds plaintext payloads after this returns.
    /// </summary>
    /// <param name="frame">Mutable raw Ethernet frame. TCP payload is rewritten in place.</param>
    /// <param name="workingKey">64-byte RC4 working key (the same one used to encrypt).</param>
    /// <param name="ports">Port map for locating the SCERT-carrying TCP segment. Defaults if null.</param>
    /// <returns>Number of records decrypted (zero if the frame carries no encrypted SCERT records).</returns>
    /// <remarks>
    /// Use case: the AI captures a SCERT trace, extracts the per-session RC4
    /// key from an earlier handshake, then calls this to make every later
    /// packet readable. Pairs with <see cref="Dissect"/> for full structured
    /// readout from a single key + frame.
    /// </remarks>
    public static int DecryptScertInPlace(byte[] frame, byte[] workingKey, MediusPortMap? ports = null)
    {
        if (workingKey.Length != 64) throw new ArgumentException("RC4 working key must be 64 bytes.", nameof(workingKey));
        var portMap = ports ?? MediusPortMap.Defaults;

        if (frame.Length < 34) return 0;
        if (((frame[12] << 8) | frame[13]) != 0x0800) return 0;            // IPv4 only
        int ihl = (frame[14] & 0xF) * 4;
        if (frame[23] != 6) return 0;                                       // TCP only
        int ipTotalLen = (frame[16] << 8) | frame[17];
        int tcpHdrOff  = 14 + ihl;
        if (frame.Length < tcpHdrOff + 20) return 0;
        int srcPort  = (frame[tcpHdrOff]     << 8) | frame[tcpHdrOff + 1];
        int dstPort  = (frame[tcpHdrOff + 2] << 8) | frame[tcpHdrOff + 3];
        if (!portMap.ScertPorts.Contains(srcPort) && !portMap.ScertPorts.Contains(dstPort)) return 0;

        int tcpHdr   = ((frame[tcpHdrOff + 12] >> 4) & 0xF) * 4;
        int payOff   = tcpHdrOff + tcpHdr;
        int payLen   = ipTotalLen - ihl - tcpHdr;
        if (payLen <= 0 || payOff + payLen > frame.Length) return 0;

        int decrypted = 0;
        int off = payOff;
        int end = payOff + payLen;
        while (off < end)
        {
            if (off + 3 > end) break;
            byte rawType = frame[off];
            byte msgType = (byte)(rawType & 0x7F);
            if (!Enum.IsDefined(typeof(RT_MSG_TYPE), msgType)) break;
            ushort recPayloadLen = (ushort)(frame[off + 1] | (frame[off + 2] << 8));
            bool encrypted = (rawType & 0x80) != 0;
            int hdr  = encrypted ? 7 : 3;
            int rec  = hdr + recPayloadLen;
            if (off + rec > end) break;

            if (encrypted && recPayloadLen > 0)
            {
                // SCERT encrypted layout: [type|0x80][lenLE:2][hash:4][cipher payload]
                // PS2_RC4 takes the 4-byte hash verbatim as IV. Decrypt the
                // payload in place; leave the encrypted flag + hash bytes
                // unchanged so Dissect's hdrSize=7 calculation still locates
                // the (now-plaintext) payload correctly. The label "Encrypted:
                // Yes" stays in the dissection so callers can see this record
                // *was* encrypted on the wire.
                byte[] hash = new byte[4];
                Array.Copy(frame, off + 3, hash, 0, 4);
                PS2_RC4.Decrypt(workingKey, hash, frame.AsSpan(off + 7, recPayloadLen));
                decrypted++;
            }

            off += rec;
        }
        return decrypted;
    }

    /// <summary>
    /// Parse a Medius payload against an explicit struct definition. Useful when
    /// a caller already knows the class+type (e.g. when assembling synthetic
    /// packets or testing a payload extracted from a save).
    /// </summary>
    public static IList<DissectedField> ParseMediusFields(MediusStructDef def, byte[] payload)
    {
        var fields = new List<DissectedField>();
        int off    = 0;
        foreach (var f in def.Fields)
        {
            if (off >= payload.Length)
            {
                if (!f.Optional) break;
                continue;
            }
            int avail = Math.Min(f.Length, payload.Length - off);

            switch (f.Type)
            {
                case MediusFieldType.UInt:
                {
                    uint val = 0;
                    for (int i = 0; i < avail; i++) val |= (uint)payload[off + i] << (i * 8);
                    string? enumName = ResolveEnumName(f, val);
                    string  valStr   = enumName != null
                        ? $"{val} ({enumName})"
                        : $"{val} (0x{val:X})";
                    fields.Add(new DissectedField(f.Name, valStr));
                    break;
                }
                case MediusFieldType.AsciiZ:
                {
                    int end = off;
                    while (end < off + avail && payload[end] != 0) end++;
                    string str = Encoding.ASCII.GetString(payload, off, end - off);
                    fields.Add(new DissectedField(f.Name, str.Length > 0 ? str : "(empty)"));
                    break;
                }
                case MediusFieldType.Bool:
                {
                    uint val = 0;
                    for (int i = 0; i < avail; i++) val |= (uint)payload[off + i] << (i * 8);
                    fields.Add(new DissectedField(f.Name, val != 0 ? "true" : "false"));
                    break;
                }
                case MediusFieldType.Bytes:
                default:
                {
                    var sb = new StringBuilder(avail * 3);
                    bool allPrintable = true;
                    for (int i = 0; i < avail; i++)
                    {
                        byte b = payload[off + i];
                        if (i > 0) sb.Append(' ');
                        sb.Append(b.ToString("X2"));
                        if (!(b == 0 || (b >= 0x20 && b < 0x7F))) allPrintable = false;
                    }
                    if (allPrintable && avail > 0)
                    {
                        sb.Append("  \"");
                        for (int i = 0; i < avail; i++)
                        {
                            byte b = payload[off + i];
                            if (b != 0) sb.Append((char)b);
                        }
                        sb.Append('"');
                    }
                    fields.Add(new DissectedField(f.Name, sb.ToString()));
                    break;
                }
            }
            off += f.Length;
        }
        return fields;
    }

    // ── ARP / IPv4 / TCP / UDP / ICMP ────────────────────────────────────────

    private static void DissectArp(byte[] raw, int off, List<DissectedLayer> layers)
    {
        if (raw.Length < off + 28) return;
        int    op        = ReadU16BE(raw, off + 6);
        string senderMac = FmtMac(raw, off + 8);
        string senderIP  = FmtIP(raw, off + 14);
        string targetMac = FmtMac(raw, off + 18);
        string targetIP  = FmtIP(raw, off + 24);
        layers.Add(new DissectedLayer
        {
            Label  = "ARP",
            Color  = "#e6a23c",
            Offset = off,
            Length = 28,
            Fields =
            [
                new("Opcode",     op == 1 ? "1 (Request)" : op == 2 ? "2 (Reply)" : op.ToString()),
                new("Sender MAC", senderMac),
                new("Sender IP",  senderIP),
                new("Target MAC", targetMac),
                new("Target IP",  targetIP),
            ],
        });
    }

    private static bool TryDissectIPv4(
        byte[] raw, int off, int direction, MediusPortMap portMap, List<DissectedLayer> layers,
        out int proto, out int payOff, out int totalLen, out int ihl)
    {
        proto = 0; payOff = 0; totalLen = 0; ihl = 0;
        if (raw.Length < off + 20) return false;

        ihl       = (raw[off] & 0x0F) * 4;
        totalLen  = ReadU16BE(raw, off + 2);
        int  ttl  = raw[off + 8];
        proto     = raw[off + 9];
        string srcIP = FmtIP(raw, off + 12);
        string dstIP = FmtIP(raw, off + 16);
        int    ipId  = ReadU16BE(raw, off + 4);
        int    flagsFrag = ReadU16BE(raw, off + 6);
        int    flags     = (flagsFrag >> 13) & 0x7;
        int    fragOff   = flagsFrag & 0x1FFF;

        layers.Add(new DissectedLayer
        {
            Label  = "Internet Protocol Version 4",
            Color  = "#67c23a",
            Offset = off,
            Length = ihl,
            Fields =
            [
                new("Version",             ((raw[off] >> 4) & 0xF).ToString()),
                new("Header Length",       $"{ihl} bytes"),
                new("Total Length",        totalLen.ToString()),
                new("Identification",      $"0x{ipId:X4} ({ipId})"),
                new("Flags",               $"0x{flags:X}"),
                new("Fragment Offset",     fragOff.ToString()),
                new("TTL",                 ttl.ToString()),
                new("Protocol",            $"{proto} ({IpProtoName(proto)})"),
                new("Source Address",      srcIP),
                new("Destination Address", dstIP),
            ],
        });

        payOff = off + ihl;

        if (proto == 1) // ICMP
        {
            if (raw.Length >= payOff + 8)
            {
                int icmpType = raw[payOff];
                int icmpCode = raw[payOff + 1];
                int icmpId   = ReadU16BE(raw, payOff + 4);
                int icmpSeq  = ReadU16BE(raw, payOff + 6);
                layers.Add(new DissectedLayer
                {
                    Label  = "Internet Control Message Protocol",
                    Color  = "#909399",
                    Offset = payOff,
                    Length = Math.Min(8, raw.Length - payOff),
                    Fields =
                    [
                        new("Type",       $"{icmpType} ({IcmpTypeName(icmpType)})"),
                        new("Code",       icmpCode.ToString()),
                        new("Identifier", $"0x{icmpId:X4}"),
                        new("Sequence",   icmpSeq.ToString()),
                    ],
                });
            }
            return true;
        }

        if (proto == 6) // TCP
        {
            DissectTcp(raw, payOff, totalLen, ihl, srcIP, dstIP, direction, portMap, layers);
            return true;
        }

        if (proto == 17) // UDP
        {
            DissectUdp(raw, payOff, layers);
            return true;
        }

        return true;
    }

    private static void DissectTcp(
        byte[] raw, int payOff, int totalLen, int ihl,
        string srcIP, string dstIP, int direction, MediusPortMap portMap,
        List<DissectedLayer> layers)
    {
        if (raw.Length < payOff + 20) return;

        int srcPort = ReadU16BE(raw, payOff);
        int dstPort = ReadU16BE(raw, payOff + 2);
        uint seqNum = ReadU32BE(raw, payOff + 4);
        uint ackNum = ReadU32BE(raw, payOff + 8);
        int dataOff = ((raw[payOff + 12] >> 4) & 0xF) * 4;
        int tcpFlags = raw[payOff + 13];
        int win      = ReadU16BE(raw, payOff + 14);

        var flagNames = new List<string>(6);
        if ((tcpFlags & 0x01) != 0) flagNames.Add("FIN");
        if ((tcpFlags & 0x02) != 0) flagNames.Add("SYN");
        if ((tcpFlags & 0x04) != 0) flagNames.Add("RST");
        if ((tcpFlags & 0x08) != 0) flagNames.Add("PSH");
        if ((tcpFlags & 0x10) != 0) flagNames.Add("ACK");
        if ((tcpFlags & 0x20) != 0) flagNames.Add("URG");

        string server = portMap.ServerFromPorts(srcPort, dstPort);
        string tcpLabel = server.Length > 0
            ? $"Transmission Control Protocol  [{server} :{(dstPort > srcPort ? dstPort : srcPort)}]"
            : "Transmission Control Protocol";

        layers.Add(new DissectedLayer
        {
            Label  = tcpLabel,
            Color  = "#e6a23c",
            Offset = payOff,
            Length = dataOff,
            Fields =
            [
                new("Source Port",      srcPort.ToString()),
                new("Destination Port", dstPort.ToString()),
                new("Sequence Number",  seqNum.ToString()),
                new("Acknowledgment",   ackNum.ToString()),
                new("Header Length",    $"{dataOff} bytes"),
                new("Flags",            $"0x{tcpFlags:X2} ({(flagNames.Count > 0 ? string.Join(", ", flagNames) : "none")})"),
                new("Window",           win.ToString()),
            ],
        });

        int tcpPayOff = payOff + dataOff;
        // Use IP total length to exclude Ethernet padding from TCP payload length.
        int tcpPayLen = totalLen - ihl - dataOff;

        bool isScertPort = portMap.ScertPorts.Contains(srcPort) || portMap.ScertPorts.Contains(dstPort);
        if (tcpPayLen > 0 && server.Length > 0 && isScertPort)
        {
            var tcpPayload = raw.AsSpan(tcpPayOff, tcpPayLen).ToArray();
            var scertMsgs  = ParseScert(tcpPayload, tcpPayOff);
            if (scertMsgs.Count > 0)
            {
                EmitScertLayers(scertMsgs, server, layers);
                return;
            }
        }

        // Fallback: generic TCP data layer
        if (tcpPayLen > 0)
        {
            layers.Add(new DissectedLayer
            {
                Label  = $"Data ({tcpPayLen} bytes)",
                Color  = "#909399",
                Offset = tcpPayOff,
                Length = tcpPayLen,
                Fields = [new("Length", $"{tcpPayLen} bytes")],
            });
        }
    }

    private static void DissectUdp(byte[] raw, int payOff, List<DissectedLayer> layers)
    {
        if (raw.Length < payOff + 8) return;
        int srcPort = ReadU16BE(raw, payOff);
        int dstPort = ReadU16BE(raw, payOff + 2);
        int udpLen  = ReadU16BE(raw, payOff + 4);
        layers.Add(new DissectedLayer
        {
            Label  = "User Datagram Protocol",
            Color  = "#409eff",
            Offset = payOff,
            Length = 8,
            Fields =
            [
                new("Source Port",      srcPort.ToString()),
                new("Destination Port", dstPort.ToString()),
                new("Length",           udpLen.ToString()),
            ],
        });
        int udpPayLen = raw.Length - payOff - 8;
        if (udpPayLen > 0)
        {
            layers.Add(new DissectedLayer
            {
                Label  = $"Data ({udpPayLen} bytes)",
                Color  = "#909399",
                Offset = payOff + 8,
                Length = udpPayLen,
                Fields = [new("Length", $"{udpPayLen} bytes")],
            });
        }
    }

    private static void EmitScertLayers(IList<ParsedScertMessage> scertMsgs, string server, List<DissectedLayer> layers)
    {
        foreach (var msg in scertMsgs)
        {
            int    hdrLen          = msg.IsEncrypted ? 7 : 3;
            // For encrypted records the inner Medius bytes are still ciphertext,
            // so suffix "(encrypted)" rather than the bare RT_MSG name — this
            // makes capture tables read like "RT_MSG_CLIENT_APP_TOSERVER (encrypted)"
            // instead of just "RT_MSG_CLIENT_APP_TOSERVER" sitting next to plaintext
            // siblings that DO have a Medius name attached.
            string msgTypeDisplay =
                  msg.Medius != null   ? $"{msg.MsgTypeName}({msg.Medius.MsgTypeName})"
                : msg.IsEncrypted      ? $"{msg.MsgTypeName} (encrypted)"
                :                        msg.MsgTypeName;

            layers.Add(new DissectedLayer
            {
                Label  = $"SCERT [{server}]  {msgTypeDisplay}",
                Color  = "#c792ea",
                Offset = msg.FrameOffset,
                Length = hdrLen,
                Fields =
                [
                    new("Server",         server),
                    new("Message Type",   $"0x{(byte)msg.MsgType:X2} — {msgTypeDisplay}"),
                    new("Encrypted",      msg.IsEncrypted ? $"Yes (hash: 0x{msg.Hash!.Value:X8})" : "No"),
                    new("Payload Length", $"{msg.PayloadLen} bytes"),
                ],
            });

            if (msg.Medius is { } m)
            {
                int mediusPayLen = msg.PayloadLen - 2;
                var fields = new List<DissectedField>
                {
                    new("Message Class",  $"0x{m.MsgClass:X2} — {m.MsgClassName}"),
                    new("Message Type",   $"0x{m.MsgType:X2} — {m.MsgTypeName}"),
                    new("Payload Length", $"{mediusPayLen} bytes"),
                };
                fields.AddRange(m.PayloadFields);
                layers.Add(new DissectedLayer
                {
                    Label  = $"Medius [{m.MsgClassName}]  {m.MsgTypeName}",
                    Color  = "#ff9580",
                    Offset = m.FrameOffset,
                    Length = 2,
                    Fields = fields,
                });

                if (m.PayloadFields.Count == 0 && mediusPayLen > 0)
                {
                    layers.Add(new DissectedLayer
                    {
                        Label  = $"Medius Payload ({mediusPayLen} bytes)",
                        Color  = "#909399",
                        Offset = m.FrameOffset + 2,
                        Length = mediusPayLen,
                        Fields = [new("Length", $"{mediusPayLen} bytes")],
                    });
                }
            }
            else if (msg.ScertPayloadFields is { Count: > 0 } scertFields)
            {
                var fields = new List<DissectedField>
                {
                    new("Length", $"{msg.PayloadLen} bytes"),
                };
                fields.AddRange(scertFields);
                layers.Add(new DissectedLayer
                {
                    Label  = $"SCERT Payload — {msg.MsgTypeName}",
                    Color  = "#b5c9e8",
                    Offset = msg.FrameOffset + hdrLen,
                    Length = msg.PayloadLen,
                    Fields = fields,
                });
            }
            else if (msg.PayloadLen > 0)
            {
                layers.Add(new DissectedLayer
                {
                    Label  = $"SCERT Payload ({msg.PayloadLen} bytes)",
                    Color  = "#909399",
                    Offset = msg.FrameOffset + hdrLen,
                    Length = msg.PayloadLen,
                    Fields = [new("Length", $"{msg.PayloadLen} bytes")],
                });
            }
        }
    }

    // ── Enum / class name resolution ─────────────────────────────────────────

    /// <summary>
    /// Heuristic: do the supplied class+type bytes look like a legitimately
    /// decoded Medius header, or like random ciphertext? Used to suppress
    /// false labels when an encrypted record's payload is still scrambled.
    /// </summary>
    /// <remarks>
    /// Two-stage check:
    ///   1. class byte must be a known <see cref="NetMessageClass"/> value (0–5).
    ///   2. type byte must be defined in the message-id enum that the class
    ///      routes to (Lobby / LobbyExt / DME). Per-PS2-overrides (0x62–0x65)
    ///      are also accepted because <see cref="MediusTypeName"/> renders them.
    /// </remarks>
    private static bool LooksLikePlausibleMediusHeader(byte msgClass, byte msgType)
    {
        if (msgClass > 5) return false;

        Type? enumType = msgClass switch
        {
            0 or 3       => typeof(MediusDmeMessageIds),
            1 or 2 or 5  => typeof(MediusLobbyMessageId),
            4            => typeof(MediusLobbyExtMessageId),
            _            => null,
        };
        if (enumType == null) return false;

        // The PS2 override range is always legitimate for Lobby-family classes.
        if ((msgClass == 1 || msgClass == 2 || msgClass == 5)
            && s_ps2LobbyOverrides.ContainsKey(msgType))
            return true;

        return Enum.IsDefined(enumType, msgType);
    }

    /// <summary>
    /// PS2-era overrides for Lobby-class message type bytes that share their
    /// id with a newer Medius name. SOCOM II et al. use the older meaning,
    /// which is what we display since this codebase emulates PS2.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="Enum.GetName(Type, object)"/> picks one of the
    /// duplicate enum members alphabetically — and for these four values the
    /// alphabetic winner is the WRONG one for PS2 traffic
    /// (InvitePlayerToClan beats TicketLogin, etc.). The mislabel is severe:
    /// a TicketLogin sent during initial auth shows up as "InvitePlayerToClan"
    /// in dissection, which makes the trace look like the client is sending
    /// clan invites before it has even logged in.
    /// </remarks>
    private static readonly Dictionary<byte, string> s_ps2LobbyOverrides = new()
    {
        [0x62] = "TicketLogin",          // vs. InvitePlayerToClan
        [0x63] = "TicketLoginResponse",  // vs. InvitePlayerToClanResponse
        [0x64] = "GetUniverse",          // vs. CheckMyClanInvitations
        [0x65] = "GetUniverseResponse",  // vs. CheckMyClanInvitationsResponse
    };

    /// <summary>
    /// Resolve a Medius message type byte to a symbolic name, handling the
    /// historical class-byte ambiguity in PS2-era titles.
    /// </summary>
    /// <remarks>
    /// Classes 2/3/5 (Application/LobbyReport/LobbyAuth) shared id spaces with
    /// the Lobby/DME tables in older Medius — class=5 (auth) routes lobby ids,
    /// class=3 (lobby-report) routes DME server ids, class=2 (app) routes lobby
    /// ids. If the class-specific lookup misses (e.g. encrypted payloads where
    /// the class byte is gibberish), fall back through Lobby → LobbyExt → DME
    /// rather than surface a bare hex byte.
    /// </remarks>
    private static string MediusTypeName(byte msgClass, byte msgType)
    {
        // PS2-era overrides only apply when the on-the-wire class byte says
        // we're in a class that routes lobby ids (1/2/5). For other classes
        // 0x62–0x65 mean something else and the normal lookup is correct.
        if ((msgClass == 1 || msgClass == 2 || msgClass == 5)
            && s_ps2LobbyOverrides.TryGetValue(msgType, out var ps2Name))
            return ps2Name;

        string? name = msgClass switch
        {
            0           => SafeEnumName(typeof(MediusDmeMessageIds),   msgType),
            1 or 2 or 5 => SafeEnumName(typeof(MediusLobbyMessageId),  msgType),
            3           => SafeEnumName(typeof(MediusDmeMessageIds),   msgType),
            4           => SafeEnumName(typeof(MediusLobbyExtMessageId), msgType),
            _ => null,
        };
        if (name != null) return name;

        // Last-resort fallback (used when the class byte is gibberish — e.g.
        // the record is actually still encrypted). Apply the same PS2 override
        // here so the trace doesn't read as clan traffic when it isn't.
        if (s_ps2LobbyOverrides.TryGetValue(msgType, out var fallback))
            return fallback;

        return SafeEnumName(typeof(MediusLobbyMessageId),    msgType)
            ?? SafeEnumName(typeof(MediusLobbyExtMessageId), msgType)
            ?? SafeEnumName(typeof(MediusDmeMessageIds),     msgType)
            ?? $"0x{msgType:X2}";
    }

    /// <summary>
    /// Display name for a <see cref="NetMessageClass"/> byte. CLR enum members
    /// are <c>MessageClassLobby</c>/etc.; strip that prefix so labels match the
    /// short forms (Lobby, DME, LobbyExt) the TS dissector emitted.
    /// </summary>
    private static string MsgClassName(byte msgClass)
    {
        var name = SafeEnumName(typeof(NetMessageClass), msgClass);
        if (name == null) return $"0x{msgClass:X2}";
        const string prefix = "MessageClass";
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
    }

    /// <summary>
    /// Resolve an enum value to a symbolic name. Trims namespace prefixes that
    /// match the type name (e.g. <c>MediusLanguage_USEnglish</c> → <c>USEnglish</c>)
    /// for cleaner display, matching the TS dissector's output.
    /// </summary>
    private static string? ResolveEnumName(MediusFieldDef f, uint val)
    {
        if (f.EnumDict is { } dict && dict.TryGetValue(val, out var dictName))
            return dictName;

        if (f.EnumType is { } enumType)
        {
            string? name = SafeEnumName(enumType, val);
            if (name == null) return null;

            // Strip a leading "{TypeName}_" prefix for readability when present.
            string typePrefix = enumType.Name + "_";
            if (name.StartsWith(typePrefix, StringComparison.Ordinal))
                return name[typePrefix.Length..];
            return name;
        }
        return null;
    }

    /// <summary>
    /// Wrapper over <see cref="Enum.GetName(Type, object)"/> that handles
    /// signed-enum-stored-as-unsigned and skips throwing on unknown values.
    /// </summary>
    private static string? SafeEnumName(Type enumType, uint val)
    {
        // For int-backed enums (most of ScertTypes), negative wire values are
        // stored as their two's-complement uint (-1 → 0xFFFFFFFF). Convert to
        // the underlying type before lookup.
        var underlying = Enum.GetUnderlyingType(enumType);
        object key = underlying == typeof(byte)  ? (object)(byte)val
                   : underlying == typeof(sbyte) ? (object)(sbyte)val
                   : underlying == typeof(short) ? (object)(short)val
                   : underlying == typeof(ushort)? (object)(ushort)val
                   : underlying == typeof(int)   ? (object)(int)val
                   : underlying == typeof(uint)  ? (object)val
                   : underlying == typeof(long)  ? (object)(long)val
                   : underlying == typeof(ulong) ? (object)(ulong)val
                   : val;
        return Enum.GetName(enumType, key);
    }

    // ── Byte readers / formatters ────────────────────────────────────────────

    private static ushort ReadU16BE(byte[] b, int o)
        => o + 1 < b.Length ? (ushort)((b[o] << 8) | b[o + 1]) : (ushort)0;

    private static ushort ReadU16LE(byte[] b, int o)
        => o + 1 < b.Length ? (ushort)(b[o] | (b[o + 1] << 8)) : (ushort)0;

    private static uint ReadU32BE(byte[] b, int o)
        => o + 3 < b.Length
            ? (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3])
            : 0u;

    private static string FmtMac(byte[] b, int o)
        => o + 6 <= b.Length
            ? $"{b[o]:X2}:{b[o+1]:X2}:{b[o+2]:X2}:{b[o+3]:X2}:{b[o+4]:X2}:{b[o+5]:X2}"
            : "00:00:00:00:00:00";

    private static string FmtIP(byte[] b, int o)
        => o + 4 <= b.Length ? $"{b[o]}.{b[o+1]}.{b[o+2]}.{b[o+3]}" : "0.0.0.0";

    private static string EtherTypeName(ushort t) => t switch
    {
        0x0800 => "IPv4",
        0x0806 => "ARP",
        0x86DD => "IPv6",
        _      => "Unknown",
    };

    private static string IpProtoName(int p) => p switch
    {
        1  => "ICMP",
        6  => "TCP",
        17 => "UDP",
        _  => "Unknown",
    };

    private static string IcmpTypeName(int t) => t switch
    {
        0  => "Echo Reply",
        3  => "Destination Unreachable",
        8  => "Echo Request",
        11 => "Time Exceeded",
        _  => "Other",
    };
}
