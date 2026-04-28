import { create } from 'zustand';
import type { DebuggerState } from '@shared/types';

interface DebuggerStoreState extends DebuggerState {
  host: string;
  port: number;
  setEndpoint: (host: string, port: number) => void;
  connect: () => Promise<void>;
  step: () => Promise<void>;
  cont: () => Promise<void>;
  pause: () => Promise<void>;
}

export const useDebuggerStore = create<DebuggerStoreState>((set, get) => ({
  connected: false,
  running: false,
  pc: undefined,
  registers: {},
  callStack: [],
  host: '127.0.0.1',
  port: 9999,

  setEndpoint: (host, port) => set({ host, port }),

  connect: async () => {
    const { host, port } = get();
    const result = (await window.api.debugger.connect(host, port)) as { connected: boolean };
    set({ connected: !!result.connected });
  },

  step: async () => {
    await window.api.debugger.step();
  },
  cont: async () => {
    await window.api.debugger.cont();
    set({ running: true });
  },
  pause: async () => {
    await window.api.debugger.pause();
    set({ running: false });
  }
}));
