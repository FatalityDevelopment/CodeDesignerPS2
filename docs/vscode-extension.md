[← Back to Documentation Index](README.md)

# VS Code Extension — CodeDesigner Frontend

The VS Code extension provides a graphical user interface for CodeDesigner, enabling interactive debugging, code editing, and analysis of PS2 games within Visual Studio Code.

## Overview

The extension integrates CodeDesigner functionality directly into VS Code, providing a seamless development and debugging experience for PS2 homebrew and reverse engineering projects.

## Features

### Disassembler Panel
- **Live disassembly** of PS2 memory
- **Interactive navigation** with click-to-jump
- **Breakpoint management** with visual indicators
- **Memory hex viewer** with search capabilities

### CDS Language Support
- **Syntax highlighting** for .cds files
- **IntelliSense** for cheat script commands
- **Compilation** with error reporting
- **Code snippets** for common patterns

### Cheat Engine Integration
- **Real-time cheat application** during gameplay
- **Cheat database browser** with search
- **Code validation** and testing
- **Batch operations** for multiple cheats

### Memory Analysis Tools
- **Label management** with database integration
- **Pointer scanning** and following
- **Value watching** with change notifications
- **Memory comparison** between sessions

## Installation

### From Marketplace
1. Open VS Code
2. Go to Extensions (Ctrl+Shift+X)
3. Search for "CodeDesigner"
4. Click Install

### From Source
```bash
cd vscode/
npm install
npm run build
code --install-extension codedesigner-*.vsix
```

## Configuration

### Settings
- **Server Host/Port**: CodeDesigner socket server connection
- **Database Path**: Location of labels/cheats database
- **Theme**: Light/dark mode for panels
- **Auto-connect**: Connect to emulator on startup

### Keybindings
- **F5**: Start debugging session
- **F10**: Step over instruction
- **F11**: Step into function
- **Shift+F11**: Step out

## User Interface

### Main Panels

#### Disassembler
- **Address column** with labels
- **Hex dump** of instruction bytes
- **Assembly code** with syntax highlighting
- **Breakpoint gutter** for setting stops

#### Registers
- **GPR view** with 128-bit display
- **COP0 system registers**
- **FPU registers** with float/hex toggle
- **VU0 state** for vector operations

#### Memory
- **Hex editor** with data type overlay
- **String viewer** for ASCII/UTF-8
- **Structure viewer** for parsed data
- **Search/replace** functionality

#### Cheats
- **Code list** with enable/disable
- **Editor** for code modification
- **Tester** for validation
- **Importer** for external formats

### Toolbar
- **Connect/Disconnect** from emulator
- **Load ELF** for analysis
- **Save State** management
- **Settings** access

## CDS Language

### Syntax Features
- **Code blocks** with automatic formatting
- **IntelliSense** for addresses and values
- **Error squiggles** for invalid syntax
- **Hover information** for commands

### Compilation
- **Real-time compilation** as you type
- **Error panel** with detailed messages
- **Output panel** showing generated codes
- **Optimization suggestions**

## Debugging Workflow

### Setup
1. **Start CodeDesigner server**: `dotnet run -- --server`
2. **Open PS2 project** in VS Code
3. **Connect extension** to server
4. **Load game ELF** or start emulator

### Session
1. **Set breakpoints** in disassembler
2. **Run game** until breakpoint hit
3. **Inspect memory/registers**
4. **Apply cheats** for testing
5. **Continue/step** through code

### Analysis
1. **Create labels** for important addresses
2. **Follow pointers** to data structures
3. **Search memory** for values
4. **Export findings** to database

## Extension API

### Commands
- `codedesigner.connect`: Connect to CodeDesigner server
- `codedesigner.disconnect`: Disconnect from server
- `codedesigner.loadElf`: Load ELF file for analysis
- `codedesigner.setBreakpoint`: Set breakpoint at address
- `codedesigner.compileCds`: Compile CDS file

### Events
- `onConnected`: Server connection established
- `onDisconnected`: Server connection lost
- `onBreakpointHit`: Execution paused at breakpoint
- `onMemoryChanged`: Memory write detected

## Integration

### With CodeDesigner Server
- **Socket communication** for real-time data
- **Database synchronization** for labels/cheats
- **State management** for debugging sessions

### With VS Code
- **Native UI** using webview panels
- **Workspace integration** for project files
- **Task provider** for build operations
- **Debug protocol** for stepping

### With External Tools
- **PCSX2 integration** via PINE protocol
- **Cheat databases** import/export
- **Save file management**
- **Memory snapshot** sharing

## Development

### Project Structure
```
vscode/
├── src/
│   ├── extension.ts          # Main extension entry
│   ├── providers/
│   │   ├── disasmPanel.ts    # Disassembler panel
│   │   └── cdsLanguage.ts    # CDS language support
│   └── utils/
│       └── serverClient.ts   # Server communication
├── package.json              # Extension manifest
└── webpack.config.js         # Build configuration
```

### Building
```bash
npm install
npm run build
npm run package  # Create .vsix file
```

### Testing
```bash
npm run test
npm run test-ui  # UI tests
```

## Troubleshooting

### Connection Issues
- **Server not running**: Start CodeDesigner with `--server`
- **Port mismatch**: Check settings for correct port
- **Firewall blocking**: Allow VS Code network access

### Performance Problems
- **Large memory views**: Limit display range
- **Frequent updates**: Reduce refresh rate
- **Memory leaks**: Restart VS Code

### Feature Not Working
- **Check version**: Ensure latest extension
- **Server compatibility**: Match CodeDesigner version
- **Logs**: Check VS Code developer console

## Examples

### Basic Debugging
1. Connect to running emulator
2. Set breakpoint at function start
3. Run until breakpoint
4. Inspect register values
5. Step through instructions

### Cheat Development
1. Open .cds file
2. Write cheat script
3. Compile and test
4. Apply to running game
5. Refine and save

### Memory Analysis
1. Search for specific value
2. Follow pointer chain
3. Create labels for findings
4. Export to database

## Best Practices

### Workflow
- **Organize projects** in workspace folders
- **Use version control** for CDS files
- **Document findings** with labels
- **Test cheats thoroughly**

### Performance
- **Limit panel updates** during intensive debugging
- **Use filters** in memory searches
- **Close unused panels** to save resources

### Maintenance
- **Keep extension updated** for new features
- **Backup databases** regularly
- **Report issues** with detailed logs</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\vscode-extension.md