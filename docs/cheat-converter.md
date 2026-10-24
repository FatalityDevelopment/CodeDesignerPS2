[← Back to Documentation Index](README.md)

# Cheat Converter

The Cheat Converter decrypts cheat codes from commercial PS2 cheat devices into the plain **raw** format CodeDesigner uses, and encrypts raw codes for a device. It lives in `CodeDesigner.Ps2.CheatEngine/CheatConverter/`.

---

## Formats

| Format (`CheatFormat`) | Device | Encryption |
|---|---|---|
| `Raw` | Unencrypted GameShark / Action Replay / CodeBreaker (`TTAAAAAA VVVVVVVV`) | none |
| `ActionReplay1` | Action Replay 1 | fixed-seed byte XOR |
| `ActionReplay2` | Action Replay 2 / GameShark 2 | `DEADFACE`-seeded byte XOR |
| `ActionReplayMax` | Action Replay MAX | DES-like block cipher, 32-character alphanumeric codes |
| `GameSharkV3` | GameShark 3+, Xploder 4, Xterminator | Mersenne Twister cipher |
| `CodeBreakerV1` | CodeBreaker 1–6 | seed-table XOR / rotation |
| `CodeBreakerV7` | CodeBreaker 7+ (`BEEFC0DE` codes) | ARCFOUR + modular inverse |

### Detection

`CheatFormatDetector.Detect` guesses the format from the text: all-alphanumeric ARMAX lines, a `BEEFC0DE` or `DEADFACE` key line, or address bits that only make sense encrypted. When nothing matches it returns `Raw`. Name lines (`#…`) and `//` comments are ignored.

---

## In VS Code

Open it from the **Cheats** tab → **Cheat Tools → Converter**. Paste codes into **Input Codes**, pick the device format, then:

- **Convert to Raw** decrypts the input from the selected format to raw codes.
- **Convert to Engine** encrypts raw input codes to the selected format.

The result appears in **Output Codes**.

---

## From the Bridge and MCP

| | Command / tool | Parameters |
|---|---|---|
| Bridge | `CONVERTCHEAT` | `Data` (codes), `Direction` (`toRaw` or `toEngine`), `Format` (optional for `toRaw`: auto-detected when left out; required for `toEngine`) |
| MCP | `convert_cheat` | `codes`, `direction`, `targetFormat` (same rules as `Format`) |
| MCP | `detect_cheat_format` | `codes` |

In code, every format implements `ICheatCodeConverter` (`Decode(text)` → `RawCode` list, `Encode(codes)` → text); `CheatFormatDetector.CreateConverter(format)` returns the right one.

---

## Credits

The cipher implementations follow the PS2 code-conversion work by [mlafeldt](https://github.com/mlafeldt).
