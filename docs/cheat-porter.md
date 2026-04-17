[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.CheatPorter — Cheat Code Porting

The `CodeDesigner.Ps2.CheatPorter` project provides tools for porting cheat codes between different versions/revisions of PS2 games.

## Overview

PS2 games often have multiple revisions with different memory layouts. Cheat codes written for one version may not work on another due to shifted addresses, changed data structures, or relocated functions. CheatPorter automates the process of updating cheat codes for new game versions.

## Key Features

### Automatic Address Translation
- Analyzes memory differences between game versions
- Identifies relocated code and data sections
- Updates cheat addresses automatically

### Pattern Matching
- Uses binary signatures to locate functions/data
- Handles code refactoring and optimization changes
- Supports both static and dynamic address resolution

### Batch Processing
- Processes entire cheat code collections
- Generates compatibility reports
- Validates ported codes against target version

## Usage

### Command Line

```bash
# Port cheats from version A to version B
CheatPorter.exe --source-version SLUS-12345.v1 --target-version SLUS-12345.v2 --cheat-file cheats.txt

# Analyze differences between versions
CheatPorter.exe --analyze --old-elf game_v1.elf --new-elf game_v2.elf
```

### API Usage

```csharp
var porter = new CheatPorter();
var portedCodes = porter.PortCheats(
    sourceVersion: "SLUS-12345",
    targetVersion: "SLUS-12346",
    cheatCodes: originalCheats
);
```

## Algorithm

### 1. Binary Diff Analysis
- Compares ELF executables or memory dumps
- Identifies changed, moved, and identical sections
- Builds relocation map

### 2. Signature Generation
- Extracts function prologue/epilogue patterns
- Creates data structure fingerprints
- Generates unique identifiers for code blocks

### 3. Address Resolution
- Matches signatures in target version
- Calculates address offsets
- Updates cheat code addresses

### 4. Validation
- Tests ported codes in emulator
- Verifies expected behavior
- Reports success/failure rates

## Supported Cheat Formats

- GameShark/Action Replay codes
- Raw memory addresses
- CDS script references
- Custom binary patches

## Integration

### With Database
- Stores version metadata and signatures
- Caches analysis results
- Shares patterns across games

### With Emulator
- Live testing of ported codes
- Memory scanning for validation
- Automated testing pipelines

## Examples

### Porting Infinite Health Cheat

Original (v1.0):
```
80010000 000000FF  // Health = 255
```

Analysis finds health variable moved from 0x00100000 to 0x00105000.

Ported (v1.1):
```
80015000 000000FF  // Health = 255 (updated address)
```

### Function Hook Porting

Original hook on `Player::TakeDamage` at 0x00200000.

Target version has function at 0x00210000.

Porter updates hook address and adjusts relative offsets.

## Limitations

- Cannot handle major engine rewrites
- Requires sufficient binary similarity
- May need manual intervention for complex changes
- Limited support for dynamically allocated data

## Best Practices

### Version Management
- Maintain version history in database
- Use consistent naming conventions
- Document known incompatibilities

### Testing
- Always test ported codes thoroughly
- Use emulator save states for comparison
- Validate on multiple hardware configurations

### Maintenance
- Update signatures when new versions release
- Monitor community reports for issues
- Provide fallback manual porting options</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\cheat-porter.md