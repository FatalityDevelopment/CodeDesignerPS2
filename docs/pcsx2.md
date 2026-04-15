[← Back to Documentation Index](README.md)

# PCSX2 Integration — PS2 Emulation and Debugging

PCSX2 is the reference PlayStation 2 emulator that CodeDesigner integrates with for advanced debugging and reverse engineering capabilities.

## Overview

CodeDesigner uses PCSX2 as its primary PS2 emulation backend, connecting via the PINE (PlayStation Interface Network Extension) protocol for real-time debugging, memory inspection, and cheat application.

## Integration Features

### PINE Protocol
- **Real-time connection** to running PCSX2 instances
- **Memory read/write** operations
- **CPU state inspection** (registers, PC, breakpoints)
- **Execution control** (pause, resume, step)
- **Extended opcodes** for pad input and network simulation

### Debugging Workflow
1. **Launch PCSX2** with PINE enabled
2. **Start CodeDesigner** server or VS Code extension
3. **Connect** via socket/PINE bridge
4. **Set breakpoints** and inspect memory
5. **Apply cheats** and analyze behavior

## Setting Up PCSX2

### Requirements
- **PCSX2 v1.7.0+** with PINE support
- **PS2 BIOS** (legitimately obtained)
- **Game ISOs** or disc images

### Configuration
1. **Install PCSX2** from [pcsx2.net](https://pcsx2.net/downloads/)
2. **Enable PINE** in PCSX2 settings:
   - Go to `Config > Advanced > Debug`
   - Check `Enable PINE Server`
   - Set port (default: 28012)
3. **Configure plugins** for your system

### Launch Options
```bash
# Enable PINE with custom port
pcsx2.exe --pine --pine-port=28012

# Load game directly
pcsx2.exe --elf="game.elf" --pine
```

## CodeDesigner Connection

### Via Console App
```bash
# Connect to PCSX2 PINE server
CodeDesigner.Ps2.exe --pine-host=localhost --pine-port=28012
```

### Via VS Code Extension
1. **Open CodeDesigner workspace**
2. **Configure connection** in settings:
   ```json
   {
     "codedesigner.pineHost": "localhost",
     "codedesigner.pinePort": 28012
   }
   ```
3. **Connect** using the extension panel

### Via MCP Bridge
```csharp
var bridge = new PineClient();
bridge.Connect("localhost", 28012);
```

## PINE Protocol Details

### Standard Opcodes (0x00-0x0F)
- `0x00`: Ping/Pong
- `0x01`: Read Memory
- `0x02`: Write Memory
- `0x03`: Execute
- `0x04`: Get Registers
- `0x05`: Set Registers

### Extended Opcodes (0x10-0x3F)
CodeDesigner extends PINE with additional opcodes for:
- **Debugger commands** (breakpoints, stepping)
- **Pad input simulation**
- **Network packet injection**
- **Save state management**

See [PINE Protocol Extension](pine-debugger-extension.md) for complete details.

## Debugging Features

### Breakpoints
- **Address breakpoints** on code execution
- **Memory watchpoints** on read/write access
- **Conditional breakpoints** with register conditions

### Memory Analysis
- **Live disassembly** synchronized with PCSX2
- **Hex viewer** with data type overlay
- **Pointer following** and structure parsing
- **Memory search** and comparison

### Execution Control
- **Single-step** through instructions
- **Run to cursor** functionality
- **Call stack** inspection
- **Function tracing**

## Cheat Application

### Real-time Cheats
- **Apply GameShark codes** during gameplay
- **Persistent cheats** that survive resets
- **Conditional activation** based on game state

### Development Testing
- **Prototype cheats** in CDS language
- **Test on live games** without recompilation
- **Debug cheat effects** with memory inspection

## Performance Considerations

### Optimization
- **Use latest PCSX2** for best performance
- **Configure graphics plugins** appropriately
- **Enable speed hacks** carefully (may affect debugging)

### Debugging Impact
- **PINE overhead** minimal when not actively debugging
- **Breakpoint performance** depends on frequency
- **Memory inspection** may cause frame drops

## Troubleshooting

### Connection Issues
- **Verify PINE enabled** in PCSX2 settings
- **Check firewall** blocking port 28012
- **Confirm PCSX2 version** supports PINE
- **Test connection** with ping command

### Debugging Problems
- **Breakpoints not hitting**: Check address validity
- **Memory reads failing**: Verify game loaded and running
- **Slow performance**: Reduce breakpoint frequency

### Common Errors
- **"PINE server not responding"**: Restart PCSX2 with PINE enabled
- **"Invalid opcode"**: Update to compatible PCSX2 version
- **"Memory access denied"**: Game may not be fully loaded

## Building PCSX2

### From Source
```bash
git clone https://github.com/PCSX2/pcsx2.git
cd pcsx2
cmake -B build -S .
cmake --build build --config Release
```

### Development Builds
- **Nightly builds** available from [pcsx2.net](https://pcsx2.net/downloads/)
- **GitHub Actions** provide automated builds
- **Custom builds** for specific debugging needs

## Compatibility

### Supported Games
- **Most commercial PS2 games** work with varying compatibility
- **Homebrew software** generally excellent support
- **Check compatibility list** at [pcsx2.net/compat/](https://pcsx2.net/compat/)

### Hardware Requirements
- **Modern CPU** (Intel i5/Ryzen 5 minimum)
- **Dedicated GPU** recommended
- **4GB RAM** minimum, 8GB+ recommended

## Advanced Usage

### Custom PINE Servers
- **Implement custom PINE handlers** for specialized debugging
- **Extend protocol** with application-specific opcodes
- **Bridge to other emulators** via PINE compatibility layer

### Integration Testing
- **Automated test suites** using PINE commands
- **Regression testing** across PCSX2 versions
- **Performance benchmarking** with controlled scenarios

## Resources

### Documentation
- [PCSX2 Wiki](https://wiki.pcsx2.net/) - Official documentation
- [PINE Protocol](pine-debugger-extension.md) - CodeDesigner extensions
- [PCSX2 Forums](https://forums.pcsx2.net/) - Community support

### Development
- [PCSX2 GitHub](https://github.com/PCSX2/pcsx2) - Source code
- [Contribution Guide](https://pcsx2.net/docs/contributing/) - How to contribute
- [Discord Server](https://discord.com/invite/TCz3t9k) - Developer chat\[← Back to Documentation Index](README.md)

# PCSX2



!\[Windows Build Status](https://img.shields.io/github/actions/workflow/status/PCSX2/pcsx2/windows\_build\_matrix.yml?label=%F0%9F%96%A5%EF%B8%8F%20Windows%20Builds)

!\[Linux Build Status](https://img.shields.io/github/actions/workflow/status/PCSX2/pcsx2/linux\_build\_matrix.yml?label=%F0%9F%90%A7%20Linux%20Builds)

!\[MacOS Build Status](https://img.shields.io/github/actions/workflow/status/PCSX2/pcsx2/macos\_build\_matrix.yml?label=%F0%9F%8D%8E%20MacOS%20Builds)

\[!\[Codacy Badge](https://app.codacy.com/project/badge/Grade/1f7c0d75fec74d6daa6adb084e5b4f71)](https://app.codacy.com/gh/PCSX2/pcsx2/dashboard?utm\_source=github.com\&utm\_medium=referral\&utm\_content=PCSX2/pcsx2\&utm\_campaign=Badge\_Grade)

\[!\[Discord Server](https://img.shields.io/discord/309643527816609793?color=%235CA8FA\&label=PCSX2%20Discord\&logo=discord\&logoColor=white)](https://discord.com/invite/TCz3t9k)



PCSX2 is a free and open-source PlayStation 2 (PS2) emulator. Its purpose is to emulate the PS2's hardware, using a combination of MIPS CPU \[Interpreters](<https://en.wikipedia.org/wiki/Interpreter\_(computing)>), \[Recompilers](https://en.wikipedia.org/wiki/Dynamic\_recompilation) and a \[Virtual Machine](https://en.wikipedia.org/wiki/Virtual\_machine) which manages hardware states and PS2 system memory. This allows you to play PS2 games on your PC, with many additional features and benefits.



\## Project Details



PCSX2 has been in development for more than 20 years. Past versions could only run a few public domain game demos, but newer versions can run most games at full speed, including popular titles such as Final Fantasy X and Devil May Cry 3. Visit the \[PCSX2 compatibility list](https://pcsx2.net/compat/) to check the latest compatibility status of games (with more than 2500 titles tested).



Installers and binaries for both stable and nightly builds are available from \[our website](https://pcsx2.net/downloads/).



\## System Requirements



PCSX2 supports Windows, Linux, and Mac platforms. Our \[setup documentation page](https://pcsx2.net/docs/setup/requirements) contains additional details on software and hardware requirements.



Please note that a BIOS dump from a legitimately-owned PS2 console is required to use the emulator. For more information, visit \[this page](https://pcsx2.net/docs/setup/bios/).



\## Contributing / Building



PCSX2 supports translation into other languages using \[Crowdin](https://crowdin.com/project/pcsx2-emulator).



See the \[Contribution Guide](https://pcsx2.net/docs/contributing/) for more info on how to contribute.



