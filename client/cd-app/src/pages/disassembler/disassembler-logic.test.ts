import { describe, expect, it, vi } from 'vitest';

vi.mock('../../api/bridge', () => ({ bridge: {}, bridgeState: () => 'offline' }));

const { formatAddress, parseAddress, toHexWords } = await import('./disassembler-logic');

describe('toHexWords', () => {
  it('reads little-endian words in the form DISASSEMBLE takes', () => {
    expect(toHexWords(new Uint8Array([0x08, 0x00, 0xe0, 0x03, 0x00, 0x00, 0x00, 0x00]))).toBe('0x03e00008 0x00000000');
  });

  it('ignores a trailing partial word', () => {
    expect(toHexWords(new Uint8Array([1, 0, 0, 0, 9]))).toBe('0x00000001');
  });
});

describe('addresses', () => {
  it('parses hex with or without a prefix', () => {
    expect(parseAddress('0x00100000')).toBe(0x100000);
    expect(parseAddress(' 20BE4D88 ')).toBe(0x20be4d88);
  });

  it('keeps the top bit unsigned', () => {
    expect(parseAddress('FFFFFFFF')).toBe(0xffffffff);
    expect(formatAddress(0xffffffff)).toBe('FFFFFFFF');
  });

  it('rejects what is not an address', () => {
    expect(parseAddress('main')).toBeNull();
    expect(parseAddress('123456789')).toBeNull();
  });
});
