// ═══════════════════════════════════════════════════════════════════════════
// Register Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
//
// Pure functions for MIPS register formatting and colorization.
//
// ═══════════════════════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════════════════════
// GPR Names and Categories
// ═══════════════════════════════════════════════════════════════════════════

/** Standard MIPS GPR names (index 0-31). */
export const GPR_NAMES = [
  'zero', 'at', 'v0', 'v1', 'a0', 'a1', 'a2', 'a3',
  't0', 't1', 't2', 't3', 't4', 't5', 't6', 't7',
  's0', 's1', 's2', 's3', 's4', 's5', 's6', 's7',
  't8', 't9', 'k0', 'k1', 'gp', 'sp', 'fp', 'ra',
] as const;

/** PS2 EE COP0 register names. */
export const COP0_NAMES = [
  'Index', 'Random', 'EntryLo0', 'EntryLo1',
  'Context', 'PageMask', 'Wired', 'Reserved7',
  'BadVAddr', 'Count', 'EntryHi', 'Compare',
  'Status', 'Cause', 'EPC', 'PRId',
  'Config', 'Reserved17', 'Reserved18', 'Reserved19',
  'Reserved20', 'Reserved21', 'Reserved22', 'BadPAddr',
  'Debug', 'Perf', 'Reserved26', 'Reserved27',
  'TagLo', 'TagHi', 'ErrorEPC', 'Reserved31',
] as const;

// ═══════════════════════════════════════════════════════════════════════════
// Register Category CSS Classes
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Maps a GPR index (0-31) to its CSS category class.
 */
export function gprCategoryClass(index: number): string {
  if (index === 0)                                        return 'dbg-reg-zero';    // $zero
  if (index === 1)                                        return 'dbg-reg-at';      // $at
  if (index >= 2 && index <= 3)                           return 'dbg-reg-v';       // $v0-$v1
  if (index >= 4 && index <= 7)                           return 'dbg-reg-a';       // $a0-$a3
  if ((index >= 8 && index <= 15) || index === 24 || index === 25)
                                                          return 'dbg-reg-t';       // $t0-$t9
  if (index >= 16 && index <= 23)                         return 'dbg-reg-s';       // $s0-$s7
  if (index === 26 || index === 27)                       return 'dbg-reg-k';       // $k0-$k1
  return 'dbg-reg-special';                                                         // $gp $sp $fp $ra
}

/** Pre-computed map: GPR name => CSS category class. */
const GPR_NAME_TO_CATEGORY: Readonly<Record<string, string>> = (() => {
  const map: Record<string, string> = {};
  GPR_NAMES.forEach((name, index) => {
    map[name] = gprCategoryClass(index);
  });
  return map;
})();

/**
 * Get CSS category class for a GPR by name.
 * Returns undefined if not a valid GPR name.
 */
export function gprNameToCategory(name: string): string | undefined {
  return GPR_NAME_TO_CATEGORY[name.toLowerCase()];
}

// ═══════════════════════════════════════════════════════════════════════════
// Register Colorization
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Regex that matches GPR register names.
 * Handles both forms: 'zero' and '$zero', 't0' and '$t0', etc.
 * Longer/more-specific names are listed first to avoid partial matches.
 */
const GPR_REGEX = /\$?(zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra)(?![A-Za-z0-9_])/g;

/**
 * Wraps each GPR register token in text with a <span> carrying the
 * appropriate category colour class.
 *
 * @param text - Plain text containing register names (e.g., "addiu $t0, $a0, 4")
 * @returns HTML string with register names wrapped in coloured spans
 */
export function colorizeRegisters(text: string): string {
  if (!text) return text;

  return text.replace(GPR_REGEX, (match, name) => {
    const cls = GPR_NAME_TO_CATEGORY[name];
    if (!cls) return match;
    const dollar = match.startsWith('$') ? '$' : '';
    return `<span class="${cls}">${dollar}${name}</span>`;
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// Value Formatting
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Format a 32-bit value as 8-character uppercase hex.
 */
export function formatHex32(value: number): string {
  return (value >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

/**
 * Format a 64-bit value (as two 32-bit halves) as 16-character uppercase hex.
 */
export function formatHex64(lo32: number, hi32: number): string {
  return formatHex32(hi32) + formatHex32(lo32);
}

/**
 * Format a 128-bit GPR value from its four 32-bit components.
 * PS2 EE GPRs are 128-bit: [upHi32:upLo32:hi32:lo32]
 */
export function formatHex128(lo32: number, hi32: number, upLo32: number, upHi32: number): string {
  return formatHex32(upHi32) + formatHex32(upLo32) + '_' + formatHex32(hi32) + formatHex32(lo32);
}

/**
 * Format a floating-point value for display.
 */
export function formatFloat(value: number, precision = 6): string {
  if (!Number.isFinite(value)) {
    if (Number.isNaN(value)) return 'NaN';
    return value > 0 ? '+Inf' : '-Inf';
  }
  return value.toPrecision(precision);
}

/**
 * Interpret a 32-bit value as IEEE 754 single-precision float.
 */
export function uint32ToFloat(bits: number): number {
  const buffer = new ArrayBuffer(4);
  const view = new DataView(buffer);
  view.setUint32(0, bits >>> 0, true);
  return view.getFloat32(0, true);
}

/**
 * Format a 32-bit value as both hex and float.
 */
export function formatHexAndFloat(bits: number): string {
  const hex = formatHex32(bits);
  const float = formatFloat(uint32ToFloat(bits));
  return `${hex} (${float})`;
}
