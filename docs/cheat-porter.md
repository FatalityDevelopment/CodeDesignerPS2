[← Back to Documentation Index](README.md)

# Cheat Porter

The Cheat Porter moves cheat codes from one version of a game to another, for example from an NTSC release to a PAL one or from v1.0 to a patched build. It compares the EE RAM of both versions and works out where each address in the codes moved to.

---

## What you need

Two EE RAM images taken at a comparable point in each game (same level or menu is best):

- PCSX2 save states (`.p2s`) of the current game,
- the live EE RAM of the running emulator,
- or dumps you browse to: `.bin` files, `.p2s` files or snapshot folders.

---

## Using the page

Open it from the sidebar (**Emulator → Tools → Porter**, or **Cheats → Cheat Tools → Porter**).

1. Pick **From** (the version the codes were written for) and **To** (the version you want them for). Both lists show the game's save states, live EE RAM and any dumps you've browsed. The same picker is used by Memory Diff.
2. Paste the codes, or click **Load game cheats** to fill the input from the current game's archive. Cheats are separated by name lines as usual.
3. Run the port. Results are grouped per cheat:

   | Filter | Meaning |
   |---|---|
   | **Ported** | Every address was found with a unique match (100% confidence). |
   | **Review** | A match was found, but there were several candidates or the confidence is below 100%. Pick the right candidate from the line's picker. |
   | **Not found** | At least one address couldn't be located. Partially found cheats can't be selected for output. |

4. The output preview updates as you choose. **Copy** it, or **Open in editor**.

Your input and choices are kept when the page reloads.

---

## How addresses are found

For each unique address in the codes, the porter tries four strategies in order and stops at the first that finds something:

1. **Hex pattern** — takes the bytes just before the address (48, 32, then 16 bytes, skipping all-zero runs) and searches the target dump. Matches at the *same* position in both dumps are dropped, since an address that didn't move shouldn't latch onto an unrelated copy.
2. **Code fingerprint** — finds MIPS `lui` + load/store/`addiu` pairs that build the address in the source code (user, KSEG0 and KSEG1 forms), masks the address bits, and looks for the same instructions in the target.
3. **Extended anchor** — scans back up to 512 bytes for the nearest meaningful byte run and uses it as an anchor.
4. **Pointer scan** — finds 32-bit pointers to the address (or to its structure header) in the source, reads the same slot in the target, and applies the same offset. This works well when both versions keep a pointer table at a stable place.

Candidates are ranked by distance from the source address. A unique match scores 100; more candidates lower the score and mark the line *ambiguous*.

### Code types

Only the words that hold an address are ported. The full 25-bit address is used, so codes above 16 MB port correctly.

| Type | Handling |
|---|---|
| Everything else (`0`/`1`/`2` writes, `D` conditional, …) | Address in word 1 is ported. |
| `3` increment / decrement | Address in word 2; the 32-bit forms keep their value line. |
| `4` serial write | Address ported; the step/increment line is kept. |
| `5` copy | Source (word 1) and destination (next line) are both ported. |
| `6` pointer write | Base address ported; offset lines are kept. |
| `E` multi-line conditional | Address in word 2 is ported. |
| `B` timer | Kept as is. |
| `F` master code | Both words are code addresses and are ported. |

Each result line says how it was handled (for example *Pointer offsets — copied unchanged*) and which method found it (for example `HexPattern-48`).

---

## From code and AI

- **Bridge**: `PORTCHEATS` resolves the two images to raw dumps and runs the porter.
- **MCP**: `port_cheats` takes the codes and two `.bin` dump paths.
- **Library**: `CodeDesigner.Ps2.CheatEngine.CheatPorter.CheatPorter.Port(sourceData, targetData, blocks, …)` returns one `PortCheatResult` per line, with `Found`, `NewCode`, `Confidence`, `Ambiguous`, `Candidates`, `Method` and `Note`.

---

## Tips

- Dump both versions at the same game state. The more of the surrounding memory matches, the better the hex-pattern and anchor strategies work.
- Codes that point into the heap (values that move every run) usually need the pointer scan, or a pointer (`6`) code instead.
- Always test ported codes. Snapshot first.
