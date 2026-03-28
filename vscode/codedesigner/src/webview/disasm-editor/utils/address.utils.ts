// ═══════════════════════════════════════════════════════════════════════════
// Address Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
//
// Pure functions for PS2 EE address manipulation and parsing.
//
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Translate any virtual/logical EE address to its physical equivalent.
 *
 * KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
 * EE RAM uncached mirror (0x20000000–0x21FFFFFF): subtract 0x20000000.
 * EE RAM uncached+accelerated (0x30000000–0x31FFFFFF): subtract 0x30000000.
 * All other addresses (physical or special like scratchpad) pass through.
 */
export function toPhysAddr(addr: number): number {
  const v = addr >>> 0;
  if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
  if (v >= 0x20000000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
  if (v >= 0x30000000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
  return v;
}

/**
 * Parse a hex address string to a number.
 * Accepts formats: "0x12345678", "12345678", "$12345678"
 * Returns null if invalid.
 */
export function parseAddress(input: string): number | null {
  const cleaned = input.trim().toLowerCase()
    .replace(/^0x/, '')
    .replace(/^\$/, '');

  if (!/^[0-9a-f]+$/.test(cleaned)) {
    return null;
  }

  const value = parseInt(cleaned, 16);
  return isNaN(value) ? null : value;
}

/**
 * Format an address as an 8-character uppercase hex string.
 */
export function formatAddress(addr: number): string {
  return (addr >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

/**
 * Format an address with 0x prefix.
 */
export function formatAddressWithPrefix(addr: number): string {
  return '0x' + formatAddress(addr);
}

/**
 * Heuristic: does this value look like a valid EE code pointer?
 * Returns true if aligned to 4 bytes and within EE main RAM or BIOS regions.
 */
export function isCodePointer(addr: number): boolean {
  const v = addr >>> 0;

  // Must be non-zero and 4-byte aligned
  if (v === 0 || (v & 3) !== 0) {
    return false;
  }

  const phys = toPhysAddr(v);

  // EE main RAM (0x00000000–0x01FFFFFF) or BIOS (0x1FC00000–0x1FFFFFFF)
  return (phys < 0x02000000) || (phys >= 0x1FC00000 && phys < 0x20000000);
}

/**
 * Calculate instruction index from address given a base address.
 * Returns -1 if address is not in the valid range.
 */
export function addressToInstructionIndex(
  addr: number,
  baseAddress: number,
  totalInstructions: number
): number {
  const physAddr = toPhysAddr(addr);
  const physBase = toPhysAddr(baseAddress);
  const index = Math.floor((physAddr - physBase) / 4);

  if (index < 0 || index >= totalInstructions) {
    return -1;
  }

  return index;
}

/**
 * Calculate address from instruction index given a base address.
 */
export function instructionIndexToAddress(
  index: number,
  baseAddress: number
): number {
  return (baseAddress + index * 4) >>> 0;
}

/**
 * Parse a byte pattern from search input.
 * Accepts formats: "12 34 AB CD", "0x12 0x34", "12,34,AB"
 * Returns null if invalid.
 */
export function parseBytePattern(pattern: string): number[] | null {
  const cleaned = pattern
    .replace(/0x/gi, '')
    .replace(/[,\s]+/g, ' ')
    .trim();

  if (!cleaned) {
    return null;
  }

  const bytes = cleaned.split(/\s+/);
  const result: number[] = [];

  for (const byte of bytes) {
    if (!/^[0-9a-f]{1,2}$/i.test(byte)) {
      return null;
    }
    result.push(parseInt(byte, 16));
  }

  return result.length > 0 ? result : null;
}
