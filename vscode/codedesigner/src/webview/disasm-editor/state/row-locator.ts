// ═══════════════════════════════════════════════════════════════════════════
// Row Locator
// ═══════════════════════════════════════════════════════════════════════════
//
// The editor cursor is a global *instruction index* (one unit per 4 bytes
// from the region base).  Disassembly rows, however, are variable length:
// data-format overrides and auto-string detection produce `.byte` / `.half`
// rows shorter than a word and `.dword` / `.qword` / string rows that span
// several words.  Consequently "row N of a page" and "instruction index
// pageStart + N" are *not* the same thing, and any lookup that assumes they
// are mis-identifies the cursor row as soon as a page contains a formatted
// row.
//
// Everything in this module resolves rows by *address* instead.  Positions
// are expressed as byte offsets from the region base so that sub-word rows
// stay addressable (`cursor * 4 + cursorByteOffset`).
//
// ═══════════════════════════════════════════════════════════════════════════

import { DisasmRow } from './types';

/**
 * Translate any virtual/logical EE address to its physical equivalent.
 *
 * KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
 * EE RAM uncached mirror (0x20000000–0x21FFFFFF): subtract 0x20000000.
 * EE RAM uncached+accelerated (0x30000000–0x31FFFFFF): subtract 0x30000000.
 * All other addresses pass through.
 */
export function toPhysAddr(addr: number): number {
  const v = addr >>> 0;
  if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
  if (v >= 0x20000000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
  if (v >= 0x30000000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
  return v;
}

/** Bytes per MIPS instruction — the stride of the cursor coordinate. */
export const INSTRUCTION_BYTES = 4;

/**
 * Number of bytes a row covers, derived from its hex byte string.  Rows
 * without byte data (placeholder / error rows) are assumed to be one
 * instruction wide.
 */
export function rowByteLength(row: DisasmRow): number {
  const hex = (row.bytes ?? '').replace(/[^0-9a-f]/gi, '');
  return hex.length >= 2 ? Math.floor(hex.length / 2) : INSTRUCTION_BYTES;
}

/** Byte offset of a row's start address from the region base address. */
export function rowByteOffset(row: DisasmRow, baseAddress: number): number {
  return toPhysAddr(parseInt(row.addr, 16)) - toPhysAddr(baseAddress);
}

/** Global instruction index of the word that contains the row's start address. */
export function rowInstructionIndex(row: DisasmRow, baseAddress: number): number {
  return Math.floor(rowByteOffset(row, baseAddress) / INSTRUCTION_BYTES);
}

/** Byte offset from the region base for a cursor position. */
export function cursorToByteOffset(cursor: number, cursorByteOffset: number): number {
  return cursor * INSTRUCTION_BYTES + cursorByteOffset;
}

/** Whether `byteOffset` (from the region base) falls inside the row's span. */
export function rowContainsByteOffset(row: DisasmRow, baseAddress: number, byteOffset: number): boolean {
  const start = rowByteOffset(row, baseAddress);
  return byteOffset >= start && byteOffset < start + Math.max(1, rowByteLength(row));
}

/**
 * Index of the row whose byte span contains `byteOffset`, or -1 when no row
 * covers it.  `rows` must be sorted by ascending address, which holds for a
 * single page and for adjacent pages concatenated in order.
 */
export function findRowIndexForByteOffset(
  rows: readonly DisasmRow[],
  baseAddress: number,
  byteOffset: number,
): number {
  if (rows.length === 0) { return -1; }

  // Binary search for the last row whose start offset is <= byteOffset.
  let lo = 0;
  let hi = rows.length - 1;
  let candidate = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const start = rowByteOffset(rows[mid], baseAddress);
    if (start <= byteOffset) {
      candidate = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }

  if (candidate < 0) { return -1; }
  return rowContainsByteOffset(rows[candidate], baseAddress, byteOffset) ? candidate : -1;
}

/**
 * Index of the row that starts exactly at the given address string (as
 * produced by the extension host, e.g. "00100040"), or -1.  Uses the same
 * ordering assumption as {@link findRowIndexForByteOffset}.
 */
export function findRowIndexByAddr(rows: readonly DisasmRow[], addr: string): number {
  if (rows.length === 0) { return -1; }
  const target = toPhysAddr(parseInt(addr, 16));
  let lo = 0;
  let hi = rows.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const value = toPhysAddr(parseInt(rows[mid].addr, 16));
    if (value === target) { return mid; }
    if (value < target) { lo = mid + 1; } else { hi = mid - 1; }
  }
  return -1;
}
