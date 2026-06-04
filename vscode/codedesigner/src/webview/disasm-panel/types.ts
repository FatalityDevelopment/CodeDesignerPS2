/** A single cheat entry from the CheatCodeArchive. */
export interface ArchiveCheat {
  name: string;
  author?: string;
  notes?: string;
  codes: string[];
  enabled: boolean;
}

export interface EmuOption {
  label: string;
  value: string;
}

export interface SnapshotEntry {
  label: string;
  timestamp: string;
  path: string;
  name: string;
  date: string;
  isoName: string;
  /** PCSX2 disc serial parsed from the .p2s file name (e.g. "SCUS-97275"). */
  serial?: string;
  /** PCSX2 disc CRC parsed from the .p2s file name (8 hex chars). */
  crc?: string;
  /** PCSX2 slot token parsed from the .p2s file name ("00".."09", "resume", "backup"). */
  slot?: string;
  /** Byte size of the .p2s archive on disk. */
  sizeBytes?: number;
  /** File modification time in ms since epoch — used for stable date sorting. */
  mtimeMs?: number;
}

export interface DiscElfEntry {
  Path: string;
  Name: string;
  Size: number;
  IsBootElf: boolean;
}

/** One entry in the PC navigation history (server-recorded). */
export interface NavHistoryEntry {
  address: string;
  label?: string;
  /** 'user' for manual navigations, 'mcp' for AI-driven ones. */
  source: string;
  timestamp: string;
}

/** A label from the active label set, slimmed for the Recent Labels list. */
export interface RecentLabelEntry {
  name: string;
  type: string;
  address: string;
}

/** Reachability of a single Multiplayer server port. */
export interface ServerStatus {
  name: string;
  port: number;
  online: boolean;
}

/** Multiplayer server reachability + packet-capture state. */
export interface NetworkStatus {
  servers: ServerStatus[];
  capturing: boolean;
}

/** An AI agent profile, slimmed for the Agents list. */
export interface AgentSummary {
  id: number;
  name: string;
  description: string;
  /** True when this agent is the team lead (only one agent at a time). */
  isLead: boolean;
  /** Webview URI of the agent's profile picture, or '' when no match. */
  imageUri: string;
}

/** An orchestration plan, slimmed for the Orchestration list. */
export interface OrchestrationSummary {
  id: number;
  name: string;
  description: string;
  /** True when this plan is the active one (only one plan at a time). */
  isActive: boolean;
}
