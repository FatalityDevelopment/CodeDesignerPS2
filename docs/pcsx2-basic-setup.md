[← Back to Documentation Index](README.md)

# PCSX2 Setup Guide — Basic Setup

This guide covers the basic setup and installation of PCSX2, the PlayStation 2 emulator that CodeDesigner integrates with.

## System Requirements

### Minimum Requirements
- **Operating System**: Windows 10 (64-bit), Linux (Ubuntu 22.04+), or macOS 11.0+
- **CPU**: SSE4.1 support, PassMark single-thread rating ≥ 1500
- **GPU**: Direct3D11/OpenGL 3.3/Vulkan 1.1 support, 2GB VRAM
- **RAM**: 8GB system memory
- **Storage**: 10GB free space

### Recommended Requirements
- **Operating System**: Windows 11, Linux (latest), or macOS (latest)
- **CPU**: PassMark single-thread rating ≥ 2000
- **GPU**: Modern GPU with 4GB+ VRAM
- **RAM**: 16GB system memory
- **Storage**: SSD with 20GB+ free space

## Installation

### Download PCSX2
1. Visit [pcsx2.net/downloads](https://pcsx2.net/downloads/)
2. Download the latest stable release for your platform
3. Extract/install the emulator

### First-Time Setup
1. Launch PCSX2
2. Follow the first-time setup wizard
3. Configure your controller (optional)
4. Set up display settings

## BIOS Setup

### Why You Need a BIOS
PCSX2 requires a PlayStation 2 BIOS to function. The BIOS contains essential system code that games rely on.

### Dumping Your PS2 BIOS
1. Download the BIOS dumper from [pcsx2.net/docs/setup/bios](https://pcsx2.net/docs/setup/bios)
2. Follow the instructions to dump BIOS from your PS2 console
3. Copy the dumped files to `PCSX2/bios/` directory

### BIOS File Requirements
- `SCPH-XXXXX.bin` (main BIOS file)
- May include additional `.bin` and `.rom` files
- File size typically 4-8 MB total

## Game Setup

### Playing from Disc
1. Insert PS2 game disc
2. Launch PCSX2
3. Select `System > Boot CD/DVD`
4. Choose your disc drive

### Playing from ISO
1. Dump your PS2 game to ISO format
2. Place ISO files in a dedicated games folder
3. In PCSX2: `Settings > Game List`
4. Add your games directory
5. Select game from the list and click play

## Configuration

### Basic Settings
- **Graphics**: Choose renderer (Vulkan recommended)
- **Audio**: Select audio backend
- **Controller**: Configure input devices
- **System**: Set region and BIOS

### Performance Settings
- **EE Cyclerate**: Adjust for speed vs compatibility
- **VU Cycle Stealing**: Fine-tune VU performance
- **Speedhacks**: Enable for better performance (may reduce compatibility)

### Display Settings
- **Aspect Ratio**: 4:3, 16:9, or Widescreen patches
- **Resolution**: Internal resolution scaling
- **Anti-aliasing**: FXAA, MSAA options
- **Texture Filtering**: Anisotropic filtering

## Controller Setup

### Keyboard Controls
- Configure basic controls in `Settings > Controllers`
- Use keyboard mapping for simple gameplay

### Gamepad Setup
1. Connect your controller
2. Select `Settings > Controllers`
3. Choose your controller from the dropdown
4. Map buttons to PS2 controls
5. Test configuration

### Advanced Input
- **LilyPad plugin**: Advanced controller configuration
- **Multiple controllers**: Support for multiplayer games
- **Rumble support**: Force feedback configuration

## Troubleshooting

### Common Issues

#### PCSX2 Won't Start
- Ensure you have a 64-bit operating system
- Check graphics drivers are up to date
- Verify .NET Framework is installed (Windows)

#### BIOS Not Recognized
- Confirm BIOS files are in the correct directory
- Check file permissions
- Verify BIOS is from a legitimate PS2 console

#### Games Run Slowly
- Lower internal resolution
- Enable speedhacks in advanced settings
- Close background applications
- Check CPU/GPU temperatures

#### Graphics Issues
- Try different graphics renderers
- Adjust shader settings
- Disable anti-aliasing if problematic
- Update graphics drivers

#### Audio Problems
- Change audio backend in settings
- Adjust synchronization settings
- Check volume levels

### Getting Help
- Check the [PCSX2 compatibility list](https://pcsx2.net/compat/)
- Search the [PCSX2 forums](https://forums.pcsx2.net/)
- Join the [PCSX2 Discord](https://pcsx2.net/discord)
- Report bugs on [GitHub](https://github.com/PCSX2/pcsx2/issues)

## Advanced Setup

### Portable Installation
- Extract PCSX2 to a USB drive
- All settings and BIOS stored locally
- Useful for multiple computers

### Custom Configurations
- Create per-game settings
- Use command-line options for automation
- Configure multiple controller profiles

### Performance Optimization
- Use the latest development builds
- Experiment with different speedhack combinations
- Monitor performance with built-in tools

## Next Steps

Once PCSX2 is set up, you can:
- [Configure CodeDesigner integration](pcsx2.md) for debugging
- Explore [advanced configuration options](configuration-guide.md)
- Learn about [troubleshooting common issues](troubleshooting.md)
- Set up [development builds](building-pcsx2.md) for testing