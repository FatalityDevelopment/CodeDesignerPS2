[← Back to Documentation Index](README.md)

# PCSX2 Setup Guide — Game Dumping

This guide explains how to dump PlayStation 2 games from disc to ISO format for use with PCSX2 and CodeDesigner.

## Why Dump Games?

### Benefits of ISO Files
- **Faster Loading**: No disc spin-up delays
- **Better Performance**: Consistent read speeds
- **Convenience**: Easy backup and organization
- **Compatibility**: Works with all PCSX2 features
- **Archival**: Preserve games in digital format

### Legal Considerations
- **Personal Use**: Dumping your own games is generally legal
- **Backup Copies**: Create backups of games you own
- **No Piracy**: Do not distribute dumped games
- **Region Laws**: Check local laws regarding game backups

## Requirements

### Hardware
- **PS2 Console**: Working PlayStation 2
- **Original Games**: Legitimate game discs
- **PC**: Windows, Linux, or macOS
- **Storage**: Sufficient space for ISO files (4-8GB each)

### Software
- **ImgBurn** (Windows) - Free disc burning/imaging software
- **DD** (Linux) - Command-line disc dumping
- **Disc Utility** (macOS) - Built-in imaging tool
- **PCSX2** - For verification

## Windows - Using ImgBurn

### Install ImgBurn
1. Download ImgBurn from [imgburn.com](https://www.imgburn.com/)
2. Install the software (free version available)
3. Launch ImgBurn

### Dump a Game
1. **Insert PS2 Game Disc**
   - Place the game disc in your DVD drive
   - Wait for the disc to be recognized

2. **Start ImgBurn**
   - Open ImgBurn
   - Click "Create image file from disc"

3. **Configure Settings**
   - **Source**: Select your DVD drive
   - **Destination**: Choose save location and filename
   - **File Type**: ISO
   - **Layer Break**: None (for single-layer discs)

4. **Start Dumping**
   - Click the disc icon to begin
   - Wait for the process to complete (10-30 minutes)
   - Verify the ISO size (typically 4-8GB)

### Advanced Options
- **Read Speed**: Set to maximum for faster dumping
- **Error Handling**: Enable for scratched discs
- **Verify**: Check "Verify after dump" for data integrity

## Linux - Using DD

### Command-Line Dumping
```bash
# Identify your DVD drive
lsblk

# Dump the disc (replace /dev/sr0 with your drive)
dd if=/dev/sr0 of=game.iso bs=4M status=progress
```

### Using Brasero (GUI)
1. Install Brasero: `sudo apt install brasero`
2. Launch Brasero
3. Select "Disc Copy"
4. Choose "Image File" as destination
5. Select ISO format
6. Start the copy process

## macOS - Using Disc Utility

### Built-in Method
1. **Insert PS2 Game Disc**
2. **Open Disc Utility**
   - Applications > Utilities > Disc Utility

3. **Select the Disc**
   - Choose your PS2 game disc from the sidebar

4. **Create Image**
   - Click "File > New Image > Image from [Disc Name]"
   - Choose "DVD/CD Master" format
   - Select save location
   - Click "Save"

5. **Convert to ISO** (Optional)
   - The result is a .cdr file
   - Convert using: `hdiutil convert game.cdr -format UDTO -o game.iso`

## Verifying Dumps

### Check File Integrity
- **File Size**: Compare with known good dumps
- **MD5/SHA256**: Generate and compare hashes
- **PCSX2 Test**: Try loading in PCSX2

### PCSX2 Verification
1. **Add to Game List**
   - Settings > Game List
   - Add directory containing your ISO

2. **Test Loading**
   - Select game from list
   - Click "Boot"
   - Verify game starts without errors

3. **Basic Gameplay Test**
   - Play for a few minutes
   - Check for graphical/audio issues
   - Verify save functionality

## Handling Different Disc Types

### Single-Layer Discs
- **Capacity**: Up to 4.7GB
- **Dumping**: Standard process
- **Compatibility**: Works with all drives

### Dual-Layer Discs
- **Capacity**: Up to 8.5GB
- **Dumping**: Requires dual-layer compatible drive
- **Layer Break**: Usually not needed for PS2 games

### Special Discs
- **Mixed Mode**: Data + Audio tracks
- **Copy Protection**: Some games have basic protection
- **Multi-Disc Games**: Dump each disc separately

## Organizing Your Collection

### Directory Structure
```
PS2_Games/
├── Action/
│   ├── God_of_War.iso
│   └── God_of_War_2.iso
├── RPG/
│   ├── Final_Fantasy_X.iso
│   └── Shadow_Hearts.iso
└── Sports/
    ├── FIFA_2005.iso
    └── NBA_Street_V3.iso
```

### Naming Conventions
- **Standard**: `Game Title (Region).iso`
- **Examples**:
  - `Final Fantasy X (USA).iso`
  - `Metal Gear Solid 3 (PAL).iso`
  - `Kingdom Hearts (Japan).iso`

### Metadata
- **Region**: NTSC-U, NTSC-J, PAL
- **Disc Count**: For multi-disc games
- **Special Editions**: Include edition info
- **DLC/Content**: Separate folders for extras

## Troubleshooting

### Common Issues

#### Disc Not Recognized
- **Clean the disc**: Remove fingerprints/smudges
- **Try different drive**: Some drives work better
- **Check disc condition**: Scratches may prevent reading

#### Dumping Errors
- **Buffer underrun**: Reduce read speed
- **Read errors**: Enable error correction in software
- **Drive compatibility**: Try different DVD drive

#### ISO Playback Issues
- **Corrupted dump**: Redump the disc
- **Wrong format**: Ensure ISO format (not BIN/CUE)
- **Missing files**: Some games require multiple discs

#### Performance Problems
- **Slow loading**: Use SSD for storage
- **Stuttering**: Check ISO integrity
- **Compatibility issues**: Update PCSX2

### Advanced Troubleshooting

#### Using Alternative Tools
- **DVD Decrypter**: Older but reliable tool
- **ISOBuster**: Good for damaged discs
- **gddrescue** (Linux): Advanced error recovery

#### Hardware Solutions
- **External DVD drive**: Better compatibility
- **Cleaning kit**: For dirty discs
- **Professional repair**: For damaged discs

## Multi-Disc Games

### Handling Multi-Disc Titles
1. **Dump each disc separately**
2. **Name consistently**: `Game_Disc1.iso`, `Game_Disc2.iso`
3. **Configure in PCSX2**: Set up disc swapping

### Disc Swapping
- **Automatic**: PCSX2 can prompt for disc changes
- **Manual**: Use hotkeys to switch discs
- **Save states**: Save before disc changes

## Backup and Archival

### Storage Recommendations
- **Multiple copies**: Keep backups in different locations
- **Redundant storage**: Use RAID or cloud backup
- **Regular verification**: Check file integrity periodically

### Long-term Preservation
- **File formats**: ISO is stable and widely supported
- **Metadata**: Include region, version, and condition info
- **Documentation**: Keep original manuals and artwork

## CodeDesigner Integration

### Using Dumped Games
- **Load in CodeDesigner**: Import ISO for analysis
- **Debug sessions**: Use with PCSX2 debugging
- **Cheat development**: Test on dumped games

### Project Organization
- **Game folders**: Organize by project
- **Version control**: Track changes to dumps
- **Documentation**: Include game info and modifications

## Legal and Ethical Considerations

### Fair Use
- **Personal backups**: Legal in most jurisdictions
- **Private use**: Don't share dumped games
- **Preservation**: Help preserve gaming history

### Distribution
- **No sharing**: Don't distribute ISO files
- **No selling**: Don't sell backups commercially
- **Report issues**: Help improve compatibility lists

## Resources

### Tools
- [ImgBurn](https://www.imgburn.com/) - Windows disc imaging
- [Brasero](https://wiki.gnome.org/Apps/Brasero) - Linux disc burning
- [Disc Utility](https://support.apple.com/guide/mac-help/mchlp1599/mac) - macOS built-in

### Guides
- [PCSX2 Dumping Guide](https://pcsx2.net/docs/setup/dumping)
- [Redump.org](https://redump.org/) - Accurate disc information
- [PS2 Datfile](https://datomatic.no-intro.org/) - Game database

### Community
- [PCSX2 Forums](https://forums.pcsx2.net/Forum-General-Discussion)
- [Reddit r/PS2](https://www.reddit.com/r/PS2/)
- [Emulation General](https://emulation.gametechwiki.com/index.php/Main_Page)

## Conclusion

Dumping your PS2 games to ISO format provides the best experience with PCSX2 and CodeDesigner. Follow these guidelines to ensure high-quality dumps that work reliably for gaming and development purposes.