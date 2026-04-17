[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.TestProject — Unit Test Suite

The `CodeDesigner.Ps2.TestProject` contains the comprehensive unit test suite for CodeDesigner, with over 1000 tests covering instruction accuracy, emulation correctness, and integration functionality.

## Overview

This test project ensures CodeDesigner components work correctly and maintain compatibility with PS2 hardware behavior. Tests are written using xUnit and cover all major functionality.

## Test Categories

### Instruction Tests
- **Encoding/Decoding**: Verify assembler/disassembler accuracy
- **Execution**: Test instruction behavior against PCSX2 reference
- **Edge Cases**: Boundary conditions and error handling

### Emulation Tests
- **CPU Execution**: Register state changes and flag updates
- **Memory Access**: Load/store operations and address translation
- **Exception Handling**: Interrupt and trap processing

### Integration Tests
- **ELF Loading**: Executable parsing and memory mapping
- **Hardware Peripherals**: Timer, DMA, and I/O simulation
- **Game Compatibility**: Real game execution verification

## Running Tests

### Command Line

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "InstructionTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Visual Studio
- Open Test Explorer
- Run all or selected tests
- View detailed results and debugging

## Test Structure

### Test Classes

#### Instruction Coverage
- `TestFullInstructionCoverage.cs`: Tests all MIPS instructions
- `TestPS2Instructions.cs`: PS2-specific instruction verification
- `TestFPUInstructions.cs`: Floating-point unit tests
- `TestMMIInstructions.cs`: Multimedia instruction tests

#### Emulation Tests
- `TestCpuExecution.cs`: CPU state changes
- `TestMemoryBus.cs`: Memory access patterns
- `TestExceptionHandling.cs`: Interrupt processing

#### Integration Tests
- `TestElfLoader.cs`: ELF file parsing
- `TestHardwarePeripherals.cs`: Peripheral simulation
- `TestGameCompatibility.cs`: Real game testing

### Test Data
- **Reference binaries** from PCSX2
- **Test ELF files** with known behavior
- **Memory dumps** from real hardware
- **Cheat code databases** for validation

## Writing Tests

### Basic Test Structure

```csharp
[Fact]
public void TestAddInstruction() {
    var cpu = new CpuState();
    cpu.Regs[1] = 5;  // $a0 = 5
    cpu.Regs[2] = 10; // $a1 = 10
    
    var add = new AddInstruction {
        Rs = 1, Rt = 2, Rd = 3
    };
    add.Exec(cpu);
    
    Assert.Equal(15, cpu.Regs[3]); // $v1 should be 15
}
```

### Parameterized Tests

```csharp
[Theory]
[InlineData(0x00000000, "nop")]
[InlineData(0x0000000D, "break")]
public void TestDisassemble(uint opcode, string expected) {
    var decoder = new InstructionDecoder();
    var instr = decoder.Decode(opcode);
    Assert.Equal(expected, instr.Disassemble());
}
```

### Integration Tests

```csharp
[Fact]
public void TestElfExecution() {
    var emulator = new PS2Emulator();
    emulator.LoadElf("test.elf");
    
    // Execute until completion
    while (!emulator.Halted) {
        emulator.Step();
    }
    
    // Verify results
    Assert.Equal(42, emulator.Cpu.Regs[2]); // $v0
}
```

## Test Data Management

### Fixtures
- **TestElfLoader**: Loads test ELF files
- **MemoryFixture**: Sets up memory state
- **HardwareFixture**: Initializes peripherals

### Data Sources
- **Embedded resources** for test binaries
- **External files** for large test data
- **Generated data** for parametric tests

## Continuous Integration

### Automated Testing
- **GitHub Actions** run tests on every commit
- **Code coverage** reporting
- **Performance regression** detection

### Quality Gates
- **Test pass rate** must be 100%
- **Coverage minimum** of 90%
- **No performance degradation**

## Debugging Tests

### Test Debugging
- **Step-through execution** in debugger
- **Memory inspection** during test runs
- **Log output** for complex tests

### Failure Analysis
- **Detailed assertions** with context
- **Expected vs actual** comparisons
- **Stack traces** for exceptions

## Performance Testing

### Benchmark Tests
- **Execution speed** measurements
- **Memory usage** tracking
- **Scalability tests** for large games

### Profiling
- **CPU profiling** of test execution
- **Memory leak detection**
- **Threading analysis**

## Maintenance

### Test Organization
- **Clear naming** conventions
- **Logical grouping** by functionality
- **Documentation** for complex tests

### Updating Tests
- **Reference updates** when PCSX2 changes
- **New test addition** for new features
- **Test refactoring** for maintainability

## Examples

### Instruction Test

```csharp
[Fact]
public void TestLoadWord() {
    var bus = new PS2MemoryBus();
    bus.Write32(0x1000, 0x12345678);
    
    var cpu = new CpuState { MemoryBus = bus };
    var lw = new LwInstruction { Rs = 0, Rt = 2, Immediate = 0x1000 };
    lw.Exec(cpu);
    
    Assert.Equal(0x12345678, cpu.Regs[2]);
}
```

### Game Compatibility Test

```csharp
[Fact]
public void TestGameBoot() {
    var emulator = new PS2Emulator();
    emulator.LoadElf("game.elf");
    
    // Boot sequence
    for (int i = 0; i < 1000; i++) {
        emulator.Step();
    }
    
    // Verify game reached main menu
    Assert.True(emulator.MemoryBus.Read32(0x00100000) != 0);
}
```

## Best Practices

### Test Design
- **Single responsibility** per test
- **Descriptive names** and comments
- **Arrange-Act-Assert** pattern

### Coverage Goals
- **Statement coverage** > 90%
- **Branch coverage** > 80%
- **Path coverage** for critical paths

### Maintenance
- **Regular review** of test effectiveness
- **Update tests** with code changes
- **Remove obsolete** tests</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\test-project.md