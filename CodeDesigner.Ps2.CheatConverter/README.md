# CodeDesigner.Ps2.CheatConverter — Cheat Code Format Converter

## Overview

`CodeDesigner.Ps2.CheatConverter` detects and converts PS2 cheat codes between multiple device-specific encryption formats and the raw (unencrypted) format used internally by CodeDesigner.

---

## Supported Formats

| Format | Class | Notes |
|--------|-------|-------|
| Raw (unencrypted) | `RawConverter` | No transformation; used as the canonical internal representation |
| Action Replay 2 | `ActionReplay2Converter` | Original AR2 encryption scheme |
| AR Max | `ArMaxConverter` | AR Max / AR MAX EVO variant |
| CodeBreaker v1 | `CodeBreakerV1Converter` | Early CodeBreaker encryption |
| CodeBreaker v7 | `CodeBreakerV7Converter` | CodeBreaker v7 / v8 encryption |
| GameShark v3 | `GameSharkV3Converter` | GameShark v3 / v5 encryption |

---

## Format Detection

`CheatFormatDetector` inspects a block of cheat lines and returns the most likely format:

```csharp
using CodeDesigner.Ps2.CheatConverter;

string cheatBlock = "B4336FA9 4DFEFB79\nDE6389E6 BCA99B37";
var format = CheatFormatDetector.Detect(cheatBlock);
Console.WriteLine(format); // e.g. ActionReplay2
```

---

## Conversion

Each converter implements `ICheatCodeConverter` with `Decode` (format → raw) and `Encode` (raw → format) methods:

```csharp
using CodeDesigner.Ps2.CheatConverter;

// Decode AR2 codes to raw
var converter = new ActionReplay2Converter();
var rawCodes = converter.Decode(arCodes);

// Re-encode to CodeBreaker v7
var cbConverter = new CodeBreakerV7Converter();
var cbCodes = cbConverter.Encode(rawCodes);
```

---

## Integration

- The **MCP bridge** exposes `detect_cheat_format` and `convert_cheat` tools so GitHub Copilot can work with codes from any device.
- See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the `DETECTFORMAT` and `CONVERTCHEATS` command schemas.
- See [CodeDesigner.Ps2.CheatEngine/README.md](../CodeDesigner.Ps2.CheatEngine/README.md) for the raw cheat format that CodeDesigner uses internally.
