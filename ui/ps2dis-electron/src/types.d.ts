declare const __dirname: string;
declare const process: { env: Record<string, string | undefined>; platform: string };

declare module 'node:path' {
  export function join(...parts: string[]): string;
  export function resolve(...parts: string[]): string;
}

declare module 'electron' {
  export const app: {
    whenReady(): Promise<void>;
    on(event: 'window-all-closed' | 'activate', listener: () => void): void;
    quit(): void;
  };
  export class BrowserWindow {
    constructor(options: Record<string, unknown>);
    static getAllWindows(): BrowserWindow[];
    loadURL(url: string): Promise<void>;
    loadFile(path: string): Promise<void>;
    setMenuBarVisibility(visible: boolean): void;
  }
  export const ipcMain: {
    on(channel: string, listener: (event: { sender: { send(channel: string, message: unknown): void } }, message: unknown) => void): void;
  };
  export const contextBridge: { exposeInMainWorld(key: string, api: unknown): void };
  export const ipcRenderer: {
    send(channel: string, message: unknown): void;
    on(channel: string, listener: (event: unknown, message: unknown) => void): void;
    off(channel: string, listener: (event: unknown, message: unknown) => void): void;
  };
  export namespace Electron { export type IpcRendererEvent = unknown; }
}
