[← Back to Documentation Index](README.md)

# PCSX2 Troubleshooting Guide

This guide helps diagnose and resolve common PCSX2 issues, with specific focus on CodeDesigner integration problems.

## Issue Reporting

### Before Reporting
- **Update PCSX2**: Use latest development build
- **Check compatibility**: Verify game compatibility
- **Test settings**: Try default configuration
- **Search existing issues**: Check forums and GitHub

### How to Report Issues
1. **Gather information**:
   - PCSX2 version and build date
   - Operating system and version
   - Hardware specifications
   - Game serial number and region

2. **Describe the problem**:
   - What happens vs what should happen
   - Steps to reproduce
   - Frequency of occurrence
   - Any error messages

3. **Provide logs**:
   - Enable logging in PCSX2
   - Capture console output
   - Include system information

### Reporting Channels
- [PCSX2 Forums Bug Reports](https://forums.pcsx2.net/Forum-Bug-reporting)
- [GitHub Issues](https://github.com/PCSX2/pcsx2/issues)
- [PCSX2 Discord](https://pcsx2.net/discord)

## Common Issues and Solutions

### PCSX2 Won't Start

#### Symptoms
- PCSX2 fails to launch
- Error messages on startup
- Crashes immediately

#### Solutions
1. **Check system requirements**:
   - Verify 64-bit operating system
   - Ensure SSE4.1 CPU support
   - Check graphics driver compatibility

2. **Dependency issues**:
   - Install Visual C++ Redistributables (Windows)
   - Update .NET Framework
   - Install required system libraries (Linux)

3. **File corruption**:
   - Delete PCSX2 settings folder
   - Reinstall PCSX2
   - Check antivirus exclusions

### BIOS Problems

#### BIOS Not Recognized
- **File location**: Ensure BIOS in correct directory
- **File permissions**: Check read access
- **File integrity**: Verify dump is not corrupted
- **File format**: Use .bin format

#### BIOS Checksum Errors
- **Redump BIOS**: Original dump corrupted
- **Different method**: Try alternative dumping tool
- **Console issues**: PS2 hardware problems

#### Region Mismatch
- **Use correct BIOS**: Match game region
- **Region patching**: Some games support patches
- **Multi-region BIOS**: Limited compatibility

### Game Loading Issues

#### Game Won't Boot
- **BIOS compatibility**: Test with different BIOS
- **Game format**: Ensure proper ISO dump
- **Disc image**: Verify ISO integrity
- **Region lock**: Check game/BIOS region match

#### Crashes on Boot
- **Disable speedhacks**: Test with defaults
- **Change renderer**: Try different graphics API
- **Update drivers**: Latest graphics drivers
- **Memory issues**: Check system RAM

#### Freezes or Hangs
- **EE cyclerate**: Adjust CPU settings
- **Game fixes**: Enable/disable specific fixes
- **Plugin compatibility**: Update or change plugins

### Performance Problems

#### Low Frame Rate
- **CPU bottleneck**: Lower internal resolution
- **GPU bottleneck**: Reduce anti-aliasing
- **Speedhacks**: Enable appropriate hacks
- **Background processes**: Close unnecessary apps

#### Stuttering/Audio Issues
- **VSync settings**: Adjust synchronization
- **Audio backend**: Change audio system
- **Buffer sizes**: Adjust audio buffers
- **Thread priority**: Set PCSX2 to high priority

#### High CPU Usage
- **MTVU**: Enable multi-threaded VU
- **Speedhacks**: Enable INTC spin detection
- **Resolution**: Lower internal resolution
- **Shaders**: Disable heavy shader effects

### Graphics Issues

#### Graphical Glitches
- **Renderer change**: Try Vulkan/OpenGL/D3D
- **Driver updates**: Latest graphics drivers
- **Shader options**: Adjust shader settings
- **Game fixes**: Enable graphics fixes

#### Missing Textures
- **Texture cache**: Clear shader cache
- **Mipmapping**: Adjust texture filtering
- **VRAM issues**: Lower resolution or disable AA
- **Plugin settings**: Adjust GS plugin options

#### Screen Tearing
- **VSync**: Enable vertical synchronization
- **Frame limiter**: Ensure proper frame rate
- **Display settings**: Match monitor refresh rate

### Audio Problems

#### No Sound
- **Audio backend**: Change audio system
- **Driver issues**: Update audio drivers
- **Volume settings**: Check PCSX2 volume
- **System mixer**: Verify audio device

#### Audio Crackling
- **Buffer size**: Increase audio buffer
- **Synchronization**: Adjust audio sync settings
- **Time stretching**: Enable audio stretching
- **CPU usage**: Reduce system load

#### Audio Delay
- **Latency settings**: Adjust audio latency
- **Buffer size**: Optimize buffer settings
- **Async mixing**: Enable async audio mixing

### Controller Issues

#### Controller Not Recognized
- **Driver installation**: Install controller drivers
- **Controller settings**: Configure in PCSX2
- **USB ports**: Try different USB ports
- **Power management**: Disable USB power saving

#### Input Lag
- **Polling rate**: Adjust controller polling
- **Buffer settings**: Optimize input buffers
- **VSync**: Disable if causing input delay
- **Exclusive mode**: Enable exclusive controller access

#### Button Mapping Problems
- **Clear configuration**: Reset controller settings
- **Test inputs**: Use PCSX2 controller test
- **Multiple controllers**: Check device conflicts
- **Software conflicts**: Close other controller software

### Memory Card Issues

#### Save Files Not Loading
- **Card format**: Check memory card format
- **File corruption**: Repair or recreate card
- **Permissions**: Check file access rights
- **Path issues**: Verify card file location

#### Save Corruption
- **Card type**: Use folder memory cards
- **Backup saves**: Regular backup routine
- **Clean shutdown**: Always exit PCSX2 properly
- **File system**: Check for file system errors

### Networking Issues (PS2 Online)

#### Connection Problems
- **Network configuration**: Set up PS2 network settings
- **Port forwarding**: Configure router ports
- **Firewall**: Allow PCSX2 network access
- **DNS settings**: Use correct DNS servers

#### Lag and Disconnects
- **Network adapter**: Use wired connection
- **MTU settings**: Adjust packet size
- **Server compatibility**: Check server status
- **Region settings**: Match game region

## CodeDesigner-Specific Issues

### PINE Connection Problems

#### Cannot Connect
- **PINE enabled**: Verify PINE server enabled in PCSX2
- **Port configuration**: Check port settings match
- **Firewall**: Allow connections on PINE port
- **PCSX2 running**: Ensure PCSX2 is started first

#### Connection Drops
- **Network stability**: Check network connection
- **Resource usage**: Monitor system resources
- **Debug logging**: Enable PINE debug logs
- **Version compatibility**: Match PCSX2/CodeDesigner versions

### Debugging Issues

#### Breakpoints Not Working
- **Address validity**: Verify breakpoint addresses
- **Game loaded**: Ensure game is running
- **PINE commands**: Check command syntax
- **Memory protection**: Some areas may be protected

#### Memory Read/Write Errors
- **Address range**: Check valid memory addresses
- **Game state**: Ensure game is in correct state
- **Timing issues**: Memory may not be accessible
- **Cache issues**: Clear PCSX2 caches

#### Performance Degradation
- **Debug overhead**: Debugging reduces performance
- **Log levels**: Reduce logging verbosity
- **Breakpoint count**: Limit number of breakpoints
- **Memory inspection**: Reduce inspection frequency

### Cheat Problems

#### Cheats Not Working
- **Code format**: Verify cheat code format
- **Address validity**: Check memory addresses
- **Timing**: Apply cheats at correct time
- **Game version**: Match game version

#### Cheat Conflicts
- **Multiple cheats**: Disable conflicting codes
- **Memory overlap**: Check for address conflicts
- **Game updates**: Verify compatibility with game patches

## Advanced Troubleshooting

### Log Analysis
- **Enable logging**: Set appropriate log levels
- **Log location**: Find log files in PCSX2 directory
- **Error patterns**: Look for common error messages
- **Timing information**: Check timestamps for issues

### System Monitoring
- **Resource usage**: Monitor CPU/GPU/RAM
- **Temperature**: Check component temperatures
- **Disk I/O**: Monitor storage performance
- **Network traffic**: Check network usage

### Clean Installation
1. **Uninstall PCSX2**
2. **Delete settings folder**
3. **Remove registry entries** (Windows)
4. **Reinstall PCSX2**
5. **Test with defaults**

### Compatibility Testing
- **Different PCSX2 versions**: Test with older/newer builds
- **Different settings**: Try various configurations
- **Different hardware**: Test on different systems
- **Clean environment**: Test in safe mode

## Getting Help

### Community Resources
- [PCSX2 Forums](https://forums.pcsx2.net/) - Main support community
- [PCSX2 Wiki](https://wiki.pcsx2.net/) - Detailed documentation
- [PCSX2 Discord](https://pcsx2.net/discord) - Real-time help
- [Reddit r/PCSX2](https://www.reddit.com/r/PCSX2/) - User experiences

### Official Support
- [GitHub Issues](https://github.com/PCSX2/pcsx2/issues) - Bug reports
- [Compatibility List](https://pcsx2.net/compat/) - Game compatibility
- [Documentation](https://pcsx2.net/docs/) - Official guides

### Professional Help
- **Paid support**: Some users offer consulting
- **Developer access**: Direct developer communication
- **Commercial support**: Enterprise support options

## Prevention

### Regular Maintenance
- **Keep updated**: Use latest PCSX2 builds
- **Backup settings**: Regular configuration backups
- **System updates**: Keep OS and drivers updated
- **Clean installation**: Periodic clean reinstalls

### Best Practices
- **Test configurations**: Verify settings work
- **Document issues**: Keep records of problems/solutions
- **Share knowledge**: Help other users with similar issues
- **Report bugs**: Contribute to project improvement

## Conclusion

Most PCSX2 issues can be resolved with proper troubleshooting. Start with basic checks, then progressively try more advanced solutions. When reporting issues, provide complete information to help developers assist you effectively.