/**
 * Centralized IPC channel names used between main and renderer.
 * Keep this list small and grouped by feature.
 */
export const IpcChannels = {
  // App
  AppGetVersion: 'app:get-version',
  AppQuit: 'app:quit',

  // Auth (GitHub OAuth)
  AuthGithubBegin: 'auth:github-begin',
  AuthGithubComplete: 'auth:github-complete',
  AuthLogout: 'auth:logout',
  AuthGetSession: 'auth:get-session',

  // Settings (persisted JSON store)
  SettingsGet: 'settings:get',
  SettingsSet: 'settings:set',
  SettingsAll: 'settings:all',

  // Docs (markdown folder)
  DocsList: 'docs:list',
  DocsRead: 'docs:read',
  DocsPickFolder: 'docs:pick-folder',

  // Debugger (stub — wires to the Bridge socket later)
  DebuggerConnect: 'debugger:connect',
  DebuggerStep: 'debugger:step',
  DebuggerContinue: 'debugger:continue',
  DebuggerPause: 'debugger:pause',
  DebuggerStateUpdate: 'debugger:state-update',

  // Packets
  PacketsOpenFile: 'packets:open-file',
  PacketsClear: 'packets:clear',
  PacketsIncoming: 'packets:incoming',

  // AI
  AiSettingsGet: 'ai:settings-get',
  AiSettingsSet: 'ai:settings-set',
  AiChatSend: 'ai:chat-send',
  AiChatStreamChunk: 'ai:chat-stream-chunk'
} as const;

export type IpcChannel = (typeof IpcChannels)[keyof typeof IpcChannels];
