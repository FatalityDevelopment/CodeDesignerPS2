// The Disassembler's state and commands. One instance, shared with the Emulator page, because both show and drive the
// same emulator. Polls run state over the Bridge; the listing reloads when the emulator stops somewhere new.

import type { LabelEntry, RegisterDump } from '@bridge-client';
import { bridge, bridgeState } from '../../api/bridge';
import { currentGame, gameId } from '../../shared/gameContext';
import { host } from '../../shared/host';
import { effect, signal, untracked } from '../../shared/signals';

export interface DisasmRow {
  address: number;
  hex: string;
  assembly: string;
  label: string;
  comment: string;
  byteLength: number;
}

export const ROW_COUNT = 64;
const WORD = 4;
const POLL_MS = 500;
const ADDRESS_MASK = 0xfffffffc;

export const formatAddress = (value: number) => (value >>> 0).toString(16).toUpperCase().padStart(8, '0');

// A typed address: hex, with or without 0x.
export function parseAddress(text: string): number | null {
  const m = /^(?:0x)?([0-9a-f]{1,8})$/i.exec(text.trim());
  return m ? parseInt(m[1], 16) >>> 0 : null;
}

// The DISASSEMBLE command's input: one little-endian word per instruction, in the form the extension sends.
export function toHexWords(bytes: Uint8Array): string {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const words: string[] = [];
  for (let offset = 0; offset + WORD <= bytes.length; offset += WORD) {
    words.push(`0x${view.getUint32(offset, true).toString(16).padStart(8, '0')}`);
  }
  return words.join(' ');
}

class DisassemblerLogic {
  readonly base = signal(0x00100000);
  readonly rows = signal<DisasmRow[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly pc = signal<number | null>(null);
  readonly paused = signal(false);
  readonly registers = signal<RegisterDump | null>(null);
  readonly breakpoints = signal<ReadonlySet<number>>(new Set());
  readonly labels = signal<LabelEntry[]>([]);
  readonly selected = signal<number | null>(null);

  private timer: ReturnType<typeof setInterval> | undefined;
  private load = 0;

  start(): void {
    effect(() => {
      clearInterval(this.timer);
      if (bridgeState() !== 'online') { return; }
      untracked(() => { void this.loadLabels(); void this.refresh(); });
      this.timer = setInterval(() => void this.poll(), POLL_MS);
    });
    // A new game means new labels and an address space that may no longer apply.
    effect(() => {
      currentGame();
      if (untracked(bridgeState) === 'online') { untracked(() => { void this.loadLabels(); void this.refresh(); }); }
    });
  }

  private async poll(): Promise<void> {
    try {
      const state = await bridge.getPC();
      const stopped = state.paused && (state.pc !== this.pc() || !this.paused());
      this.paused.set(state.paused);
      this.pc.set(state.pc);
      if (stopped) { await this.showStop(state.pc); }
    } catch {
      this.pc.set(null); // nothing attached yet
    }
  }

  // The emulator stopped: bring the PC into view and read the registers.
  private async showStop(pc: number): Promise<void> {
    if (pc < this.base() || pc >= this.base() + ROW_COUNT * WORD) { await this.goto(pc); } else { await this.refresh(); }
    await this.loadRegisters();
  }

  async goto(address: number): Promise<void> {
    this.base.set((address & ADDRESS_MASK) >>> 0);
    this.selected.set((address & ADDRESS_MASK) >>> 0);
    await this.refresh();
  }

  async scroll(rows: number): Promise<void> {
    this.base.set(Math.max(0, this.base() + rows * WORD));
    await this.refresh();
  }

  async refresh(): Promise<void> {
    const load = ++this.load;
    const base = this.base();
    this.loading.set(true);
    try {
      const bytes = await bridge.readMemory(base, ROW_COUNT * WORD);
      const results = await bridge.disassemble(toHexWords(bytes), base, currentGame()?.serial || undefined);
      if (load !== this.load) { return; }
      this.rows.set(results.map((r, i) => ({
        address: r.Address ?? base + i * WORD,
        hex: r.Hex ?? '',
        assembly: r.Assembly ?? r.Error ?? '',
        label: r.Label ?? '',
        comment: r.Comment ?? '',
        byteLength: r.ByteLength ?? WORD,
      })));
      this.error.set(null);
    } catch (err) {
      if (load === this.load) { this.error.set(err instanceof Error ? err.message : String(err)); }
    } finally {
      if (load === this.load) { this.loading.set(false); }
    }
  }

  async loadRegisters(): Promise<void> {
    try { this.registers.set(await bridge.getRegisters()); } catch { this.registers.set(null); }
  }

  async loadLabels(): Promise<void> {
    try { this.labels.set(await bridge.getLabels()); } catch { this.labels.set([]); }
  }

  // Runs one debugger action; a failure shows as a notification, and a stop is picked up by the next poll.
  private async act(name: string, run: () => Promise<unknown>): Promise<void> {
    try {
      await run();
      await this.poll();
    } catch (err) {
      host.notify(`${name}: ${err instanceof Error ? err.message : String(err)}`, 'error');
    }
  }

  pause = () => this.act('Pause', () => bridge.pause());
  resume = () => this.act('Resume', () => bridge.resume());
  stepInto = () => this.act('Step into', () => bridge.stepInto());
  stepOver = () => this.act('Step over', () => bridge.stepOver());
  stepOut = () => this.act('Step out', () => bridge.stepOut());

  async toggleBreakpoint(address: number): Promise<void> {
    const on = this.breakpoints().has(address);
    await this.act('Breakpoint', () => (on ? bridge.clearBreakpoint(address) : bridge.setBreakpoint(address)));
    const next = new Set(this.breakpoints());
    if (on) { next.delete(address); } else { next.add(address); }
    this.breakpoints.set(next);
  }

  async clearBreakpoints(): Promise<void> {
    await this.act('Breakpoints', () => bridge.clearAllBreakpoints());
    this.breakpoints.set(new Set());
  }

  async addLabel(address: number): Promise<void> {
    const name = await host.askText(`Label at ${formatAddress(address)}`, '', '^[A-Za-z_][A-Za-z0-9_.]*$', 'Letters, digits, _ and . only');
    if (!name) { return; }
    await this.act('Label', () => bridge.addLabel(address, name.trim()));
    await this.loadLabels();
    await this.refresh();
  }

  async deleteLabel(address: number): Promise<void> {
    await this.act('Label', () => bridge.deleteLabel(address));
    await this.loadLabels();
    await this.refresh();
  }

  // The game the listing is showing, for the status bar.
  gameLabel(): string {
    const game = currentGame();
    return game ? `${game.title}${game.serial ? ` (${game.serial})` : ''}` : 'No game';
  }

  labelScope(): string | undefined {
    return gameId(currentGame());
  }
}

export const disassembler = new DisassemblerLogic();
