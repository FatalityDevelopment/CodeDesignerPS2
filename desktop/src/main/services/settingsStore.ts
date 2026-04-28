import { app } from 'electron';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs';
import { dirname, join } from 'path';
import { DEFAULT_APP_SETTINGS, AppSettings } from '@shared/types';

/**
 * Tiny JSON-on-disk settings store. Synchronous reads/writes are acceptable
 * here because the file is small and only touched on user actions.
 */
class SettingsStore {
  private data: AppSettings;
  private readonly filePath: string;

  constructor() {
    this.filePath = join(app.getPath('userData'), 'settings.json');
    this.data = this.load();
  }

  private load(): AppSettings {
    try {
      if (existsSync(this.filePath)) {
        const raw = readFileSync(this.filePath, 'utf8');
        return { ...DEFAULT_APP_SETTINGS, ...JSON.parse(raw) };
      }
    } catch {
      /* fall through to defaults */
    }
    return { ...DEFAULT_APP_SETTINGS };
  }

  private save(): void {
    try {
      mkdirSync(dirname(this.filePath), { recursive: true });
      writeFileSync(this.filePath, JSON.stringify(this.data, null, 2), 'utf8');
    } catch (err) {
      console.error('settingsStore: failed to persist', err);
    }
  }

  all(): AppSettings {
    return this.data;
  }

  get<K extends keyof AppSettings>(key: K | string): AppSettings[K] | unknown {
    return (this.data as Record<string, unknown>)[key as string];
  }

  set(key: string, value: unknown): void {
    (this.data as Record<string, unknown>)[key] = value;
    this.save();
  }
}

export const settingsStore = new SettingsStore();
