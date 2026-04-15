[← Back to Documentation Index](README.md)

# PCSX2 Configuration Guide

This comprehensive guide covers all PCSX2 configuration options for optimal performance with CodeDesigner integration.

## Overview

PCSX2 offers extensive configuration options to balance performance, compatibility, and accuracy. This guide explains each setting and its impact on emulation.

## Accessing Configuration

### Main Menu
- Launch PCSX2
- Select `Config > Settings` or press `Ctrl + ,`

### Per-Game Settings
- Right-click game in game list
- Select `Properties`
- Configure game-specific overrides

## System Settings

### BIOS Configuration
- **BIOS File**: Select dumped PS2 BIOS
- **Fast Boot**: Skip BIOS animation for faster startup
- **Enable Host Filesystem**: Allow access to PC files (advanced)

### CPU Configuration
- **EE Cyclerate**: Emotion Engine clock speed
  - **-3 to +3**: Adjust for compatibility vs performance
  - **Lower values**: Better compatibility, slower performance
  - **Higher values**: Better performance, potential glitches

- **VU Cycle Stealing**: Vector Unit performance adjustment
  - **0-3**: Higher values improve performance
  - **May cause graphical issues**: Test carefully

### GS Configuration (Graphics Synthesizer)
- **Renderer**: Graphics API selection
  - **Vulkan**: Best performance on modern GPUs
  - **Direct3D 11/12**: Good Windows compatibility
  - **OpenGL**: Cross-platform compatibility

- **Internal Resolution**: Rendering resolution
  - **Native (640x448)**: Fastest, lowest quality
  - **2x-8x Native**: Balance quality and performance
  - **Custom**: Specific resolutions

- **Anisotropic Filtering**: Texture sharpness
  - **Off to 16x**: Higher values improve texture quality
  - **Performance impact**: Minimal on modern GPUs

- **Anti-aliasing**: Edge smoothing
  - **None**: Sharp edges, best performance
  - **FXAA/MSAA**: Smoother edges, performance cost

## Display Settings

### Aspect Ratio
- **4:3**: Original PS2 aspect ratio
- **16:9**: Widescreen (may crop or stretch)
- **Stretch**: Fill screen (distorts image)
- **Custom**: User-defined ratio

### Screen Filters
- **Bilinear Filtering**: Smooth scaled graphics
- **Trilinear Filtering**: Better texture filtering
- **Edge Anti-aliasing**: Reduce jagged edges

### VSync
- **Synchronize**: Prevent screen tearing
- **Adaptive**: Match display refresh rate
- **Off**: Maximum performance, may tear

## Audio Settings

### Audio Backend
- **Cubeb**: Cross-platform, recommended
- **XAudio2**: Windows-specific
- **PortAudio**: Alternative cross-platform

### Synchronization
- **Async Mix**: Reduce audio latency
- **Time Stretching**: Prevent audio crackling
- **Audio Expansion**: Surround sound support

### Volume Controls
- **Main Volume**: Overall audio level
- **Reverb**: Sound effect intensity
- **Per-channel**: Individual volume controls

## Controller Settings

### Input Device
- **Keyboard**: Basic keyboard controls
- **Gamepad**: Modern controller support
- **LilyPad**: Advanced controller configuration

### Button Mapping
- **Face Buttons**: △ □ × ○
- **Shoulders**: L1 L2 R1 R2
- **Analog Sticks**: Left/Right stick configuration
- **D-pad**: Directional controls

### Advanced Features
- **Rumble**: Force feedback support
- **Pressure Sensitivity**: Analog button pressure
- **Multiple Controllers**: Multiplayer support

## Memory Card Settings

### Memory Card Type
- **Folder**: Modern, reliable
- **File**: Traditional .ps2 format
- **Per-game**: Isolated saves per game

### Auto-eject
- **Enabled**: Automatically eject cards on exit
- **Disabled**: Keep cards inserted

### Shared Cards
- **Cross-game saves**: Share saves between games
- **Isolated saves**: Separate saves per game

## Speedhack Settings

### Recommended Speedhacks
- **EE Cyclerate**: -1 (balance compatibility/performance)
- **VU Cycle Stealing**: 1 (slight performance boost)
- **Enable INTC Spin Detection**: Reduces CPU usage
- **Enable Wait Loop Detection**: Improves idle performance

### Advanced Speedhacks
- **mVU Flag Hack**: Vector unit optimization
- **MTVU**: Multi-threaded VU processing
- **Instant VU1**: Skip VU1 synchronization

### Caution
- **Test thoroughly**: Speedhacks may cause glitches
- **Per-game basis**: Different games need different settings
- **Compatibility first**: Disable if experiencing issues

## Game Fixes

### Automatic Game Fixes
- **Enabled by default**: Fixes applied automatically
- **Per-game database**: Specific fixes for known games
- **Override manually**: Force enable/disable fixes

### Common Game Fixes
- **FPU Compare Hack**: Fixes floating-point comparisons
- **VU Add/Sub Hack**: Vector unit arithmetic fixes
- **Triangle Culling**: Graphics rendering corrections

## Advanced Settings

### Logging
- **EE Console**: Emotion Engine debug output
- **IOP Console**: I/O Processor debug output
- **GS Console**: Graphics Synthesizer debug output

### Developer Options
- **Enable GS Dump**: Save graphics data
- **Enable CDVD Dump**: Save disc read data
- **Save State Compression**: Balance size vs speed

### Plugin Settings
- **GS Plugin**: Graphics renderer configuration
- **SPU2 Plugin**: Audio processing settings
- **CDVD Plugin**: Disc drive emulation
- **USB Plugin**: USB device support

## Per-Game Configuration

### Creating Game Profiles
1. Right-click game in PCSX2
2. Select `Game Properties`
3. Configure custom settings
4. Save profile

### Profile Management
- **Inherit global settings**: Use as base
- **Override specific options**: Customize per game
- **Share profiles**: Export/import settings

### Common Overrides
- **Speedhacks**: Different settings per game
- **Resolution**: Higher settings for less demanding games
- **Audio settings**: Game-specific audio fixes

## CodeDesigner Integration

### Debug Configuration
- **Enable PINE**: Required for CodeDesigner connection
- **PINE Port**: Configure connection port
- **Log Level**: Increase for debugging

### Memory Analysis
- **Save states**: Compatible with CodeDesigner
- **Memory dumps**: Enable for analysis
- **Breakpoint support**: Full integration

### Cheat Development
- **Raw cheats**: Test in PCSX2 before CodeDesigner
- **Save state testing**: Quick iteration
- **Compatibility verification**: Ensure cheats work

## Performance Optimization

### General Tips
- **Latest PCSX2**: Use development builds for best performance
- **Graphics drivers**: Keep updated
- **Background processes**: Close unnecessary applications

### CPU Optimization
- **High-performance power plan**: Windows power settings
- **Core parking**: Disable for consistent performance
- **Thermal management**: Monitor temperatures

### GPU Optimization
- **Vulkan API**: Generally best performance
- **Shader pre-caching**: Enable for faster loading
- **VRAM management**: Monitor memory usage

## Troubleshooting Configuration

### Performance Issues
- **Reset to defaults**: Start fresh configuration
- **Disable speedhacks**: Test baseline performance
- **Check temperatures**: Prevent thermal throttling

### Compatibility Problems
- **Game fixes**: Try different combinations
- **Plugin versions**: Update or downgrade
- **BIOS version**: Test different BIOS dumps

### Audio/Video Issues
- **Renderer change**: Try different graphics backends
- **Audio backend**: Switch audio systems
- **Driver updates**: Latest graphics/audio drivers

## Backup and Restore

### Configuration Backup
- **Settings folder**: Backup entire PCSX2 settings directory
- **Per-game configs**: Export individual game settings
- **Input profiles**: Save controller configurations

### Reset Options
- **Reset all settings**: Return to defaults
- **Selective reset**: Reset specific sections
- **Profile deletion**: Remove custom game settings

## Advanced Configuration Files

### PCSX2.ini
Main configuration file containing all settings. Located in:
- **Windows**: `%APPDATA%\PCSX2\`
- **Linux**: `~/.config/PCSX2/`
- **macOS**: `~/Library/Application Support/PCSX2/`

### Manual Editing
- **Text editor**: Edit with caution
- **Backup first**: Always create backups
- **Syntax**: Standard INI file format

## Resources

### Official Documentation
- [PCSX2 Wiki](https://wiki.pcsx2.net/) - Detailed configuration guides
- [Configuration PDF](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/Docs/Configuration_Guide.pdf) - Original guide
- [Forum Guides](https://forums.pcsx2.net/Forum-Guides-and-Tutorials) - Community tutorials

### Community Resources
- [PCSX2 Discord](https://pcsx2.net/discord) - Real-time help
- [Reddit r/PCSX2](https://www.reddit.com/r/PCSX2/) - User experiences
- [GitHub Issues](https://github.com/PCSX2/pcsx2/issues) - Bug reports

## Conclusion

Proper PCSX2 configuration is essential for optimal performance with CodeDesigner. Start with conservative settings and gradually optimize based on your hardware and specific game requirements. Always test changes thoroughly and maintain backups of working configurations.