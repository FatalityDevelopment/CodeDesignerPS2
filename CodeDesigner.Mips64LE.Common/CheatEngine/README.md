# CodeDesigner Cheat Engine — Format Reference

This document describes the custom cheat code format used by the CodeDesigner PS2 emulator cheat engine. This format is **not** GameShark, Action Replay, or any other third-party standard — it is a bespoke format designed for direct integration with the CodeDesigner emulator memory bus.

---

## File Structure

A cheat file is plain text containing one or more **cheat blocks** separated by a blank line. Each block contains an optional label comment followed by one or more **cheat lines**.

```
#Block Label
TTAAAAAA DDDDDDDD

#Another Block
TTAAAAAA DDDDDDDD
TTAAAAAA DDDDDDDD
```

- Blocks are separated by **one or more blank lines**.
- Lines are case-insensitive (uppercase and lowercase hex are both accepted).

---

## Line Format

Each cheat line consists of **two 8-character hexadecimal fields** separated by whitespace:

```
TTAAAAAA DDDDDDDD
```

| Field      | Width  | Description                                               |
|------------|--------|-----------------------------------------------------------|
| `TT`       | 2 hex  | **Cheat type** — determines how the line is interpreted   |
| `AAAAAA`   | 6 hex  | **Address** — the lower 24 bits of the target address     |
| `DDDDDDDD` | 8 hex  | **Data** — value or parameters (interpretation varies by type) |

The full effective address written to is:

```
effective_address = 0x20000000 + AAAAAA
```

This means addresses are relative to the PS2 EE uncached user RAM mirror (`0x20000000–0x21FFFFFF`). KSEG0/KSEG1 addresses (`0x80000000–0xBFFFFFFF`) are also accepted and translated automatically.

---

## Block Label

A block label is a line starting with `#`. It must appear before any cheat lines in the block and is for human readability only — it has no effect on execution.

```
#Infinite Health
20123456 000003E8
```

---

## Cheat Types

### `00` — 8-Bit Write

Writes the **lowest byte** of the data field to the target address.

```
TTAAAAAA DDDDDDDD
00AAAAAA 000000VV
```

| Field | Description                          |
|-------|--------------------------------------|
| `VV`  | 1-byte value to write (lowest byte)  |

**Example** — Write `0xFF` to address `0x20123456`:
```
00123456 000000FF
```

---

### `10` — 16-Bit Write

Writes the **lowest 2 bytes** of the data field to the target address. Written in little-endian order.

```
10AAAAAA 0000VVVV
```

| Field  | Description                          |
|--------|--------------------------------------|
| `VVVV` | 2-byte value to write (little-endian)|

**Example** — Write `0x03E8` (1000 decimal) to address `0x20123456`:
```
10123456 000003E8
```

---

### `20` — 32-Bit Write

Writes all **4 bytes** of the data field to the target address. Written in little-endian order.

```
20AAAAAA VVVVVVVV
```

| Field      | Description                           |
|------------|---------------------------------------|
| `VVVVVVVV` | 4-byte value to write (little-endian) |

**Example** — Write `0x3F800000` (1.0 as a float) to address `0x20001000`:
```
20001000 3F800000
```

---

### `50` — Copy Bytes

Reads `N` bytes from a source address and writes them to the destination address specified on the **next cheat line**. Advances past both lines when applied.

```
50SSSSSS NNNNNNNN
20DDDDDD 00000000
```

| Field       | Description                                        |
|-------------|----------------------------------------------------|
| `SSSSSS`    | Source address (lower 24 bits, + base offset)      |
| `NNNNNNNN`  | Number of bytes to copy (interpreted as a signed 32-bit int from the data field) |
| `DDDDDDDD`  | Destination address is taken from the **next line's address field** |

**Example** — Copy 16 bytes from `0x20001000` to `0x20002000`:
```
50001000 00000010
20002000 00000000
```

---

### `70` — Pointer Write *(reserved)*

Reserved for pointer-based writes. The address field holds the location of a 4-byte pointer in memory. The engine reads the pointer value but does not yet perform a secondary write — this type is a placeholder for future implementation. The line is consumed and execution continues.

```
70AAAAAA DDDDDDDD
```

---

### `B0` — Timer Gate

Introduces a time-based gate. The cheats in the **current block** that follow this line are **skipped** until the specified interval has elapsed. Once the interval expires the engine resets the timer and allows all subsequent cheats in the block to execute.

```
B0AAAAAA IIVVVVVV
```

The data field encodes:

| Bits    | Field  | Description                                               |
|---------|--------|-----------------------------------------------------------|
| `[31:24]` | `II` | **Interval type** — `01` = Ticks, `02` = Seconds, `03` = Minutes |
| `[23:0]`  | `VVVVVV` | **Interval value** (24-bit unsigned integer)          |

> The address field (`AAAAAA`) is ignored for timer lines.

**Example** — Gate the block with a 5-second timer:
```
B0000000 02000005
20123456 000003E8
```

---

### `C0` — 32-Bit Condition

Reads 4 bytes from the target address and compares them with the data field. If the values **do not match**, all remaining cheats in the current block are **skipped**. If they match, execution continues with the next cheat line.

```
C0AAAAAA VVVVVVVV
```

| Field       | Description                                          |
|-------------|------------------------------------------------------|
| `AAAAAA`    | Address to read from                                 |
| `VVVVVVVV`  | Expected 32-bit value (little-endian comparison)     |

**Example** — Only apply the next cheat if the value at `0x20123456` equals `0x00000001`:
```
C0123456 00000001
20001000 000003E8
```

---

### `D0` — 16-Bit Condition

Reads 2 bytes from the target address and compares them with the **lowest 2 bytes** of the data field. If the values **do not match**, all remaining cheats in the current block are **skipped**.

```
D0AAAAAA 0000VVVV
```

| Field  | Description                                            |
|--------|--------------------------------------------------------|
| `AAAAAA` | Address to read from                                 |
| `VVVV`   | Expected 16-bit value (little-endian comparison)     |

**Example** — Only apply the next cheat if the 16-bit value at `0x20005000` equals `0x0064` (100 decimal):
```
D0005000 00000064
10005000 000000FF
```

---

## Applying Cheats via the Emulator Panel

Open the **Emulator** sidebar panel (activity bar), switch to the **Cheats** tab, paste your cheat code text into the text area, and press **Update**. The engine will parse and apply every block in sequence against the live emulator RAM immediately.

---

## Address Translation Reference

Addresses in the cheat file are the **lower 24 bits** of the target virtual address. The engine adds the base offset (`0x20000000` by default) before the write. The following virtual address ranges are all resolved correctly:

| Virtual range              | Mapped to                              |
|----------------------------|----------------------------------------|
| `0x20000000–0x21FFFFFF`    | EE main RAM (direct, default range)    |
| `0x80000000–0x9FFFFFFF`    | EE main RAM (KSEG0, strips top 3 bits) |
| `0xA0000000–0xBFFFFFFF`    | EE main RAM (KSEG1, strips top 3 bits) |
| `0x70000000–0x70003FFF`    | EE Scratchpad (16 KB)                  |
