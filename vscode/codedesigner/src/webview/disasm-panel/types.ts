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
