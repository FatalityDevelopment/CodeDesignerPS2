# Quick Start Guide: Using the MIPS Assembler and Disassembler

This guide will help you get started with the MIPS assembler and disassembler integration in VSCode.

## Prerequisites

- .NET 8.0 SDK installed
- Node.js and npm installed
- VSCode installed

## Setup

### 1. Build the C# Backend

```bash
cd CodeDesigner.Mips64LE
dotnet build
```

### 2. Start the Emulator

```bash
dotnet run -- --run
```

You should see:
```
MIPS64 Emotion Engine - CodeDesigner

Socket server started on port 8765
Waiting for VSCode extension to connect...
```

Keep this terminal open — the emulator must be running for the extension to work. The VS Code extension can also start the emulator automatically via its **Connect** button.

### 3. Install and Run the VSCode Extension

Open the project in VSCode and press `F5` to launch the Extension Development Host.

Alternatively, build and install the extension:

```bash
cd vscode/codedesigner
npm install
npm run compile
```

## Using the Assembler

1. **Open the Assembler Panel**
   - Click on the CodeDesigner icon in the VSCode sidebar
   - The Assembler panel should appear

2. **Connect to the Server**
   - Press `Ctrl+Shift+P` (or `Cmd+Shift+P` on Mac)
   - Type "CodeDesigner: Connect to Server"
   - Press Enter
   - You should see "Connected to C# server"

3. **Assemble Code**
   - In the Assembler panel, enter MIPS assembly code in the text area:
   ```
   add $t0, $s0, $s1
   addi $t1, $t0, 100
   j 0x400000
   ```
   - Click the "Assemble" button
   - The hex output will appear in the bottom text area:
   ```
   0x02114020  # add $t0, $s0, $s1
   0x21090064  # addi $t1, $t0, 100
   0x08100000  # j 0x400000
   ```

## Using the Disassembler

1. **Open the Disassembler Panel**
   - The Disassembler panel should be in the sidebar alongside the Assembler

2. **Disassemble Code**
   - Enter hex codes in the text area (space or newline separated):
   ```
   0x02114020
   0x21090064
   0x08100000
   ```
   - Click the "Disassemble" button
   - The assembly output will appear:
   ```
   0x02114020  add t0, s0, s1
   0x21090064  addi t1, t0, $0064
   0x08100000  j $00400000
   ```

## Examples

### Example 1: Basic Arithmetic

**Assembly Input:**
```
add $t0, $s0, $s1
sub $t1, $t0, $s2
mult $s0, $s1
mflo $t2
```

**Hex Output:**
```
0x02114020  # add $t0, $s0, $s1
0x01124822  # sub $t1, $t0, $s2
0x02110018  # mult $s0, $s1
0x00005012  # mflo $t2
```

### Example 2: Load/Store Operations

**Assembly Input:**
```
lw $t0, 100, $sp
sw $t1, 200, $sp
lb $t2, 4, $s0
sb $t3, 8, $s1
```

**Hex Output:**
```
0x8FA80064  # lw $t0, 100, $sp
0xAFA90064  # sw $t1, 200, $sp
0x82020004  # lb $t2, 4, $s0
0xA22B0008  # sb $t3, 8, $s1
```

### Example 3: Branches and Jumps

**Assembly Input:**
```
beq $t0, $t1, 0x10
bne $t2, $t3, 0x20
j 0x400000
jal 0x500000
jr $ra
```

**Hex Output:**
```
0x11090010  # beq $t0, $t1, 0x10
0x154B0020  # bne $t2, $t3, 0x20
0x08100000  # j 0x400000
0x0C140000  # jal 0x500000
0x03E00008  # jr $ra
```

## Troubleshooting

### "Not connected to C# server"

**Solution:** 
1. Make sure the emulator is running (`dotnet run -- --run`), or use the extension's **Connect** button
2. Use the "Connect to Server" command in VSCode
3. Check that port 8765 is not blocked by firewall

### Assembly/Disassembly fails

**Solution:**
1. Check the syntax of your instructions (see ASSEMBLER_DISASSEMBLER_INTEGRATION.md for supported formats)
2. Make sure register names are valid (e.g., `$t0`, `$s0`, `$ra`)
3. Check that immediate values are in the correct range
4. For hex input, ensure values start with `0x` or are valid hexadecimal

### Server crashes or hangs

**Solution:**
1. Stop the server (press 'q' or Ctrl+C)
2. Check for error messages in the console
3. Restart the server
4. Try reconnecting from VSCode

## Using Breakpoints

The Live Disassembler lets you pause EE execution at any instruction address and inspect state, without modifying the program.

### Step 1 — Ensure the emulator is running

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --run
```

The emulator must be running and the VSCode extension must be connected before breakpoints work.

### Step 2 — Open a Live Memory panel

A Live Memory disassembler panel opens automatically when you start a live session through the extension.  You can also run **CodeDesigner: Open Live Disassembler** from the Command Palette.

### Step 3 — Set a breakpoint

1. Use the arrow keys to navigate to the instruction you want to break at.
2. Press **F9** to toggle a breakpoint on the highlighted row.
   - The row is highlighted to indicate an active breakpoint.
   - Press F9 again to remove it.

### Step 4 — Wait for the hit

The panel polls the emulator status every 800 ms.  When the EE program counter reaches the breakpoint address:

- The emulator pauses automatically.
- The panel scrolls to the hit instruction.
- A **PAUSED** indicator appears in the status bar.

### Step 5 — Resume

Press **F5** (or click the *Resume* button) to continue execution.

### Using breakpoints via the socket protocol

You can also manage breakpoints programmatically from any TCP client on port 8765:

```json
// Set a breakpoint at 0x80001000
{ "Command": "SETBP", "BreakpointAddress": 2147487744 }

// Poll until paused
{ "Command": "GETPC" }
// Response: { "Success": true, "Paused": true, "PC": 2147487744, "BreakpointHitAddress": 2147487744 }

// Resume
{ "Command": "RESUME" }

// Remove the breakpoint
{ "Command": "CLEARBP", "BreakpointAddress": 2147487744 }
```

See [SOCKET_COMMUNICATION.md](SOCKET_COMMUNICATION.md) for the full command reference.

---

## Tips

1. **Register Formats**: You can use any of these formats:
   - `$t0`, `t0` (named registers)
   - `$8`, `8` (numeric registers)
   - `$r8`, `r8` (r-prefix registers)

2. **Number Formats**: Immediate values support:
   - Decimal: `100`, `-50`
   - Hexadecimal: `0x1234`, `0xFF`

3. **Comments**: Use `#` for comments in assembly code:
   ```
   add $t0, $s0, $s1  # Add s0 and s1
   ```

4. **Multiple Lines**: Enter one instruction per line for assembly, or space-separate hex codes for disassembly

5. **Error Messages**: If an instruction fails, the error message will tell you what went wrong

## Next Steps

- Read the full documentation in `ASSEMBLER_DISASSEMBLER_INTEGRATION.md`
- Explore the supported instruction set
- Try assembling/disassembling your own MIPS programs
- Check the emulator API in `CodeDesigner.Mips64LE.Common/Emulator/` for loading and running ELF/IRX images

## Support

For issues or questions:
1. Check the troubleshooting section above
2. Review the comprehensive documentation
3. Check the server console for error messages
4. Look at the VSCode Developer Tools console (Help → Toggle Developer Tools)
