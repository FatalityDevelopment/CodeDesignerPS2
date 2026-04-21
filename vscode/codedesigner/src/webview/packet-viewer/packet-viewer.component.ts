import {
    Component,
    signal,
    computed,
    ChangeDetectionStrategy,
    ViewEncapsulation,
} from '@angular/core';

// ── Types ────────────────────────────────────────────────────────────────────

/** A captured packet received from the bridge. */
export interface CapturedPacket {
    /** 0=TX (PS2→Host), 1=RX (Host→PS2). */
    direction: number;
    directionName: string;
    timestampUs: number;
    length: number;
    /** Base64-encoded raw Ethernet frame. */
    data: string;
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

/** A parsed protocol layer for the dissection tree. */
export interface DissectedLayer {
    label: string;          // e.g. "Ethernet II", "IPv4", "TCP"
    color: string;          // accent colour for the row
    fields: DissectedField[];
    /** Byte offset and length in the raw frame for highlighting. */
    offset: number;
    length: number;
}

export interface DissectedField {
    name: string;
    value: string;
}

/** A packet payload for pcap save/load operations. */
export interface PcapPacketEntry {
    direction: number;
    timestampUs: number;
    data: string; // base64-encoded raw Ethernet frame
}

/** Actions emitted to the host page component. */
export type PacketViewerAction =
    | { type: 'startCapture'; direction: number; maxQueue: number }
    | { type: 'stopCapture' }
    | { type: 'recvCapture'; maxPackets: number }
    | { type: 'setFilter'; filterType: number; protocol?: number; port?: number; ip?: string }
    | { type: 'getStatus' }
    | { type: 'getStats' }
    | { type: 'disassemble'; data: string }
    | { type: 'clearPackets' }
    | { type: 'openPcap' }
    | { type: 'savePcap'; packets: PcapPacketEntry[] };

// ── Helpers ──────────────────────────────────────────────────────────────────

function b64ToBytes(b64: string): Uint8Array {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
    return bytes;
}

function toHex(b: number, pad = 2): string {
    return b.toString(16).toUpperCase().padStart(pad, '0');
}

function readU16BE(buf: Uint8Array, off: number): number {
    if (off + 1 >= buf.length) { return 0; }
    return (buf[off] << 8) | buf[off + 1];
}

function readU32BE(buf: Uint8Array, off: number): number {
    if (off + 3 >= buf.length) { return 0; }
    return ((buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3]) >>> 0;
}

function fmtMac(buf: Uint8Array, off: number): string {
    if (off + 6 > buf.length) { return '00:00:00:00:00:00'; }
    return Array.from(buf.slice(off, off + 6)).map(b => toHex(b)).join(':');
}

function fmtIP(buf: Uint8Array, off: number): string {
    if (off + 4 > buf.length) { return '0.0.0.0'; }
    return `${buf[off]}.${buf[off + 1]}.${buf[off + 2]}.${buf[off + 3]}`;
}

function readU16LE(buf: Uint8Array, off: number): number {
    if (off + 1 >= buf.length) { return 0; }
    return buf[off] | (buf[off + 1] << 8);
}

// ── SCERT / Medius protocol tables ───────────────────────────────────────────

/** Known Medius server ports and their names, matching config.json defaults.
 *  MUIS = Medius Universe Information Server
 *  MAS  = Medius Authentication Server
 *  MLS  = Medius Lobby Server
 *  DME  = Distributed Multi-player Engine
 *  NAT  = NAT traversal server
 *  DNAS = Dynamic Network Authentication System
 */
const MEDIUS_PORTS = new Map<number, string>([
    [10071, 'MUIS'],
    [10078, 'MAS'],
    [10075, 'MLS'],
    [10070, 'DME'],
    [10069, 'NAT'],
    [10060, 'DNAS'],
]);

/** RT_MSG_TYPE wire values → display name. Source: ScertTypes.cs RT_MSG_TYPE enum. */
const RT_MSG_TYPE_NAMES = new Map<number, string>([
    [0x00, 'RT_MSG_CLIENT_CONNECT_TCP'],
    [0x01, 'RT_MSG_CLIENT_DISCONNECT'],
    [0x02, 'RT_MSG_CLIENT_APP_BROADCAST'],
    [0x03, 'RT_MSG_CLIENT_APP_SINGLE'],
    [0x04, 'RT_MSG_CLIENT_APP_LIST'],
    [0x05, 'RT_MSG_CLIENT_ECHO'],
    [0x06, 'RT_MSG_SERVER_CONNECT_REJECT'],
    [0x07, 'RT_MSG_SERVER_CONNECT_ACCEPT_TCP'],
    [0x08, 'RT_MSG_SERVER_CONNECT_NOTIFY'],
    [0x09, 'RT_MSG_SERVER_DISCONNECT_NOTIFY'],
    [0x0A, 'RT_MSG_SERVER_APP'],
    [0x0B, 'RT_MSG_CLIENT_APP_TOSERVER'],
    [0x0C, 'RT_MSG_UDP_APP'],
    [0x0D, 'RT_MSG_CLIENT_SET_RECV_FLAG'],
    [0x0E, 'RT_MSG_CLIENT_SET_AGG_TIME'],
    [0x0F, 'RT_MSG_CLIENT_FLUSH_ALL'],
    [0x10, 'RT_MSG_CLIENT_FLUSH_SINGLE'],
    [0x11, 'RT_MSG_SERVER_FORCED_DISCONNECT'],
    [0x12, 'RT_MSG_CLIENT_CRYPTKEY_PUBLIC'],
    [0x13, 'RT_MSG_SERVER_CRYPTKEY_PEER'],
    [0x14, 'RT_MSG_SERVER_CRYPTKEY_GAME'],
    [0x15, 'RT_MSG_CLIENT_CONNECT_TCP_AUX_UDP'],
    [0x16, 'RT_MSG_CLIENT_CONNECT_AUX_UDP'],
    [0x17, 'RT_MSG_CLIENT_CONNECT_READY_AUX_UDP'],
    [0x18, 'RT_MSG_SERVER_INFO_AUX_UDP'],
    [0x19, 'RT_MSG_SERVER_CONNECT_ACCEPT_AUX_UDP'],
    [0x1A, 'RT_MSG_SERVER_CONNECT_COMPLETE'],
    [0x1B, 'RT_MSG_CLIENT_CRYPTKEY_PEER'],
    [0x1C, 'RT_MSG_SERVER_SYSTEM_MESSAGE'],
    [0x1D, 'RT_MSG_SERVER_CHEAT_QUERY'],
    [0x1E, 'RT_MSG_SERVER_MEMORY_POKE'],
    [0x1F, 'RT_MSG_SERVER_ECHO'],
    [0x20, 'RT_MSG_CLIENT_DISCONNECT_WITH_REASON'],
    [0x21, 'RT_MSG_CLIENT_CONNECT_READY_TCP'],
    [0x22, 'RT_MSG_SERVER_CONNECT_REQUIRE'],
    [0x23, 'RT_MSG_CLIENT_CONNECT_READY_REQUIRE'],
    [0x24, 'RT_MSG_CLIENT_HELLO'],
    [0x25, 'RT_MSG_SERVER_HELLO'],
    [0x26, 'RT_MSG_SERVER_STARTUP_INFO_NOTIFY'],
    [0x27, 'RT_MSG_CLIENT_PEER_QUERY'],
    [0x28, 'RT_MSG_SERVER_PEER_QUERY_NOTIFY'],
    [0x29, 'RT_MSG_CLIENT_PEER_QUERY_LIST'],
    [0x2A, 'RT_MSG_SERVER_PEER_QUERY_LIST_NOTIFY'],
    [0x2B, 'RT_MSG_CLIENT_WALLCLOCK_QUERY'],
    [0x2C, 'RT_MSG_SERVER_WALLCLOCK_QUERY_NOTIFY'],
    [0x2D, 'RT_MSG_CLIENT_TIMEBASE_QUERY'],
    [0x2E, 'RT_MSG_SERVER_TIMEBASE_QUERY_NOTIFY'],
    [0x2F, 'RT_MSG_CLIENT_TOKEN_MESSAGE'],
    [0x30, 'RT_MSG_SERVER_TOKEN_MESSAGE'],
    [0x31, 'RT_MSG_CLIENT_SYSTEM_MESSAGE'],
    [0x32, 'RT_MSG_CLIENT_APP_BROADCAST_QOS'],
    [0x33, 'RT_MSG_CLIENT_APP_SINGLE_QOS'],
    [0x34, 'RT_MSG_CLIENT_APP_LIST_QOS'],
    [0x35, 'RT_MSG_CLIENT_MAX_MSGLEN'],
    [0x36, 'RT_MSG_SERVER_MAX_MSGLEN'],
    [0x3B, 'RT_MSG_CLIENT_MULTI_APP_TOSERVER'],
    [0x3C, 'RT_MSG_SERVER_MULTI_APP_TOCLIENT'],
]);

/** NetMessageClass byte → display name. Source: ScertTypes.cs NetMessageClass enum. */
const NET_MSG_CLASS_NAMES = new Map<number, string>([
    [0, 'DME'],
    [1, 'Lobby'],
    [2, 'Application'],
    [3, 'LobbyReport'],
    [4, 'LobbyExt'],
    [5, 'LobbyAuth'],
]);

/**
 * MediusLobbyMessageId byte → display name. Source: ScertTypes.cs.
 * Used when NetMessageClass = 1 (Lobby).
 */
const MEDIUS_LOBBY_MSG = new Map<number, string>([
    [0x00, 'WorldReport0'],
    [0x01, 'PlayerReport'],
    [0x02, 'EndGameReport'],
    [0x03, 'SessionBegin'],
    [0x04, 'SessionBeginResponse'],
    [0x05, 'SessionEnd'],
    [0x06, 'SessionEndResponse'],
    [0x07, 'AccountLogin'],
    [0x08, 'AccountLoginResponse'],
    [0x09, 'AccountRegistration'],
    [0x0A, 'AccountRegistrationResponse'],
    [0x0B, 'AccountGetProfile'],
    [0x0C, 'AccountGetProfileResponse'],
    [0x0D, 'AccountUpdateProfile'],
    [0x0E, 'AccountUpdateProfileResponse'],
    [0x0F, 'AccountUpdatePassword'],
    [0x11, 'AccountUpdateStats'],
    [0x12, 'AccountUpdateStatsResponse'],
    [0x13, 'AccountDelete'],
    [0x14, 'AccountDeleteResponse'],
    [0x15, 'AccountLogout'],
    [0x16, 'AccountLogoutResponse'],
    [0x17, 'AccountGetID'],
    [0x18, 'AccountGetIDResponse'],
    [0x19, 'AnonymousLogin'],
    [0x1A, 'AnonymousLoginResponse'],
    [0x1B, 'GetMyIP'],
    [0x1C, 'GetMyIPResponse'],
    [0x1D, 'CreateGameRequest'],
    [0x1E, 'CreateGameResponse'],
    [0x1F, 'CreateGameOnSelf'],
    [0x20, 'CreateGameOnSelfResponse'],
    [0x21, 'CreateChannelRequest'],
    [0x22, 'CreateChannelResponse'],
    [0x23, 'JoinGameRequest'],
    [0x24, 'JoinGameResponse'],
    [0x25, 'JoinChannel'],
    [0x26, 'JoinChannelResponse'],
    [0x27, 'JoinChannelFwd'],
    [0x28, 'JoinChannelFwdResponse'],
    [0x29, 'GameList'],
    [0x2A, 'GameListResponse'],
    [0x2B, 'ChannelList'],
    [0x2C, 'ChannelListResponse'],
    [0x2D, 'LobbyWorldPlayerList'],
    [0x2E, 'LobbyWorldPlayerListResponse'],
    [0x2F, 'GameWorldPlayerList'],
    [0x30, 'GameWorldPlayerListResponse'],
    [0x31, 'PlayerInfo'],
    [0x32, 'PlayerInfoResponse'],
    [0x33, 'GameInfo'],
    [0x34, 'GameInfoResponse'],
    [0x35, 'ChannelInfo'],
    [0x36, 'ChannelInfoResponse'],
    [0x37, 'FindWorldByName'],
    [0x38, 'FindWorldByNameResponse'],
    [0x39, 'FindPlayer'],
    [0x3A, 'FindPlayerResponse'],
    [0x3B, 'ChatMessage'],
    [0x3C, 'ChatFwdMessage'],
    [0x3D, 'GetBuddyList'],
    [0x3E, 'GetBuddyListResponse'],
    [0x3F, 'AddToBuddyList'],
    [0x40, 'AddToBuddyListResponse'],
    [0x41, 'RemoveFromBuddyList'],
    [0x42, 'RemoveFromBuddyListResponse'],
    [0x43, 'AddToBuddyListConfirmationRequest'],
    [0x44, 'AddToBuddyListConfirmationResponse'],
    [0x45, 'AddToBuddyListFwdConfirmationRequest'],
    [0x46, 'AddToBuddyListFwdConfirmationResponse'],
    [0x47, 'Policy'],
    [0x48, 'PolicyResponse'],
    [0x49, 'UpdateUserState'],
    [0x4A, 'ErrorMessage'],
    [0x4B, 'GetAnnouncements'],
    [0x4C, 'GetAllAnnouncements'],
    [0x4D, 'GetAnnouncementsResponse'],
    [0x4E, 'SetGameListFilter'],
    [0x4F, 'SetGameListFilterResponse'],
    [0x50, 'ClearGameListFilter'],
    [0x51, 'ClearGameListFilterResponse'],
    [0x52, 'GetGameListFilter'],
    [0x53, 'GetGameListFilterResponse'],
    [0x54, 'CreateClan'],
    [0x55, 'CreateClanResponse'],
    [0x56, 'DisbandClan'],
    [0x57, 'DisbandClanResponse'],
    [0x58, 'GetClanByID'],
    [0x59, 'GetClanByIDResponse'],
    [0x5A, 'GetClanByName'],
    [0x5B, 'GetClanByNameResponse'],
    [0x5C, 'TransferClanLeadership'],
    [0x5D, 'TransferClanLeadershipResponse'],
    [0x5E, 'AddPlayerToClan'],
    [0x5F, 'AddPlayerToClanResponse'],
    [0x60, 'RemovePlayerFromClan'],
    [0x61, 'RemovePlayerFromClanResponse'],
    [0x62, 'TicketLogin / InvitePlayerToClan'],
    [0x63, 'TicketLoginResponse / InvitePlayerToClanResponse'],
    [0x64, 'GetUniverse / CheckMyClanInvitations'],
    [0x65, 'GetUniverseResponse / CheckMyClanInvitationsResponse'],
    [0x66, 'RespondToClanInvitation'],
    [0x67, 'RespondToClanInvitationResponse'],
    [0x68, 'RevokeClanInvitation'],
    [0x69, 'RevokeClanInvitationResponse'],
    [0x6A, 'RequestClanTeamChallenge'],
    [0x6B, 'RequestClanTeamChallengeResponse'],
    [0x6C, 'GetMyClanMessages'],
    [0x6D, 'GetMyClanMessagesResponse'],
    [0x6E, 'SendClanMessage'],
    [0x6F, 'SendClanMessageResponse'],
    [0x70, 'ModifyClanMessage'],
    [0x71, 'ModifyClanMessageResponse'],
    [0x72, 'DeleteClanMessage'],
    [0x73, 'DeleteClanMessageResponse'],
    [0x74, 'RespondToClanTeamChallenge'],
    [0x75, 'RespondToClanTeamChallengeResponse'],
    [0x76, 'RevokeClanTeamChallenge'],
    [0x77, 'RevokeClanTeamChallengeResponse'],
    [0x78, 'GetClanTeamChallengeHistory'],
    [0x79, 'GetClanTeamChallengeHistoryResponse'],
    [0x7A, 'GetClanInvitationsSent'],
    [0x7B, 'GetClanInvitationsSentResponse'],
    [0x7C, 'GetMyClans'],
    [0x7D, 'GetMyClansResponse'],
    [0x7E, 'GetAllClanMessages'],
    [0x7F, 'GetAllClanMessagesResponse'],
    [0x80, 'ConfirmClanTeamChallenge'],
    [0x81, 'ConfirmClanTeamChallengeResponse'],
    [0x82, 'GetClanTeamChallenges'],
    [0x83, 'GetClanTeamChallengesResponse'],
    [0x84, 'UpdateClanStats'],
    [0x85, 'UpdateClanStatsResponse'],
    [0x86, 'VersionServer'],
    [0x87, 'VersionServerResponse'],
    [0x88, 'GetWorldSecurityLevel'],
    [0x89, 'GetWorldSecurityLevelResponse'],
    [0x8A, 'BanPlayer'],
    [0x8B, 'BanPlayerResponse'],
    [0x8C, 'GetLocations'],
    [0x8D, 'GetLocationsResponse'],
    [0x8E, 'PickLocation'],
    [0x8F, 'PickLocationResponse'],
    [0x90, 'GetClanMemberList'],
    [0x91, 'GetClanMemberListResponse'],
    [0x92, 'LadderPosition'],
    [0x93, 'LadderPositionResponse'],
    [0x94, 'LadderList'],
    [0x95, 'LadderListResponse'],
    [0x96, 'ChatToggle'],
    [0x97, 'ChatToggleResponse'],
    [0x98, 'TextFilter'],
    [0x99, 'TextFilterResponse'],
    [0x9A, 'ServerReassignGameMediusWorldID'],
    [0x9B, 'GetTotalGames'],
    [0x9C, 'GetTotalGamesResponse'],
    [0x9D, 'GetTotalChannels'],
    [0x9E, 'GetTotalChannelsResponse'],
    [0x9F, 'GetLobbyPlayerNames'],
    [0xA0, 'GetLobbyPlayerNamesResponse'],
    [0xA1, 'GetTotalUsers'],
    [0xA2, 'GetTotalUsersResponse'],
    [0xA3, 'SetLocalizationParams'],
    [0xA4, 'SetLocalizationParamsResponse'],
    [0xA5, 'FileCreate'],
    [0xA6, 'FileCreateResponse'],
    [0xA7, 'FileUpload'],
    [0xA8, 'FileUploadResponse'],
    [0xA9, 'FileUploadServerReq'],
    [0xAA, 'FileClose'],
    [0xAB, 'FileCloseResponse'],
    [0xAC, 'FileDownload'],
    [0xAD, 'FileDownloadResponse'],
    [0xAE, 'FileDownloadStream'],
    [0xAF, 'FileDownloadStreamResponse'],
    [0xB0, 'FileDelete'],
    [0xB1, 'FileDeleteResponse'],
    [0xB2, 'FileListFiles'],
    [0xB3, 'FileListFilesResponse'],
    [0xB4, 'FileUpdateAttributes'],
    [0xB5, 'FileUpdateAttributesResponse'],
    [0xB6, 'FileGetAttributes'],
    [0xB7, 'FileGetAttributesResponse'],
    [0xB8, 'FileUpdateMetaData'],
    [0xB9, 'FileUpdateMetaDataResponse'],
    [0xBA, 'FileGetMetaData'],
    [0xBB, 'FileGetMetaDataResponse'],
    [0xBC, 'FileSearchByMetaData'],
    [0xBD, 'FileSearchByMetaDataResponse'],
    [0xBE, 'FileCancelOperation'],
    [0xBF, 'FileCancelOperationResponse'],
    [0xC0, 'GetIgnoreList'],
    [0xC1, 'GetIgnoreListResponse'],
    [0xC2, 'AddToIgnoreList'],
    [0xC3, 'AddToIgnoreListResponse'],
    [0xC4, 'RemoveFromIgnoreList'],
    [0xC5, 'RemoveFromIgnoreListResponse'],
    [0xC6, 'SetMessageAsRead'],
    [0xC7, 'SetMessageAsReadResponse'],
    [0xC8, 'GetUniverseInformation'],
    [0xC9, 'UniverseNewsResponse'],
    [0xCA, 'UniverseStatusListResponse'],
    [0xCB, 'MachineSignaturePost'],
    [0xCC, 'LadderPositionFast'],
    [0xCD, 'LadderPositionFastResponse'],
    [0xCE, 'UpdateLadderStats'],
    [0xCF, 'UpdateLadderStatsResponse'],
    [0xD0, 'GetLadderStats'],
    [0xD1, 'GetLadderStatsResponse'],
    [0xD2, 'ClanLadderList'],
    [0xD3, 'ClanLadderListResponse'],
    [0xD4, 'ClanLadderPosition'],
    [0xD5, 'ClanLadderPositionResponse'],
    [0xD6, 'GetBuddyList_ExtraInfo'],
    [0xD7, 'GetBuddyList_ExtraInfoResponse'],
    [0xD8, 'GetTotalRankings'],
    [0xD9, 'GetTotalRankingsResponse'],
    [0xDA, 'GetClanMemberList_ExtraInfo'],
    [0xDB, 'GetClanMemberList_ExtraInfoResponse'],
    [0xDC, 'GetLobbyPlayerNames_ExtraInfo'],
    [0xDD, 'GetLobbyPlayerNames_ExtraInfoResponse'],
    [0xDE, 'BillingLogin'],
    [0xDF, 'BillingLoginResponse'],
    [0xE0, 'BillingListRequest'],
    [0xE1, 'BillingListResponse'],
    [0xE2, 'BillingDetailRequest'],
    [0xE3, 'BillingDetailResponse'],
    [0xE4, 'PurchaseProductRequest'],
    [0xE5, 'PurchaseProductResponse'],
    [0xE6, 'BillingInfo'],
    [0xE7, 'BillingInfoResponse'],
    [0xE8, 'BillingTunnelRequest'],
    [0xE9, 'BillingTunnelResponse'],
    [0xEA, 'GameList_ExtraInfo'],
    [0xEB, 'GameList_ExtraInfoResponse'],
    [0xEC, 'ChannelList_ExtraInfo'],
    [0xED, 'ChannelList_ExtraInfoResponse'],
    [0xEE, 'InvitePlayerToClan_ByName'],
    [0xEF, 'LadderList_ExtraInfo'],
    [0xF0, 'LadderList_ExtraInfoResponse'],
    [0xF1, 'LadderPosition_ExtraInfo'],
    [0xF2, 'LadderPosition_ExtraInfoResponse'],
    [0xF3, 'JoinGame'],
    [0xF4, 'CreateGame1'],
    [0xF5, 'UtilAddLobbyWorld'],
    [0xF6, 'UtilAddLobbyWorldResponse'],
    [0xF7, 'UtilAddGameWorld'],
    [0xF8, 'UtilAddGameWorldResponse'],
    [0xF9, 'UtilUpdateLobbyWorld'],
    [0xFA, 'UtilUpdateLobbyWorldResponse'],
    [0xFB, 'UtilUpdateGameWorld'],
    [0xFC, 'UtilUpdateGameWorldResponse'],
]);

/**
 * MediusLobbyExtMessageId byte → display name. Source: ScertTypes.cs.
 * Used when NetMessageClass = 4 (LobbyExt).
 */
const MEDIUS_LOBBY_EXT_MSG = new Map<number, string>([
    [0x00, 'CreateChannel1'],
    [0x01, 'UtilGetServerVersion'],
    [0x02, 'UtilGetServerVersionResponse'],
    [0x03, 'GetUniverse_ExtraInfo'],
    [0x04, 'UniverseStatusList_ExtraInfoResponse'],
    [0x05, 'AddToBuddyListConfirmation'],
    [0x06, 'AddToBuddyListFwdConfirmation'],
    [0x07, 'AddToBuddyListFwdConfirmationResponse'],
    [0x08, 'GetBuddyInvitations'],
    [0x09, 'GetBuddyInvitationsResponse'],
    [0x0A, 'DnasSignaturePost'],
    [0x0B, 'UpdateLadderStatsWide'],
    [0x0C, 'UpdateLadderStatsWideResponse'],
    [0x0D, 'GetLadderStatsWide'],
    [0x0E, 'GetLadderStatsWideResponse'],
    [0x0F, 'LadderList_ExtraInfo'],
    [0x10, 'UtilEventMsgHandler'],
    [0x11, 'UniverseVariableInformationResponse'],
    [0x12, 'SetLobbyWorldFilter'],
    [0x13, 'SetLobbyWorldFilterResponse'],
    [0x14, 'CreateChannel'],
    [0x15, 'ChannelList_ExtraInfo1'],
    [0x16, 'BinaryMessage'],
    [0x17, 'BinaryFwdMessage'],
    [0x18, 'PostDebugInfo'],
    [0x19, 'PostDebugInfoResponse'],
    [0x1A, 'UpdateClanLadderStatsWide_Delta'],
    [0x1B, 'UpdateClanLadderStatsWide_DeltaResponse'],
    [0x1C, 'GetLadderStatsWide_wIDArray'],
    [0x1D, 'GetLadderStatsWide_wIDArray_Response'],
    [0x1E, 'UniverseVariableSvoURLResponse'],
    [0x1F, 'ChannelList_ExtraInfo'],
    [0x23, 'GenericChatMessage'],
    [0x24, 'GenericChatFwdMessage'],
    [0x25, 'GenericChatSetFilterRequest'],
    [0x26, 'GenericChatSetFilterResponse'],
    [0x27, 'ExtendedSessionBeginRequest'],
    [0x28, 'TokenRequest'],
    [0x2A, 'GetServerTimeRequest'],
    [0x2B, 'GetServerTimeResponse'],
    [0x2C, 'VoteToBanPlayerRequest'],
    [0x2D, 'SetAutoChatHistoryRequest'],
    [0x2E, 'SetAutoChatHistoryResponse'],
    [0x2F, 'CreateGame'],
    [0x30, 'WorldReport'],
    [0x31, 'ClearGameListFilter'],
    [0x32, 'GetGameListFilterResponse'],
    [0x33, 'SetGameListFilter'],
    [0x34, 'SetGameListFilterResponse'],
    [0x35, 'GameInfo'],
    [0x36, 'GameInfoResponse'],
    [0x37, 'GameList_ExtraInfo'],
    [0x38, 'GameList_ExtraInfoResponse'],
    [0x39, 'AccountUpdateStats_OpenAccess'],
    [0x3A, 'AccountUpdateStats_OpenAccessResponse'],
    [0x3B, 'AddPlayerToClan_ByClanOfficer'],
    [0x3C, 'AddPlayerToClan_ByClanOfficerResponse'],
    [0x58, 'TicketLogin'],
    [0x59, 'TicketLoginResponse'],
    [0x73, 'GenericChatMessage1'],
    [0x74, 'GenericChatFwdMessage1'],
    [0x75, 'MediusTextFilter1'],
    [0x76, 'MediusTextFilterResponse1'],
    [0x7B, 'SetLocalizationParams1'],
    [0x86, 'SetLobbyWorldFilter1'],
    [0x87, 'SetLobbyWorldFilterResponse1'],
    [0x8B, 'SessionBegin1'],
]);

// ── SCERT parser ─────────────────────────────────────────────────────────────

interface ScertMediusParsed {
    /** NetMessageClass value. */
    msgClass: number;
    msgClassName: string;
    /** Medius message type byte (MediusLobbyMessageId / MediusLobbyExtMessageId). */
    msgType: number;
    msgTypeName: string;
    /** Byte offset in the full frame where the Medius class byte resides. */
    frameOffset: number;
}

interface ScertParsed {
    /** Raw type byte (before masking out the encrypted flag). */
    rawTypeByte: number;
    /** SCERT message type (low 7 bits). */
    msgType: number;
    msgTypeName: string;
    isEncrypted: boolean;
    /** Declared payload length (LE u16). */
    payloadLen: number;
    /** Optional 4-byte hash (present when encrypted). */
    hash?: number;
    /** Parsed Medius layer (present for APP_TOSERVER / SERVER_APP with class+type). */
    medius?: ScertMediusParsed;
    /** Byte offset of the SCERT header (type byte) within the full frame. */
    frameOffset: number;
    /** Total byte length of this SCERT record (header + hash? + payload). */
    frameLength: number;
}

/**
 * Return the server name for the port pair found in a TCP header,
 * checking both src and dst against MEDIUS_PORTS.
 */
function serverFromPorts(srcPort: number, dstPort: number): string {
    return MEDIUS_PORTS.get(dstPort) ?? MEDIUS_PORTS.get(srcPort) ?? '';
}

/** Resolve a Medius message type name given its class and type byte. */
function mediusTypeName(msgClass: number, msgType: number): string {
    if (msgClass === 1) { return MEDIUS_LOBBY_MSG.get(msgType) ?? `0x${toHex(msgType)}`; }
    if (msgClass === 4) { return MEDIUS_LOBBY_EXT_MSG.get(msgType) ?? `0x${toHex(msgType)}`; }
    return `0x${toHex(msgType)}`;
}

// RT_MSG_TYPE values that carry a Medius APP payload (class + type bytes)
const SCERT_APP_MSG_TYPES = new Set([
    0x0A, // RT_MSG_SERVER_APP
    0x0B, // RT_MSG_CLIENT_APP_TOSERVER
    0x3B, // RT_MSG_CLIENT_MULTI_APP_TOSERVER
    0x3C, // RT_MSG_SERVER_MULTI_APP_TOCLIENT
]);

/**
 * Parse SCERT messages from a raw TCP payload.
 *
 * Wire format (per SCERT spec):
 *   Plain:     [type:1][payloadLen:2 LE][payload:payloadLen]
 *   Encrypted: [type|0x80:1][payloadLen:2 LE][hash:4 LE][payload:payloadLen]
 *
 * @param tcpPayload  The TCP payload bytes (already offset past TCP header).
 * @param frameBase   The absolute byte offset of tcpPayload[0] within the full
 *                    Ethernet frame, so that frameOffset fields are frame-relative.
 */
function parseSCERT(tcpPayload: Uint8Array, frameBase: number): ScertParsed[] {
    const msgs: ScertParsed[] = [];
    let off = 0;

    while (off < tcpPayload.length) {
        // Need at least 3 bytes: type(1) + length(2)
        if (off + 3 > tcpPayload.length) { break; }

        const rawTypeByte  = tcpPayload[off];
        const isEncrypted  = (rawTypeByte & 0x80) !== 0;
        const msgType      = rawTypeByte & 0x7F;
        const payloadLen   = readU16LE(tcpPayload, off + 1);

        // Sanity: type must be a known SCERT value; bail if it looks like garbage
        if (!RT_MSG_TYPE_NAMES.has(msgType)) { break; }

        const hdrSize    = isEncrypted ? 7 : 3; // 1+2 + optional 4-byte hash
        const totalSize  = hdrSize + payloadLen;

        // Don't over-read beyond the buffer (fragmented segment — stop here)
        if (off + totalSize > tcpPayload.length) { break; }

        const hash = isEncrypted ? readU32BE(tcpPayload, off + 3) : undefined;

        // Parse Medius APP layer when present
        let medius: ScertMediusParsed | undefined;
        const isAppMsg = SCERT_APP_MSG_TYPES.has(msgType);
        if (isAppMsg && payloadLen >= 2) {
            const appOff    = off + hdrSize;
            const msgClass  = tcpPayload[appOff];
            const mType     = tcpPayload[appOff + 1];
            medius = {
                msgClass,
                msgClassName: NET_MSG_CLASS_NAMES.get(msgClass) ?? `0x${toHex(msgClass)}`,
                msgType: mType,
                msgTypeName: mediusTypeName(msgClass, mType),
                frameOffset: frameBase + appOff,
            };
        }

        msgs.push({
            rawTypeByte,
            msgType,
            msgTypeName: RT_MSG_TYPE_NAMES.get(msgType) ?? `0x${toHex(msgType)}`,
            isEncrypted,
            payloadLen,
            hash,
            medius,
            frameOffset: frameBase + off,
            frameLength: totalSize,
        });

        off += totalSize;
    }

    return msgs;
}

/** Lightweight Ethernet/IPv4/ARP/TCP/UDP/ICMP dissector. */
function dissectFrame(raw: Uint8Array): DissectedLayer[] {
    const layers: DissectedLayer[] = [];
    if (raw.length < 14) { return layers; }

    // ── Ethernet II ──
    const dstMac = fmtMac(raw, 0);
    const srcMac = fmtMac(raw, 6);
    const etherType = readU16BE(raw, 12);
    const ethFields: DissectedField[] = [
        { name: 'Destination', value: dstMac },
        { name: 'Source', value: srcMac },
        { name: 'Type', value: `0x${toHex(etherType, 4)} (${etherTypeName(etherType)})` },
    ];
    layers.push({ label: 'Ethernet II', color: '#5b9bd5', fields: ethFields, offset: 0, length: 14 });

    let off = 14;
    if (etherType === 0x0806) {
        // ── ARP ──
        if (raw.length >= off + 28) {
            const op = readU16BE(raw, off + 6);
            const senderMac = fmtMac(raw, off + 8);
            const senderIP = fmtIP(raw, off + 14);
            const targetMac = fmtMac(raw, off + 18);
            const targetIP = fmtIP(raw, off + 24);
            layers.push({
                label: 'ARP',
                color: '#e6a23c',
                fields: [
                    { name: 'Opcode', value: op === 1 ? '1 (Request)' : op === 2 ? '2 (Reply)' : String(op) },
                    { name: 'Sender MAC', value: senderMac },
                    { name: 'Sender IP', value: senderIP },
                    { name: 'Target MAC', value: targetMac },
                    { name: 'Target IP', value: targetIP },
                ],
                offset: off,
                length: 28,
            });
        }
        return layers;
    }

    if (etherType !== 0x0800) { return layers; } // only IPv4 beyond here

    // ── IPv4 ──
    if (raw.length < off + 20) { return layers; }
    const ihl = (raw[off] & 0x0F) * 4;
    const totalLen = readU16BE(raw, off + 2);
    const ttl = raw[off + 8];
    const proto = raw[off + 9];
    const srcIP = fmtIP(raw, off + 12);
    const dstIP = fmtIP(raw, off + 16);
    const ipId = readU16BE(raw, off + 4);
    const flagsFrag = readU16BE(raw, off + 6);
    const flags = (flagsFrag >> 13) & 0x7;
    const fragOff = flagsFrag & 0x1FFF;
    layers.push({
        label: 'Internet Protocol Version 4',
        color: '#67c23a',
        fields: [
            { name: 'Version', value: String((raw[off] >> 4) & 0xF) },
            { name: 'Header Length', value: `${ihl} bytes` },
            { name: 'Total Length', value: String(totalLen) },
            { name: 'Identification', value: `0x${toHex(ipId, 4)} (${ipId})` },
            { name: 'Flags', value: `0x${toHex(flags, 1)}` },
            { name: 'Fragment Offset', value: String(fragOff) },
            { name: 'TTL', value: String(ttl) },
            { name: 'Protocol', value: `${proto} (${ipProtoName(proto)})` },
            { name: 'Source Address', value: srcIP },
            { name: 'Destination Address', value: dstIP },
        ],
        offset: off,
        length: ihl,
    });

    const payloadOff = off + ihl;

    // ── ICMP ──
    if (proto === 1 && raw.length >= payloadOff + 8) {
        const icmpType = raw[payloadOff];
        const icmpCode = raw[payloadOff + 1];
        const icmpId = readU16BE(raw, payloadOff + 4);
        const icmpSeq = readU16BE(raw, payloadOff + 6);
        layers.push({
            label: 'Internet Control Message Protocol',
            color: '#909399',
            fields: [
                { name: 'Type', value: `${icmpType} (${icmpTypeName(icmpType)})` },
                { name: 'Code', value: String(icmpCode) },
                { name: 'Identifier', value: `0x${toHex(icmpId, 4)}` },
                { name: 'Sequence', value: String(icmpSeq) },
            ],
            offset: payloadOff,
            length: Math.min(8, raw.length - payloadOff),
        });
        return layers;
    }

    // ── TCP ──
    if (proto === 6 && raw.length >= payloadOff + 20) {
        const srcPort = readU16BE(raw, payloadOff);
        const dstPort = readU16BE(raw, payloadOff + 2);
        const seqNum = readU32BE(raw, payloadOff + 4);
        const ackNum = readU32BE(raw, payloadOff + 8);
        const dataOff = ((raw[payloadOff + 12] >> 4) & 0xF) * 4;
        const tcpFlags = raw[payloadOff + 13];
        const win = readU16BE(raw, payloadOff + 14);
        const flagNames: string[] = [];
        if (tcpFlags & 0x01) { flagNames.push('FIN'); }
        if (tcpFlags & 0x02) { flagNames.push('SYN'); }
        if (tcpFlags & 0x04) { flagNames.push('RST'); }
        if (tcpFlags & 0x08) { flagNames.push('PSH'); }
        if (tcpFlags & 0x10) { flagNames.push('ACK'); }
        if (tcpFlags & 0x20) { flagNames.push('URG'); }

        const server = serverFromPorts(srcPort, dstPort);
        const tcpLabel = server
            ? `Transmission Control Protocol  [${server} :${dstPort > srcPort ? dstPort : srcPort}]`
            : 'Transmission Control Protocol';

        layers.push({
            label: tcpLabel,
            color: '#e6a23c',
            fields: [
                { name: 'Source Port', value: String(srcPort) },
                { name: 'Destination Port', value: String(dstPort) },
                { name: 'Sequence Number', value: String(seqNum) },
                { name: 'Acknowledgment', value: String(ackNum) },
                { name: 'Header Length', value: `${dataOff} bytes` },
                { name: 'Flags', value: `0x${toHex(tcpFlags, 2)} (${flagNames.join(', ') || 'none'})` },
                { name: 'Window', value: String(win) },
            ],
            offset: payloadOff,
            length: dataOff,
        });

        // TCP payload
        const tcpPayOff = payloadOff + dataOff;
        const tcpPayLen = raw.length - tcpPayOff;

        if (tcpPayLen > 0 && server) {
            // ── SCERT parsing ──
            const tcpPayload = raw.slice(tcpPayOff);
            const scertMsgs  = parseSCERT(tcpPayload, tcpPayOff);

            if (scertMsgs.length > 0) {
                for (const msg of scertMsgs) {
                    const hdrLen = msg.isEncrypted ? 7 : 3;
                    const scertFields: DissectedField[] = [
                        { name: 'Server',       value: server },
                        { name: 'Message Type', value: `0x${toHex(msg.msgType)} — ${msg.msgTypeName}` },
                        { name: 'Encrypted',    value: msg.isEncrypted ? `Yes (hash: 0x${toHex(msg.hash!, 8)})` : 'No' },
                        { name: 'Payload Length', value: `${msg.payloadLen} bytes` },
                    ];

                    layers.push({
                        label: `SCERT [${server}]  ${msg.msgTypeName}`,
                        color: '#c792ea',
                        fields: scertFields,
                        offset: msg.frameOffset,
                        length: hdrLen,
                    });

                    // Medius APP sub-layer
                    if (msg.medius) {
                        const m = msg.medius;
                        layers.push({
                            label: `Medius [${m.msgClassName}]  ${m.msgTypeName}`,
                            color: '#ff9580',
                            fields: [
                                { name: 'Message Class', value: `0x${toHex(m.msgClass)} — ${m.msgClassName}` },
                                { name: 'Message Type',  value: `0x${toHex(m.msgType)} — ${m.msgTypeName}` },
                                { name: 'Payload Length', value: `${msg.payloadLen - 2} bytes` },
                            ],
                            offset: m.frameOffset,
                            length: 2,
                        });

                        // Medius payload (remainder after 2-byte header)
                        const mediusPayLen = msg.payloadLen - 2;
                        if (mediusPayLen > 0) {
                            layers.push({
                                label: `Medius Payload (${mediusPayLen} bytes)`,
                                color: '#909399',
                                fields: [{ name: 'Length', value: `${mediusPayLen} bytes` }],
                                offset: m.frameOffset + 2,
                                length: mediusPayLen,
                            });
                        }
                    } else if (msg.payloadLen > 0) {
                        // Raw SCERT payload (non-APP messages)
                        layers.push({
                            label: `SCERT Payload (${msg.payloadLen} bytes)`,
                            color: '#909399',
                            fields: [{ name: 'Length', value: `${msg.payloadLen} bytes` }],
                            offset: msg.frameOffset + hdrLen,
                            length: msg.payloadLen,
                        });
                    }
                }
                return layers;
            }
        }

        // Fallback: generic TCP data layer
        if (tcpPayLen > 0) {
            layers.push({
                label: `Data (${tcpPayLen} bytes)`,
                color: '#909399',
                fields: [{ name: 'Length', value: `${tcpPayLen} bytes` }],
                offset: tcpPayOff,
                length: tcpPayLen,
            });
        }
        return layers;
    }

    // ── UDP ──
    if (proto === 17 && raw.length >= payloadOff + 8) {
        const srcPort = readU16BE(raw, payloadOff);
        const dstPort = readU16BE(raw, payloadOff + 2);
        const udpLen = readU16BE(raw, payloadOff + 4);
        layers.push({
            label: 'User Datagram Protocol',
            color: '#409eff',
            fields: [
                { name: 'Source Port', value: String(srcPort) },
                { name: 'Destination Port', value: String(dstPort) },
                { name: 'Length', value: String(udpLen) },
            ],
            offset: payloadOff,
            length: 8,
        });
        const udpPayLen = raw.length - payloadOff - 8;
        if (udpPayLen > 0) {
            layers.push({
                label: `Data (${udpPayLen} bytes)`,
                color: '#909399',
                fields: [{ name: 'Length', value: `${udpPayLen} bytes` }],
                offset: payloadOff + 8,
                length: udpPayLen,
            });
        }
        return layers;
    }

    return layers;
}

function etherTypeName(t: number): string {
    switch (t) {
        case 0x0800: return 'IPv4';
        case 0x0806: return 'ARP';
        case 0x86DD: return 'IPv6';
        default: return 'Unknown';
    }
}

function ipProtoName(p: number): string {
    switch (p) {
        case 1: return 'ICMP';
        case 6: return 'TCP';
        case 17: return 'UDP';
        default: return 'Unknown';
    }
}

function icmpTypeName(t: number): string {
    switch (t) {
        case 0: return 'Echo Reply';
        case 3: return 'Destination Unreachable';
        case 8: return 'Echo Request';
        case 11: return 'Time Exceeded';
        default: return 'Other';
    }
}

/** Generate the brief "Info" column summary for a packet. */
function packetInfoSummary(raw: Uint8Array): string {
    if (raw.length < 14) { return 'Too short'; }
    const etherType = readU16BE(raw, 12);
    if (etherType === 0x0806 && raw.length >= 42) {
        const op = readU16BE(raw, 20);
        const senderIP = fmtIP(raw, 28);
        const targetIP = fmtIP(raw, 38);
        return op === 1 ? `ARP Who has ${targetIP}? Tell ${senderIP}` : `ARP ${senderIP} is at ${fmtMac(raw, 22)}`;
    }
    if (etherType !== 0x0800 || raw.length < 34) { return etherTypeName(etherType); }
    const ihl = (raw[14] & 0x0F) * 4;
    const proto = raw[23];
    const srcIP = fmtIP(raw, 26);
    const dstIP = fmtIP(raw, 30);
    const payOff = 14 + ihl;
    if (proto === 1 && raw.length >= payOff + 2) {
        const t = raw[payOff];
        return `${srcIP} → ${dstIP}  ICMP ${icmpTypeName(t)}`;
    }
    if ((proto === 6 || proto === 17) && raw.length >= payOff + 4) {
        const sp = readU16BE(raw, payOff);
        const dp = readU16BE(raw, payOff + 2);
        const pn = proto === 6 ? 'TCP' : 'UDP';
        if (proto === 6 && raw.length >= payOff + 14) {
            const flags = raw[payOff + 13];
            const fl: string[] = [];
            if (flags & 0x02) { fl.push('SYN'); }
            if (flags & 0x10) { fl.push('ACK'); }
            if (flags & 0x01) { fl.push('FIN'); }
            if (flags & 0x04) { fl.push('RST'); }
            if (flags & 0x08) { fl.push('PSH'); }

            // ── SCERT summary ──
            const server = serverFromPorts(sp, dp);
            if (server) {
                const tcpHdrLen = ((raw[payOff + 12] >> 4) & 0xF) * 4;
                const tcpPayOff = payOff + tcpHdrLen;
                if (tcpPayOff < raw.length) {
                    const scertMsgs = parseSCERT(raw.slice(tcpPayOff), tcpPayOff);
                    if (scertMsgs.length > 0) {
                        const first = scertMsgs[0];
                        const port  = MEDIUS_PORTS.has(dp) ? dp : sp;
                        let summary = `[${server}:${port}] ${first.msgTypeName}`;
                        if (first.medius) {
                            summary += ` → ${first.medius.msgTypeName}`;
                        }
                        if (scertMsgs.length > 1) {
                            summary += ` (+${scertMsgs.length - 1} more)`;
                        }
                        return summary;
                    }
                }
                return `${srcIP}:${sp} → ${dstIP}:${dp}  [${server}] TCP [${fl.join(',')}]`;
            }

            return `${srcIP}:${sp} → ${dstIP}:${dp}  ${pn} [${fl.join(',')}]`;
        }
        return `${srcIP}:${sp} → ${dstIP}:${dp}  ${pn}`;
    }
    return `${srcIP} → ${dstIP}  ${ipProtoName(proto)}`;
}

/** Determine the high-level protocol name for the Protocol column. */
function packetProtocol(raw: Uint8Array): string {
    if (raw.length < 14) { return '???'; }
    const et = readU16BE(raw, 12);
    if (et === 0x0806) { return 'ARP'; }
    if (et !== 0x0800 || raw.length < 24) { return etherTypeName(et); }
    const proto = raw[23];
    if (proto !== 6 || raw.length < 34) { return ipProtoName(proto); }
    const ihl    = (raw[14] & 0xF) * 4;
    const payOff = 14 + ihl;
    if (raw.length < payOff + 4) { return 'TCP'; }
    const sp = readU16BE(raw, payOff);
    const dp = readU16BE(raw, payOff + 2);
    if (serverFromPorts(sp, dp)) { return 'SCERT'; }
    return 'TCP';
}

/**
 * Build a structured hex dump with an optional highlighted byte range.
 * Returns HTML matching the disassembler memory webview layout —
 * a header row followed by data rows using flex-based alignment with
 * distinct colours for address, data bytes, and ASCII columns.
 */
function hexDump(raw: Uint8Array, hlOff = -1, hlLen = 0): string {
    // Header row
    let header = '<div class="pv-hex-row">';
    header += '<span class="pv-hex-addr">Offset</span>';
    header += '<span class="pv-hex-bytes">';
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) { header += '<span class="pv-hex-gap"></span>'; }
        header += `<span class="pv-hex-byte">+${i.toString(16).toUpperCase()}</span>`;
    }
    header += '</span>';
    header += '<span class="pv-hex-ascii">0123456789ABCDEF</span>';
    header += '</div>';

    // Data rows
    const rows: string[] = [header];
    for (let i = 0; i < raw.length; i += 16) {
        const addr = toHex(i, 8);
        let bytesHtml = '';
        let asciiHtml = '';
        for (let j = 0; j < 16; j++) {
            if (j && j % 4 === 0) { bytesHtml += '<span class="pv-hex-gap"></span>'; }
            if (i + j < raw.length) {
                const b = raw[i + j];
                const inHl = hlOff >= 0 && (i + j) >= hlOff && (i + j) < hlOff + hlLen;
                const hlClass = inHl ? ' pv-hex-hl' : '';
                bytesHtml += `<span class="pv-hex-byte${hlClass}">${toHex(b)}</span>`;
                const ch = (b >= 0x20 && b < 0x7F) ? String.fromCharCode(b) : '.';
                asciiHtml += `<span class="pv-hex-char${hlClass}">${ch}</span>`;
            } else {
                bytesHtml += '<span class="pv-hex-byte">  </span>';
                asciiHtml += '<span class="pv-hex-char"> </span>';
            }
        }
        rows.push(
            `<div class="pv-hex-row">` +
            `<span class="pv-hex-addr">${addr}</span>` +
            `<span class="pv-hex-bytes">${bytesHtml}</span>` +
            `<span class="pv-hex-ascii">${asciiHtml}</span>` +
            `</div>`
        );
    }
    return rows.join('');
}

// ── Component ────────────────────────────────────────────────────────────────

@Component({
    selector: 'app-packet-viewer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    styles: [`
/* ── Top-level layout ─────────────────────────────────────────── */
.pv-root {
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
    background: #1e1e1e;
    color: #cccccc;
}

/* ── Toolbar ──────────────────────────────────────────────────── */
.pv-toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 12px;
    background: #252526;
    border-bottom: 1px solid #3c3c3c;
    flex-shrink: 0;
    flex-wrap: wrap;
}
.pv-toolbar button {
    padding: 4px 10px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #333;
    color: #ccc;
    font-size: 12px;
    cursor: pointer;
    white-space: nowrap;
}
.pv-toolbar button:hover { background: #444; }
.pv-toolbar button.pv-active { background: #0e639c; border-color: #1177bb; color: #fff; }
.pv-toolbar button:disabled { opacity: 0.5; cursor: default; }
.pv-toolbar select, .pv-toolbar input {
    padding: 3px 6px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #333;
    color: #ccc;
    font-size: 12px;
}
.pv-toolbar input { width: 110px; }
.pv-toolbar .pv-sep { width: 1px; height: 20px; background: #555; }
.pv-toolbar .pv-stats {
    margin-left: auto;
    font-size: 11px;
    color: #888;
    white-space: nowrap;
}

/* ── Three-pane body (packet list / dissection tree / hex dump) ─ */
.pv-body {
    display: flex;
    flex-direction: column;
    flex: 1;
    overflow: hidden;
}
.pv-pane {
    overflow: auto;
    border-bottom: 1px solid #3c3c3c;
}
.pv-pane-packets { flex: 2; min-height: 80px; }
.pv-pane-dissect { flex: 1.2; min-height: 80px; }
.pv-pane-hexdump { flex: 1; min-height: 60px; }

/* ── Packet list table ────────────────────────────────────────── */
.pv-table {
    width: 100%;
    border-collapse: collapse;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    font-size: 12px;
}
.pv-table th {
    position: sticky;
    top: 0;
    background: #2d2d2d;
    border-bottom: 1px solid #555;
    text-align: left;
    padding: 4px 8px;
    font-weight: 600;
    color: #999;
    font-size: 11px;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    z-index: 1;
}
.pv-table td {
    padding: 2px 8px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 500px;
}
.pv-table tr { cursor: pointer; }
.pv-table tr:hover { background: #2a2d2e; }
.pv-table tr.pv-selected { background: #094771; }
.pv-dir-tx { color: #e6a23c; }
.pv-dir-rx { color: #67c23a; }
.pv-server { color: #c792ea; font-weight: 600; }

/* ── Dissection tree ──────────────────────────────────────────── */
.pv-tree { padding: 4px 0; }
.pv-tree-layer {
    padding: 2px 8px;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
}
.pv-tree-layer:hover { background: #2a2d2e; }
.pv-tree-layer.pv-layer-selected { background: #094771; }
.pv-tree-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
}
.pv-tree-chevron {
    width: 12px;
    text-align: center;
    color: #888;
    flex-shrink: 0;
    font-size: 10px;
    user-select: none;
}
.pv-tree-field {
    padding: 1px 8px 1px 40px;
    font-size: 12px;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    color: #aaa;
}
.pv-tree-field-name { color: #9cdcfe; }
.pv-tree-field-value { color: #ce9178; }

/* ── Hex dump (disassembler-style) ────────────────────────────── */
.pv-hex {
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    font-size: 12px;
    padding: 2px 4px 4px;
    background: #2d2d30;
}
.pv-hex-row {
    display: flex;
    gap: 8px;
    padding: 2px 6px;
    line-height: 1.4;
}
.pv-hex-row:first-child {
    background: #2d2d30;
    border-bottom: 2px solid #3e3e42;
}
.pv-hex-addr {
    color: #858585;
    width: 10ch;
    flex-shrink: 0;
}
.pv-hex-bytes {
    display: flex;
    gap: 0;
    flex: 1;
}
.pv-hex-byte {
    color: #d4d4d4;
    width: 2ch;
    text-align: center;
    margin: 0 1px;
}
.pv-hex-byte.pv-hex-hl {
    background: #264f78;
    color: #fff;
}
.pv-hex-gap {
    width: 8px;
    flex-shrink: 0;
}
.pv-hex-ascii {
    color: #608b4e;
    width: 16ch;
    border-left: 1px solid #3e3e42;
    padding-left: 8px;
    display: flex;
    flex-shrink: 0;
}
.pv-hex-char {
    width: 1ch;
    display: inline-block;
    text-align: center;
}
.pv-hex-char.pv-hex-hl {
    background: #264f78;
    color: #fff;
}

/* ── Status bar ───────────────────────────────────────────────── */
.pv-statusbar {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 3px 12px;
    background: #252526;
    border-top: 1px solid #3c3c3c;
    font-size: 11px;
    color: #888;
    flex-shrink: 0;
}
.pv-statusbar .pv-status-ok { color: #67c23a; }
.pv-statusbar .pv-status-err { color: #f56c6c; }

/* ── Empty state ──────────────────────────────────────────────── */
.pv-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: #666;
    gap: 8px;
}
.pv-empty-icon { font-size: 48px; opacity: 0.4; }

/* ── Display filter bar ───────────────────────────────────────── */
.pv-filterbar {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 12px;
    background: #1e1e1e;
    border-bottom: 1px solid #3c3c3c;
    flex-shrink: 0;
}
.pv-filterbar-label {
    font-size: 11px;
    color: #888;
    white-space: nowrap;
}
.pv-filterbar-input {
    flex: 1;
    padding: 3px 6px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #2d2d30;
    color: #ccc;
    font-size: 12px;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    min-width: 0;
}
.pv-filterbar-input:focus { outline: 1px solid #0e639c; border-color: #0e639c; }
.pv-filterbar-clear {
    padding: 2px 6px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #333;
    color: #ccc;
    font-size: 11px;
    cursor: pointer;
    flex-shrink: 0;
}
.pv-filterbar-clear:hover { background: #444; }
.pv-filterbar-count {
    font-size: 11px;
    color: #888;
    white-space: nowrap;
    flex-shrink: 0;
}
`],
    template: `
<div class="pv-root">
    <!-- ── Toolbar ─────────────────────────────────────────────────── -->
    <div class="pv-toolbar">
        @if (!capturing()) {
            <button (click)="onStartCapture()">▶ Start Capture</button>
        } @else {
            <button class="pv-active" (click)="onStopCapture()">⏹ Stop Capture</button>
        }
        <button (click)="onPollOnce()" [disabled]="!capturing()">⟳ Poll</button>
        <button (click)="onClear()">✕ Clear</button>
        <div class="pv-sep"></div>
        <button (click)="onOpenPcap()" title="Open a .pcap file">📂 Open</button>
        <button (click)="onSavePcap()" [disabled]="packets().length === 0" title="Save capture as .pcap">💾 Save</button>
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Direction</label>
        <select [value]="captureDirection()" (change)="captureDirection.set(+$any($event).target.value)">
            <option value="0">Both</option>
            <option value="1">TX only</option>
            <option value="2">RX only</option>
        </select>
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Capture Filter</label>
        <select [value]="filterType()" (change)="filterType.set(+$any($event).target.value)">
            <option value="0">None</option>
            <option value="1">Protocol</option>
            <option value="2">Port</option>
            <option value="3">IP</option>
        </select>
        @if (filterType() === 1) {
            <select [value]="filterProtocol()" (change)="filterProtocol.set(+$any($event).target.value)">
                <option value="0">All</option>
                <option value="1">TCP</option>
                <option value="2">UDP</option>
                <option value="3">ICMP</option>
                <option value="4">ARP</option>
                <option value="5">DNS</option>
                <option value="6">DHCP</option>
            </select>
        }
        @if (filterType() === 2) {
            <input type="number" placeholder="Port" [value]="filterPort()" (input)="filterPort.set(+$any($event).target.value)" min="0" max="65535" />
        }
        @if (filterType() === 3) {
            <input type="text" placeholder="IP address" [value]="filterIP()" (input)="filterIP.set($any($event).target.value)" />
        }
        @if (filterType() !== 0) {
            <button (click)="onApplyFilter()">Apply</button>
        }
        <span class="pv-stats">
            @if (stats()) {
                TX: {{ stats()!.txPackets }} pkts / {{ formatBytes(stats()!.txBytes) }}
                &nbsp;|&nbsp;
                RX: {{ stats()!.rxPackets }} pkts / {{ formatBytes(stats()!.rxBytes) }}
                &nbsp;|&nbsp;
                Queue: {{ stats()!.captureQueueSize }}
                @if (stats()!.droppedCount > 0) {
                    &nbsp;| <span style="color:#f56c6c">Dropped: {{ stats()!.droppedCount }}</span>
                }
            }
        </span>
    </div>

    <!-- ── Display filter bar ───────────────────────────────────────── -->
    <div class="pv-filterbar">
        <label class="pv-filterbar-label">Display Filter:</label>
        <input class="pv-filterbar-input" type="text" placeholder="Search packets… (src, dst, proto, info)"
               [value]="displayFilter()"
               (input)="displayFilter.set($any($event).target.value)" />
        @if (displayFilter()) {
            <button class="pv-filterbar-clear" (click)="displayFilter.set('')" title="Clear filter">✕</button>
            <span class="pv-filterbar-count">{{ filteredPackets().length }} / {{ packets().length }}</span>
        }
    </div>

    <!-- ── Body: three panes ───────────────────────────────────────── -->
    <div class="pv-body">
        <!-- Packet list -->
        <div class="pv-pane pv-pane-packets">
            @if (packets().length === 0) {
                <div class="pv-empty">
                    <div class="pv-empty-icon">📡</div>
                    <div>No packets captured</div>
                    <div style="font-size:11px">Click <b>Start Capture</b> to begin or <b>📂 Open</b> to load a .pcap file</div>
                </div>
            } @else if (filteredPackets().length === 0) {
                <div class="pv-empty">
                    <div class="pv-empty-icon">🔍</div>
                    <div>No packets match the display filter</div>
                </div>
            } @else {
                <table class="pv-table">
                    <thead>
                        <tr>
                            <th style="width:50px">No.</th>
                            <th style="width:80px">Time</th>
                            <th style="width:40px">Dir</th>
                            <th style="width:60px">Server</th>
                            <th style="width:130px">Source</th>
                            <th style="width:130px">Destination</th>
                            <th style="width:55px">Proto</th>
                            <th style="width:45px">Len</th>
                            <th>Info</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (pkt of filteredPackets(); track pkt.index; let i = $index) {
                            <tr [class.pv-selected]="selectedIndex() === pkt.index"
                                (click)="selectPacket(pkt.index)">
                                <td>{{ pkt.index + 1 }}</td>
                                <td>{{ pkt.timeStr }}</td>
                                <td [class]="pkt.dirClass">{{ pkt.dirLabel }}</td>
                                <td class="pv-server">{{ pkt.server }}</td>
                                <td>{{ pkt.src }}</td>
                                <td>{{ pkt.dst }}</td>
                                <td>{{ pkt.proto }}</td>
                                <td>{{ pkt.length }}</td>
                                <td>{{ pkt.info }}</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>

        <!-- Dissection tree -->
        <div class="pv-pane pv-pane-dissect">
            @if (selectedLayers().length > 0) {
                <div class="pv-tree">
                    @for (layer of selectedLayers(); track $index; let li = $index) {
                        <div class="pv-tree-layer" [class.pv-layer-selected]="selectedLayerIdx() === li"
                             (click)="selectLayer(li)">
                            <span class="pv-tree-chevron">{{ expandedLayers().has(li) ? '▾' : '▸' }}</span>
                            <span class="pv-tree-dot" [style.background]="layer.color"></span>
                            <span>{{ layer.label }}</span>
                        </div>
                        @if (expandedLayers().has(li)) {
                            @for (f of layer.fields; track $index) {
                                <div class="pv-tree-field">
                                    <span class="pv-tree-field-name">{{ f.name }}: </span>
                                    <span class="pv-tree-field-value">{{ f.value }}</span>
                                </div>
                            }
                        }
                    }
                </div>
            }
        </div>

        <!-- Hex dump -->
        <div class="pv-pane pv-pane-hexdump">
            @if (hexDumpHtml()) {
                <div class="pv-hex" [innerHTML]="hexDumpHtml()"></div>
            }
        </div>
    </div>

    <!-- ── Status bar ──────────────────────────────────────────────── -->
    <div class="pv-statusbar">
        @if (adapterStatus()) {
            <span [class]="adapterStatus()!.enabled ? 'pv-status-ok' : 'pv-status-err'">
                {{ adapterStatus()!.enabled ? '● Connected' : '○ Disconnected' }}
            </span>
            @if (adapterStatus()!.enabled) {
                <span>MAC: {{ adapterStatus()!.ps2Mac }}</span>
                <span>IP: {{ adapterStatus()!.ps2IP }}</span>
            }
        } @else {
            <span>Adapter status unknown — click Start Capture to query</span>
        }
        <span style="margin-left:auto">
            @if (displayFilter() && filteredPackets().length !== packets().length) {
                {{ filteredPackets().length }} displayed / {{ packets().length }} packets
            } @else {
                {{ packets().length }} packets
            }
        </span>
        @if (statusMessage()) {
            <span>{{ statusMessage() }}</span>
        }
    </div>
</div>
`,
})
export class PacketViewerComponent {
    // ── State ────────────────────────────────────────────────────────────────
    readonly capturing = signal(false);
    readonly captureDirection = signal(0);
    readonly filterType = signal(0);
    readonly filterProtocol = signal(0);
    readonly filterPort = signal(0);
    readonly filterIP = signal('');
    /** Client-side display filter — case-insensitive substring search across visible columns. */
    readonly displayFilter = signal('');
    readonly adapterStatus = signal<NetStatus | null>(null);
    readonly stats = signal<NetStats | null>(null);
    readonly statusMessage = signal('');
    readonly selectedIndex = signal(-1);
    readonly selectedLayerIdx = signal(-1);
    readonly expandedLayers = signal<Set<number>>(new Set());

    /** Internal raw packet storage. */
    private _rawPackets: Array<{ pkt: CapturedPacket; raw: Uint8Array }> = [];
    /** First timestamp for relative time display. */
    private _baseTimestamp = 0;
    /** Monotonic packet counter. */
    private _packetCounter = 0;

    /** Polling interval handle. */
    private _pollTimer: ReturnType<typeof setInterval> | undefined;
    /** Stats polling interval handle. */
    private _statsTimer: ReturnType<typeof setInterval> | undefined;

    // Public callback — set by the page component to emit actions.
    onAction: ((action: PacketViewerAction) => void) | undefined;

    /** True while a recvCapture request is in-flight — prevents overlapping polls. */
    private _recvInFlight = false;

    // ── Processed packet list for display ────────────────────────────────────
    readonly packets = signal<Array<{
        index: number;
        timeStr: string;
        dirLabel: string;
        dirClass: string;
        server: string;
        src: string;
        dst: string;
        proto: string;
        length: number;
        info: string;
    }>>([]);

    /** Packets filtered by the display filter text — case-insensitive substring match. */
    readonly filteredPackets = computed(() => {
        const all = this.packets();
        const f = this.displayFilter().trim().toLowerCase();
        if (!f) { return all; }
        return all.filter(p =>
            p.dirLabel.toLowerCase().includes(f) ||
            p.server.toLowerCase().includes(f) ||
            p.src.toLowerCase().includes(f) ||
            p.dst.toLowerCase().includes(f) ||
            p.proto.toLowerCase().includes(f) ||
            p.info.toLowerCase().includes(f) ||
            String(p.length).includes(f) ||
            p.timeStr.includes(f)
        );
    });

    // ── Dissection of selected packet ────────────────────────────────────────
    readonly selectedLayers = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return []; }
        return dissectFrame(this._rawPackets[idx].raw);
    });

    readonly hexDumpHtml = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return ''; }
        const raw = this._rawPackets[idx].raw;
        const li = this.selectedLayerIdx();
        const layers = this.selectedLayers();
        if (li >= 0 && li < layers.length) {
            return hexDump(raw, layers[li].offset, layers[li].length);
        }
        return hexDump(raw);
    });

    // ── Public API (called by page component) ────────────────────────────────

    setStatus(status: NetStatus): void {
        this.adapterStatus.set(status);
    }

    setStats(s: NetStats): void {
        this.stats.set(s);
    }

    addCapturedPackets(packets: CapturedPacket[]): void {
        for (const pkt of packets) {
            const raw = b64ToBytes(pkt.data);
            if (this._baseTimestamp === 0) { this._baseTimestamp = pkt.timestampUs; }
            this._rawPackets.push({ pkt, raw });
        }
        this._rebuildPacketList();
    }

    showStatus(msg: string): void {
        this.statusMessage.set(msg);
    }

    startPolling(): void {
        this.stopPolling();
        this._pollTimer = setInterval(() => {
            // Skip this tick if the previous recvCapture hasn't returned yet.
            if (this._recvInFlight) { return; }
            this._recvInFlight = true;
            this.onAction?.({ type: 'recvCapture', maxPackets: 100 });
        }, 500);
        this._statsTimer = setInterval(() => {
            this.onAction?.({ type: 'getStats' });
        }, 2000);
    }

    /** Called by the page component after a recvCapture response (success or error). */
    recvCaptureComplete(): void {
        this._recvInFlight = false;
    }

    stopPolling(): void {
        if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = undefined; }
        if (this._statsTimer) { clearInterval(this._statsTimer); this._statsTimer = undefined; }
    }

    // ── UI event handlers ────────────────────────────────────────────────────

    onStartCapture(): void {
        this.statusMessage.set('Starting capture…');
        this.onAction?.({ type: 'startCapture', direction: this.captureDirection(), maxQueue: 1000 });
    }

    /** Called by the page component when the provider confirms capture started. */
    captureStarted(): void {
        this.capturing.set(true);
        this.statusMessage.set('');
        this.onAction?.({ type: 'getStatus' });
        this.startPolling();
    }

    /** Called by the page component when the provider reports capture failed to start. */
    captureStartFailed(msg: string): void {
        this.capturing.set(false);
        this.statusMessage.set(msg);
    }

    onStopCapture(): void {
        this.capturing.set(false);
        this.onAction?.({ type: 'stopCapture' });
        this.stopPolling();
    }

    onPollOnce(): void {
        this.onAction?.({ type: 'recvCapture', maxPackets: 100 });
    }

    onClear(): void {
        this._rawPackets = [];
        this._baseTimestamp = 0;
        this._packetCounter = 0;
        this.selectedIndex.set(-1);
        this.selectedLayerIdx.set(-1);
        this.expandedLayers.set(new Set());
        this.packets.set([]);
        this.onAction?.({ type: 'clearPackets' });
    }

    onApplyFilter(): void {
        this.onAction?.({
            type: 'setFilter',
            filterType: this.filterType(),
            protocol: this.filterProtocol(),
            port: this.filterPort(),
            ip: this.filterIP(),
        });
    }

    onOpenPcap(): void {
        this.onAction?.({ type: 'openPcap' });
    }

    onSavePcap(): void {
        if (this._rawPackets.length === 0) {
            return;
        }
        const packets: PcapPacketEntry[] = this._rawPackets.map(e => ({
            direction:   e.pkt.direction,
            timestampUs: e.pkt.timestampUs,
            data:        e.pkt.data,
        }));
        this.onAction?.({ type: 'savePcap', packets });
    }

    selectPacket(idx: number): void {
        this.selectedIndex.set(idx);
        this.selectedLayerIdx.set(-1);
        // Auto-expand all layers on selection
        const layers = this.selectedLayers();
        this.expandedLayers.set(new Set(layers.map((_, i) => i)));
    }

    selectLayer(li: number): void {
        this.selectedLayerIdx.set(li);
        // Toggle expansion
        const ex = new Set(this.expandedLayers());
        if (ex.has(li)) { ex.delete(li); } else { ex.add(li); }
        this.expandedLayers.set(ex);
    }

    formatBytes(n: number): string {
        if (n < 1024) { return `${n} B`; }
        if (n < 1048576) { return `${(n / 1024).toFixed(1)} KB`; }
        return `${(n / 1048576).toFixed(1)} MB`;
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private _rebuildPacketList(): void {
        const list = this._rawPackets.map((entry, i) => {
            const { pkt, raw } = entry;
            const relTime = ((pkt.timestampUs - this._baseTimestamp) / 1_000_000).toFixed(6);
            const src = raw.length >= 26 && readU16BE(raw, 12) === 0x0800 ? fmtIP(raw, 26) : fmtMac(raw, 6);
            const dst = raw.length >= 30 && readU16BE(raw, 12) === 0x0800 ? fmtIP(raw, 30) : fmtMac(raw, 0);

            // Determine server name from TCP port numbers (if IPv4 TCP)
            let server = '';
            if (readU16BE(raw, 12) === 0x0800 && raw.length > 23 && raw[23] === 6) {
                const ihl    = (raw[14] & 0xF) * 4;
                const payOff = 14 + ihl;
                if (raw.length >= payOff + 4) {
                    server = serverFromPorts(readU16BE(raw, payOff), readU16BE(raw, payOff + 2));
                }
            }

            return {
                index: i,
                timeStr: relTime,
                dirLabel: pkt.directionName,
                dirClass: pkt.direction === 0 ? 'pv-dir-tx' : 'pv-dir-rx',
                server,
                src,
                dst,
                proto: packetProtocol(raw),
                length: pkt.length,
                info: packetInfoSummary(raw),
            };
        });
        this.packets.set(list);
    }
}
