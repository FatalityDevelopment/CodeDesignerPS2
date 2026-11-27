---
name: cd-cheat-port
description: Port PS2 GameShark/Action Replay cheats between game revisions — detect format, decrypt to raw, scan source vs target dumps for matching patterns, recalculate addresses, output ported codes. Offline (no live emulator needed); optionally verified in Ps2Emulator.
argument-hint: <source-dump-path> <target-dump-path>
---

# Port cheats across revisions

The user typically has working cheats for one game revision and wants them
working on another (different region/version) without re-finding addresses.

## Required inputs

- A block of cheat codes (paste in chat).
- Path to an EE RAM dump from the **source** revision (`EEmem.bin` from a
  Code Designer snapshot — panel → Actions → Snapshot while Ps2Emulator
  runs that revision).
- Path to an EE RAM dump from the **target** revision.

## Steps

1. **Identify the format**: `detect_cheat_format codes=<paste>`. Returns one
   of Raw, ActionReplay1/2/Max, GameSharkV3, CodeBreakerV1/V7.

2. **Decrypt to Raw** if needed: `convert_cheat codes=<paste> direction=toRaw
   [targetFormat=<detected>]`. Skip if already Raw.

3. **Run the port**: `port_cheats codes=<raw> sourceDumpPath=<src>
   targetDumpPath=<tgt>`. The backend scans both dumps for matching byte
   patterns around each code's address and recalculates offsets.

4. **Re-encrypt** (optional) to the user's preferred device format:
   `convert_cheat codes=<ported_raw> direction=toEngine targetFormat=<format>`.

5. **Apply + verify** (live, optional): with the target revision running in
   Ps2Emulator, `ps2_set_patch name=<cheat> codes=<ported raw>` and ask the
   user to confirm the effect in-game (`ps2_screenshot` helps). Then
   `ps2_read_values` at each new address to spot-check the values.

6. **Persist**: `progress_save` named `<target-game>-<cheats-name>`
   with the ported block + a note on confidence (ambiguous-match warnings,
   patterns not found, etc.).

## Tips

- The port can return `(AMBIGUOUS)` rows when multiple matches exist —
  surface them to the user and ask for visual confirmation before applying.
- If `port_cheats` reports many `NOT FOUND` rows, the dumps are likely from
  unrelated game states. Ask the user for snapshots taken closer to the same
  in-game context.
