[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.MemoryCardManager — PS2 Memory Card Emulation

The `CodeDesigner.Ps2.MemoryCardManager` project provides comprehensive emulation and management of PlayStation 2 memory cards, including save file manipulation and cross-platform compatibility.

## Overview

PS2 memory cards store game save data, system settings, and downloadable content. This project emulates the PS2 memory card hardware and provides tools for managing save files across different formats and systems.

## Features

### Memory Card Emulation
- **8 MB card simulation** (standard PS2 capacity)
- **2-slot support** (simultaneous card insertion)
- **Hardware-accurate timing** and error simulation
- **PS1 backward compatibility** mode

### Save File Management
- **Import/Export saves** from/to .ps2 file format
- **Directory browsing** and file operations
- **Save state management** with metadata
- **Corruption detection** and repair

### Cross-Platform Support
- **Windows/Linux/Mac** compatibility
- **PS2/PS3/PSP** save file conversion
- **Cloud backup** integration
- **Network sharing** capabilities

## Memory Card Format

### Physical Structure
- **8 MB total capacity** (2× 3.5 MB partitions + system area)
- **Clusters**: 512-byte allocation units
- **FAT-like filesystem** with directory entries
- **ECC protection** for data integrity

### File System
- **Root directory** with game save folders
- **Icon.sys/Icon.icn** for save thumbnails
- **Save data** in proprietary formats per game
- **System files** for card management

## Usage

### Command Line

```bash
# Create new memory card
MemoryCardManager.exe --create card.ps2

# Import save file
MemoryCardManager.exe --import save.psu --card card.ps2

# Export all saves
MemoryCardManager.exe --export-all --card card.ps2 --output saves/

# List card contents
MemoryCardManager.exe --list --card card.ps2
```

### API Usage

```csharp
var manager = new MemoryCardManager();

// Create new card
var card = manager.CreateCard("card.ps2");

// Import save
manager.ImportSave("save.psu", card);

// Export save
manager.ExportSave(card, "BASLUS-12345", "save.psu");
```

## Save File Operations

### Import Formats
- **.ps2**: Native PS2 memory card files
- **.psu**: Single save file (PS2 format)
- **.psv**: Single save file (PS3/PSP format)
- **.max**: Action Replay save files
- **.sps**: SharkPort save files

### Export Options
- **Individual saves** as .psu files
- **Full card images** as .ps2 files
- **Batch export** with directory structure
- **Compressed archives** for storage

### File Management
- **Copy/Move/Delete** saves between cards
- **Rename saves** with custom titles
- **Edit save metadata** (title, icon, description)
- **Duplicate detection** and merging

## Integration

### With Emulator
- **Hot-swap cards** during gameplay
- **Save state compatibility** with card contents
- **Automatic backup** on save operations
- **Corruption recovery** from emulator crashes

### With Database
- **Save metadata storage** (play time, achievements)
- **Game compatibility** tracking
- **User profile management**
- **Backup scheduling**

### With VS Code Extension
- **Graphical card browser**
- **Drag-and-drop file operations**
- **Save comparison tools**
- **Bulk management operations**

## Advanced Features

### Save Analysis
- **Hex editor** for save file contents
- **Structure parsing** for known game formats
- **Cheat detection** and removal
- **Data validation** and repair

### Backup and Sync
- **Automatic backups** on save
- **Cloud storage** integration
- **Multi-device sync**
- **Version history** tracking

### Security
- **Save encryption** for sensitive data
- **Digital signatures** verification
- **Tamper detection**
- **Secure deletion** of old saves

## Examples

### Creating a Card

```csharp
var manager = new MemoryCardManager();
var card = manager.CreateCard("my_card.ps2");
card.Format(); // Initialize filesystem
```

### Importing Saves

```csharp
// Import single save
manager.ImportSave("ffx_save.psu", card);

// Import from directory
manager.ImportDirectory("saves/", card);
```

### Managing Saves

```csharp
// List all saves
var saves = card.ListSaves();
foreach (var save in saves) {
    Console.WriteLine($"{save.GameId}: {save.Title}");
}

// Delete old save
card.DeleteSave("BASLUS-12345");
```

## Troubleshooting

### Common Issues
- **Corrupted cards**: Use repair tools
- **Incompatible saves**: Check region/format
- **Storage full**: Clean up old saves
- **Permission errors**: Run as administrator

### Recovery Options
- **Filesystem repair** for damaged cards
- **Data carving** from corrupted saves
- **Backup restoration** from previous versions
- **Format recovery** for bricked cards

## Best Practices

### Organization
- **Game-specific folders** for saves
- **Regular backups** to prevent loss
- **Descriptive names** for save files
- **Version numbering** for important saves

### Maintenance
- **Regular defragmentation** of cards
- **Filesystem checks** before important operations
- **Update tools** for new game compatibility
- **Monitor storage usage** and clean up</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\memory-card-manager.md