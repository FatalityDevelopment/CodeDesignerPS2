import { create } from 'zustand';
import { DEFAULT_APP_SETTINGS, AppSettings } from '@shared/types';

interface SettingsState {
  settings: AppSettings;
  hydrate: () => Promise<void>;
  setDocsFolder: (folder: string | undefined) => Promise<void>;
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  settings: DEFAULT_APP_SETTINGS,

  hydrate: async () => {
    const settings = await window.api.settings.all();
    set({ settings });
  },

  setDocsFolder: async (folder) => {
    const next: AppSettings = { ...get().settings, docsFolder: folder };
    await window.api.settings.set('docsFolder', folder);
    set({ settings: next });
  }
}));
