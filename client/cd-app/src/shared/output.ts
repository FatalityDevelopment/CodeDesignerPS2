// Named log channels for the Output panel, the app's stand-in for VS Code output channels.

import { signal } from './signals';

export interface OutputLine { time: string; text: string; error: boolean }

const MAX_LINES = 2000;

export const outputChannels = signal<Record<string, OutputLine[]>>({ CodeDesigner: [] });

export const output = {
  append(channel: string, text: string, error = false): void {
    const time = new Date().toLocaleTimeString();
    outputChannels.update(all => {
      const lines = [...(all[channel] ?? []), ...text.split(/\r?\n/).map(t => ({ time, text: t, error }))];
      return { ...all, [channel]: lines.slice(-MAX_LINES) };
    });
  },

  clear(channel: string): void {
    outputChannels.update(all => ({ ...all, [channel]: [] }));
  },
};
