[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.CheatConverter — Cheat Format Conversion

The `CodeDesigner.Ps2.CheatConverter` project provides tools for converting cheat codes between different formats used by various PS2 cheat devices and software.

## Overview

PS2 cheat codes exist in multiple formats depending on the device or software used. CheatConverter enables seamless conversion between formats, ensuring compatibility across different tools and platforms.

## Supported Formats

### GameShark / Action Replay
- **GS1**: Original GameShark format (8-digit codes)
- **GS2**: Enhanced GameShark format (16-digit codes)
- **AR1**: Action Replay v1 format
- **AR2**: Action Replay v2 format
- **ARMax**: Action Replay MAX format

### CodeBreaker
- **CB1**: CodeBreaker v1-7 format
- **CB8**: CodeBreaker v8+ format

### Raw Formats
- **RAW**: Direct memory addresses and values
- **CDS**: CodeDesigner Script format
- **HEX**: Hexadecimal patch files

### Encrypted Formats
- **GS3**: GameShark v3+ encrypted codes
- **AR3**: Action Replay v3+ encrypted codes

## Key Features

### Automatic Detection
- Identifies input format automatically
- Validates code syntax and checksums
- Reports unsupported or invalid codes

### Batch Conversion
- Processes multiple codes simultaneously
- Preserves comments and metadata
- Generates conversion reports

### Encryption Handling
- Decrypts proprietary encrypted formats
- Re-encrypts for target devices
- Maintains code integrity

### Validation
- Verifies converted codes
- Tests in emulator when possible
- Reports potential issues

## Usage

### Command Line

```bash
# Convert GameShark to Action Replay
CheatConverter.exe --input cheats_gs.txt --output cheats_ar.txt --from GS2 --to AR2

# Auto-detect and convert
CheatConverter.exe --input mixed_codes.txt --output converted.txt

# Batch processing with validation
CheatConverter.exe --input cheats.txt --output validated.txt --validate
```

### API Usage

```csharp
var converter = new CheatConverter();
var result = converter.Convert(
    inputCodes: gsCodes,
    sourceFormat: CheatFormat.GameSharkV2,
    targetFormat: CheatFormat.ActionReplayV2
);
```

## Conversion Algorithms

### Address Translation
Different devices use different address encodings:
- GameShark: Encrypted address + value
- CodeBreaker: Direct address with type prefixes
- Raw: Unencoded memory addresses

### Value Encoding
- **8-bit/16-bit writes**: Different byte ordering
- **32-bit operations**: Endianness handling
- **Conditional codes**: Format-specific syntax

### Encryption/Decryption
- **GameShark encryption**: XOR-based algorithm
- **Action Replay encryption**: Proprietary cipher
- **Checksum validation**: Format-specific CRC

## Examples

### GameShark to Raw

Input (GS2):
```
20345678 12345678  // Infinite Health
```

Output (RAW):
```
0x00345678 = 0x12345678  // Infinite Health
```

### CodeBreaker to Action Replay

Input (CB8):
```
28345678 12345678  // Infinite Ammo
```

Output (AR2):
```
00345678 12345678  // Infinite Ammo
```

## Integration

### With Cheat Engine
- Automatic format conversion during application
- Fallback format support
- Error reporting and recovery

### With Database
- Stores conversion mappings
- Caches decrypted codes
- Tracks format popularity

### With VS Code Extension
- Real-time conversion in editor
- Format validation
- Import/export capabilities

## Error Handling

### Common Issues
- **Invalid checksums**: Codes corrupted or wrong format
- **Unsupported operations**: Advanced features not available in target format
- **Address conflicts**: Overlapping memory regions

### Recovery Options
- Skip invalid codes with warnings
- Attempt best-effort conversion
- Provide manual editing suggestions

## Best Practices

### Format Selection
- Use Raw format for development
- Choose device-specific formats for distribution
- Prefer unencrypted formats when possible

### Validation
- Always validate converted codes
- Test on target hardware/emulator
- Keep original codes as backup

### Maintenance
- Update conversion algorithms for new formats
- Monitor device compatibility
- Provide user feedback for issues</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\cheat-converter.md