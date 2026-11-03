// Wire shapes shared by the Packet Viewer page, list and composer. They mirror the bridge DTOs
// (CapturedPacketInfo / ParsedPacketRow / DissectedLayer / CatalogEntry) — see socketClient.ts.

/** A captured packet received from the bridge. */
export interface CapturedPacket {
    /** 0 = TX (PS2 → host), 1 = RX (host → PS2). */
    direction: number;
    directionName: string;
    timestampUs: number;
    length: number;
    /** Base64-encoded raw Ethernet frame. */
    data: string;
    /** Layer tree from the bridge dissector (CodeDesigner.Ps2.Multiplayer PacketDissector). */
    layers?: DissectedLayer[];
}

/** Pre-parsed table row returned by the bridge alongside a captured packet. */
export interface ParsedPacketRow {
    no: number;
    time: string;
    dir: string;
    server: string;
    src: string;
    dst: string;
    proto: string;
    len: number;
    info: string;
}

/** Adapter status snapshot. */
export interface NetStatus {
    enabled: boolean;
    adapterType: number;
    ps2Mac: string;
    ps2IP: string;
}

/** Traffic statistics snapshot. */
export interface NetStats {
    txPackets: number;
    rxPackets: number;
    txBytes: number;
    rxBytes: number;
    captureQueueSize: number;
    droppedCount: number;
}

/** One protocol layer of the dissection tree. */
export interface DissectedLayer {
    label: string;
    /** Protocol accent colour chosen by the bridge dissector. */
    color: string;
    fields: DissectedField[];
    /** Byte range of the layer's header in the raw frame, for highlighting. */
    offset: number;
    length: number;
}

export interface DissectedField {
    name: string;
    value: string;
}

/** One field of a catalog message (MessageCatalog.cs). */
export interface CatalogField {
    name: string;
    /** uint, int, bool, f32, vec3, string, bytes, cstr or hex. */
    type: string;
    offset: number;
    length: number;
    options?: string[] | null;
    auto?: string | null;
    optional?: boolean;
}

/** One message the multiplayer server defines (NETCATALOG). */
export interface CatalogEntry {
    /** SCERT, Lobby, LobbyExt, MGCL, DME, Game or Object. */
    protocol: string;
    cls: number;
    id: number;
    name: string;
    description: string;
    size: number | null;
    fields: CatalogField[];
}

/** A packet payload for pcap save/load operations. */
export interface PcapPacketEntry {
    direction: number;
    timestampUs: number;
    data: string;
}

/** Frame edit options for inject / buildFrame. */
export interface FrameEdit {
    /** New TCP/UDP payload (base64). */
    payload?: string;
    fix: boolean;
    tcpContinue: boolean;
}

/** Actions posted to the extension host. */
export type PacketViewerAction =
    | { type: 'startCapture'; direction: number; maxQueue: number }
    | { type: 'stopCapture' }
    | { type: 'recvCapture'; maxPackets: number }
    | { type: 'setFilter'; filterType: number; protocol?: number; port?: number; ip?: string }
    | { type: 'getStatus' }
    | { type: 'getStats' }
    | { type: 'getCatalog' }
    | { type: 'clearPackets' }
    | { type: 'openPcap' }
    | { type: 'savePcap'; packets: PcapPacketEntry[] }
    | ({ type: 'inject'; frame: string; tx: boolean } & FrameEdit)
    | ({ type: 'buildFrame'; frame: string; requestId: number } & FrameEdit)
    | { type: 'buildMessage'; protocol: string; id: number; values: Record<string, string>; rtType?: number; target?: number };

/** SCERT record types that carry an app message (the wrapper choices in the composer). */
export const APP_RT_TYPES: Array<{ id: number; name: string }> = [
    { id: 0x0B, name: 'CLIENT_APP_TOSERVER' },
    { id: 0x0A, name: 'SERVER_APP' },
    { id: 0x02, name: 'CLIENT_APP_BROADCAST' },
    { id: 0x03, name: 'CLIENT_APP_SINGLE' },
    { id: 0x04, name: 'CLIENT_APP_LIST' },
    { id: 0x0C, name: 'UDP_APP' },
];

// ── Byte helpers ─────────────────────────────────────────────────────────────

export function b64ToBytes(b64: string): Uint8Array {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
    return bytes;
}

export function bytesToB64(bytes: Uint8Array): string {
    let bin = '';
    for (let i = 0; i < bytes.length; i++) { bin += String.fromCharCode(bytes[i]); }
    return btoa(bin);
}

/** Hex text, 16 bytes per line. */
export function bytesToHex(bytes: Uint8Array): string {
    const lines: string[] = [];
    for (let i = 0; i < bytes.length; i += 16) {
        lines.push(Array.from(bytes.subarray(i, i + 16), b => b.toString(16).toUpperCase().padStart(2, '0')).join(' '));
    }
    return lines.join('\n');
}

/** Parse hex text (whitespace, commas and 0x prefixes ignored). Returns null when it isn't hex. */
export function hexToBytes(text: string): Uint8Array | null {
    const clean = text.replace(/0x/gi, '').replace(/[\s,]/g, '');
    if (clean.length % 2 !== 0 || /[^0-9a-f]/i.test(clean)) { return null; }
    const out = new Uint8Array(clean.length / 2);
    for (let i = 0; i < out.length; i++) { out[i] = parseInt(clean.substr(i * 2, 2), 16); }
    return out;
}
