import { describe, expect, it, vi } from 'vitest';

vi.mock('../api/bridge', () => ({ bridge: {}, bridgeState: () => 'offline' }));

const { gameId, serialFromBootElf } = await import('./gameContext');

describe('serialFromBootElf', () => {
  it('reads the serial from a BOOT2 value', () => {
    expect(serialFromBootElf('cdrom0:\\SCUS_972.75;1')).toBe('SCUS-97275');
    expect(serialFromBootElf('cdrom0:\\SLUS_216.06;1')).toBe('SLUS-21606');
  });

  it('rejects anything that is not a serial', () => {
    expect(serialFromBootElf('')).toBeUndefined();
    expect(serialFromBootElf('cdrom0:\\MAIN.ELF;1')).toBeUndefined();
  });
});

describe('gameId', () => {
  it('prefers the disc id, then the serial, and never uses a path', () => {
    expect(gameId({ title: 't', serial: 'SCUS-97275', discId: 7, isoPath: 'C:\\a.iso' })).toBe('7');
    expect(gameId({ title: 't', serial: 'SCUS-97275', discId: null, isoPath: 'C:\\a.iso' })).toBe('SCUS-97275');
    expect(gameId({ title: 't', serial: '', discId: null, isoPath: 'C:\\a.iso' })).toBeUndefined();
    expect(gameId(null)).toBeUndefined();
  });
});
