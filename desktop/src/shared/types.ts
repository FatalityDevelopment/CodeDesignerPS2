/** Shared domain types used by both main and renderer processes. */

export interface UserSession {
  provider: 'github';
  username: string;
  displayName?: string;
  avatarUrl?: string;
  accessToken?: string;
}

export interface DocFile {
  /** Path relative to the docs root, using forward slashes. */
  relativePath: string;
  /** File name without extension. */
  title: string;
}

export interface AppSettings {
  docsFolder?: string;
  theme: 'dark' | 'light';
  ai: AiSettings;
}

export interface AiSettings {
  provider: 'openai' | 'anthropic' | 'github-models' | 'ollama';
  model: string;
  apiKey?: string;
  endpoint?: string;
  temperature: number;
  maxTokens: number;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: number;
}

export interface DebuggerState {
  connected: boolean;
  running: boolean;
  pc?: number;
  registers: Record<string, number>;
  callStack: { address: number; symbol?: string }[];
}

export interface PacketRecord {
  id: string;
  timestamp: number;
  direction: 'tx' | 'rx';
  /** Source/dest as host:port if known. */
  source: string;
  destination: string;
  protocol: 'tcp' | 'udp';
  length: number;
  /** Hex string of the raw payload. */
  payloadHex: string;
  /** Optional decoded summary (e.g. SCERT/Medius). */
  summary?: string;
}

export interface LabelRecord {
  /** Hex address as decimal number. */
  address: number;
  name: string;
  comment?: string;
  /** Module/file the label belongs to. */
  module?: string;
}

export const DEFAULT_AI_SETTINGS: AiSettings = {
  provider: 'openai',
  model: 'gpt-4o-mini',
  temperature: 0.2,
  maxTokens: 2048
};

export const DEFAULT_APP_SETTINGS: AppSettings = {
  theme: 'dark',
  ai: DEFAULT_AI_SETTINGS
};
