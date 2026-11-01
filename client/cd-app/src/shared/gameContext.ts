// The one current game. Every page reads it from here, so nothing asks the user for a serial, and a change reaches all
// pages at once. Follows the running emulator; a disc picked in the Emulator page stands in until one is attached.

import { bridge, bridgeState } from '../api/bridge';
import { effect, signal } from './signals';

export interface GameContext {
  title: string;
  // Empty when no serial could be worked out; never a file path.
  serial: string;
  discId: number | null;
  isoPath: string | null;
}

const POLL_MS = 3000;

export const currentGame = signal<GameContext | null>(null);

// What label and data-format commands key their records on, best first (matches the extension's context order).
export function gameId(game: GameContext | null): string | undefined {
  if (!game) { return undefined; }
  return game.discId !== null ? String(game.discId) : game.serial || undefined;
}

// A disc serial from a SYSTEM.CNF BOOT2 value such as "cdrom0:\SLUS_216.06;1".
export function serialFromBootElf(bootElf: string): string | undefined {
  let s = bootElf.trim();
  const slash = Math.max(s.lastIndexOf('\\'), s.lastIndexOf('/'));
  if (slash >= 0) { s = s.slice(slash + 1); }
  const semi = s.indexOf(';');
  if (semi >= 0) { s = s.slice(0, semi); }
  s = s.trim().toUpperCase().replace(/_/g, '-').replace(/\./g, '');
  return /^[A-Z]{3,4}-?\d{3,6}$/.test(s) ? s : undefined;
}

const fileTitle = (path: string) => (path.split(/[\\/]/).pop() ?? path).replace(/\.[^.]+$/, '');

async function liveGame(): Promise<{ serial: string; title: string } | null> {
  try {
    const info = await bridge.getPcsx2GameInfo();
    const serial = (info.ID ?? '').replace(/\0/g, '').trim().toUpperCase();
    return serial ? { serial, title: (info.Title ?? '').replace(/\0/g, '').trim() } : null;
  } catch {
    return null; // no emulator attached
  }
}

async function publish(serial: string, title: string, isoPath: string | null): Promise<void> {
  let discId: number | null = null;
  try {
    discId = (await bridge.updateGameDisc(serial, isoPath ?? undefined, undefined, title)) ?? null;
  } catch { /* the database row is best-effort */ }
  currentGame.set({ title, serial, discId, isoPath });
}

// Sets the game from a chosen disc image: the running game if one is attached, else the disc's own SYSTEM.CNF.
export async function selectDisc(isoPath: string): Promise<void> {
  const live = await liveGame();
  if (live) { await publish(live.serial, live.title || fileTitle(isoPath), isoPath); return; }
  let serial: string | undefined;
  try { serial = serialFromBootElf(await bridge.isoBootElf(isoPath)); } catch { /* unreadable image */ }
  if (serial) { await publish(serial, fileTitle(isoPath), isoPath); return; }
  currentGame.set({ title: fileTitle(isoPath), serial: '', discId: null, isoPath });
}

async function followEmulator(): Promise<void> {
  const live = await liveGame();
  if (live && live.serial !== currentGame()?.serial) { await publish(live.serial, live.title, currentGame()?.isoPath ?? null); }
}

let timer: ReturnType<typeof setInterval> | undefined;

export function startGameContext(): void {
  effect(() => {
    clearInterval(timer);
    if (bridgeState() !== 'online') { return; }
    void followEmulator();
    timer = setInterval(() => void followEmulator(), POLL_MS);
  });
}
