// What the VS Code extension does for its pages, done in the browser: prompts, the file browser, downloads for exports,
// notifications, and switching between pages. The Bridge reads and writes files by path.

import { signal } from './signals';
import { output } from './output';

export type PageId = 'emulator' | 'disassembler';

export interface DialogField {
  key: string;
  label: string;
  value: string;
  // Submit is refused until the value matches (hint explains why).
  pattern?: RegExp;
  hint?: string;
  // Left empty is allowed (the caller picks a default).
  optional?: boolean;
}

export interface DialogRequest {
  title: string;
  submitLabel: string;
  fields: DialogField[];
  resolve: (values: Record<string, string> | null) => void;
}

// The dialog on screen (rendered by HostDialog in the app shell).
export const activeDialog = signal<DialogRequest | null>(null);

export interface FileFilter { label: string; extensions: string[] }

export interface BrowseOptions {
  mode: 'open' | 'save' | 'folder';
  title: string;
  submitLabel?: string;
  // File types offered in the filter list (the first is selected); "All files" is always added.
  filters?: FileFilter[];
  // Folder (or file) to start in; otherwise the last folder used for the same key.
  start?: string;
  // Save mode: the suggested file name.
  fileName?: string;
  // Open mode: a folder can be chosen too.
  allowFolder?: boolean;
  // Groups remembered folders (ISOs, dumps, exports …).
  key?: string;
}

export interface BrowseRequest extends BrowseOptions { resolve: (path: string | null) => void }

// The file browser on screen (rendered by FileBrowser in the app shell).
export const activeBrowse = signal<BrowseRequest | null>(null);

// A prompt or the file browser is up: pages leave the keyboard alone.
export function modalOpen(): boolean {
  return !!activeDialog() || !!activeBrowse();
}

const LAST_FOLDER = 'cdapp.lastFolder.';

export function rememberedFolder(key: string): string {
  try { return localStorage.getItem(LAST_FOLDER + key) ?? ''; } catch { return ''; }
}

export function rememberFolder(key: string, folder: string): void {
  try { localStorage.setItem(LAST_FOLDER + key, folder); } catch { /* storage unavailable */ }
}

const dirOf = (p: string) => p.replace(/[\\/][^\\/]*$/, '');

// The page in front.
export const activePage = signal<PageId>('emulator');

// Trims a typed path and drops the quotes Explorer's "Copy as path" adds.
export function cleanPath(value: string | undefined | null): string {
  return (value ?? '').trim().replace(/^"(.*)"$/, '$1');
}

export type ToastLevel = 'info' | 'warning' | 'error';

export interface Toast { id: number; level: ToastLevel; text: string }

const TOAST_MS = 6000;

// The notifications on screen (rendered by Toasts in the app shell).
export const toasts = signal<Toast[]>([]);
let toastSeq = 0;

export const host = {
  prompt(title: string, fields: DialogField[], submitLabel = 'OK'): Promise<Record<string, string> | null> {
    return new Promise(resolve => {
      activeDialog.set({
        title,
        submitLabel,
        fields,
        resolve: values => { activeDialog.set(null); resolve(values); },
      });
    });
  },

  // The file browser: a file to open, a folder, or where to save. Resolves to the chosen path (null on cancel).
  browse(options: BrowseOptions): Promise<string | null> {
    return new Promise(resolve => {
      activeBrowse.set({
        ...options,
        resolve: path => {
          activeBrowse.set(null);
          if (path) { rememberFolder(options.key ?? options.mode, options.mode === 'folder' ? path : dirOf(path)); }
          resolve(path);
        },
      });
    });
  },

  async askText(title: string, value: string, pattern?: string, hint?: string): Promise<string | null> {
    const v = await host.prompt(title, [{ key: 'text', label: 'Name', value, pattern: pattern ? new RegExp(pattern) : undefined, hint }]);
    return v ? v['text'] : null;
  },

  // Exports: a browser download.
  saveFile(suggested: string, content: { text?: string; base64?: string }, mime = 'application/octet-stream'): string {
    const name = suggested.split(/[\\/]/).pop() || 'export';
    const blob = content.text !== undefined
      ? new Blob([content.text], { type: mime })
      : new Blob([Uint8Array.from(atob(content.base64 ?? ''), c => c.charCodeAt(0))], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
    return name;
  },

  // A notification in the corner; errors and warnings also land in the Output panel.
  notify(text: string, level: ToastLevel = 'info'): void {
    const id = ++toastSeq;
    toasts.update(list => [...list, { id, level, text }]);
    setTimeout(() => host.dismissToast(id), TOAST_MS);
    if (level !== 'info') { output.append('CodeDesigner', text, level === 'error'); }
  },

  dismissToast(id: number): void {
    toasts.update(list => list.filter(t => t.id !== id));
  },

  log(text: string): void {
    output.append('CodeDesigner', text);
  },
};
