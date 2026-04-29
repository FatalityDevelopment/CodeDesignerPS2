import { contextBridge, ipcRenderer } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';
import type { AiSettings, AppSettings, ChatMessage, DocFile, UserSession } from '@shared/types';

/**
 * The single, typed surface available to the renderer as `window.api`.
 * Keep it small and grouped by feature; the renderer should never call
 * `ipcRenderer` directly.
 */
const api = {
  app: {
    getVersion: (): Promise<string> => ipcRenderer.invoke(IpcChannels.AppGetVersion),
    quit: (): Promise<void> => ipcRenderer.invoke(IpcChannels.AppQuit)
  },

  auth: {
    githubBegin: (clientId?: string) => ipcRenderer.invoke(IpcChannels.AuthGithubBegin, clientId),
    githubComplete: (code: string): Promise<UserSession> =>
      ipcRenderer.invoke(IpcChannels.AuthGithubComplete, code),
    getSession: (): Promise<UserSession | null> => ipcRenderer.invoke(IpcChannels.AuthGetSession),
    logout: (): Promise<void> => ipcRenderer.invoke(IpcChannels.AuthLogout)
  },

  settings: {
    all: (): Promise<AppSettings> => ipcRenderer.invoke(IpcChannels.SettingsAll),
    get: (key: string) => ipcRenderer.invoke(IpcChannels.SettingsGet, key),
    set: (key: string, value: unknown) => ipcRenderer.invoke(IpcChannels.SettingsSet, key, value)
  },

  docs: {
    list: (folder: string): Promise<DocFile[]> => ipcRenderer.invoke(IpcChannels.DocsList, folder),
    read: (folder: string, relativePath: string): Promise<string> =>
      ipcRenderer.invoke(IpcChannels.DocsRead, folder, relativePath),
    pickFolder: (): Promise<string | null> => ipcRenderer.invoke(IpcChannels.DocsPickFolder)
  },

  debugger: {
    connect: (host: string, port: number) =>
      ipcRenderer.invoke(IpcChannels.DebuggerConnect, { host, port }),
    step: () => ipcRenderer.invoke(IpcChannels.DebuggerStep),
    cont: () => ipcRenderer.invoke(IpcChannels.DebuggerContinue),
    pause: () => ipcRenderer.invoke(IpcChannels.DebuggerPause),
    onStateUpdate: (cb: (state: unknown) => void) => {
      const listener = (_e: unknown, payload: unknown) => cb(payload);
      ipcRenderer.on(IpcChannels.DebuggerStateUpdate, listener);
      return () => ipcRenderer.removeListener(IpcChannels.DebuggerStateUpdate, listener);
    }
  },

  ai: {
    getSettings: (): Promise<AiSettings> => ipcRenderer.invoke(IpcChannels.AiSettingsGet),
    setSettings: (settings: AiSettings): Promise<AiSettings> =>
      ipcRenderer.invoke(IpcChannels.AiSettingsSet, settings),
    sendChat: (messages: ChatMessage[]): Promise<ChatMessage> =>
      ipcRenderer.invoke(IpcChannels.AiChatSend, messages)
  }
};

contextBridge.exposeInMainWorld('api', api);

export type DesktopApi = typeof api;
