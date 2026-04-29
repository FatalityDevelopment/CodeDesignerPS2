import { ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';
import { settingsStore } from '../services/settingsStore';

export function registerSettingsHandlers(): void {
  ipcMain.handle(IpcChannels.SettingsAll, () => settingsStore.all());
  ipcMain.handle(IpcChannels.SettingsGet, (_e, key: string) => settingsStore.get(key));
  ipcMain.handle(IpcChannels.SettingsSet, (_e, key: string, value: unknown) =>
    settingsStore.set(key, value)
  );
}
