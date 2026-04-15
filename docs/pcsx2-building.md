[← Back to Documentation Index](README.md)

# PCSX2 Building Guide — Compiling from Source

This guide covers building PCSX2 from source code for development, testing, and CodeDesigner integration.

## Prerequisites

### System Requirements
- **Operating System**: Windows 10+, Ubuntu 22.04+, macOS 11+
- **RAM**: 8GB minimum, 16GB recommended
- **Storage**: 20GB free space for source and build
- **Internet**: For downloading dependencies

### Development Tools
- **Git**: Version control system
- **CMake**: Build system generator (3.16+)
- **Ninja** (optional): Fast build system
- **Python**: Required for some build scripts

## Windows Build Setup

### Visual Studio Installation
1. Download Visual Studio 2022 Community (free)
2. Install with these workloads:
   - **Desktop development with C++**
   - **Game development with C++**
   - **Windows SDK** (latest version)

### Required Tools
```powershell
# Install Scoop (package manager)
iwr -useb get.scoop.sh | iex

# Install build tools
scoop install git cmake ninja python

# Alternative: Chocolatey
choco install git cmake ninja python
```

### Qt Installation
1. Download Qt from [qt.io](https://www.qt.io/download)
2. Install Qt 6.5+ with these components:
   - **Qt Core**
   - **Qt GUI**
   - **Qt Widgets**
   - **Qt Network**
   - **Qt OpenGL**

## Linux Build Setup

### Ubuntu/Debian Dependencies
```bash
# Update system
sudo apt update && sudo apt upgrade

# Install build essentials
sudo apt install build-essential cmake ninja-build git python3

# Install Qt
sudo apt install qt6-base-dev qt6-tools-dev

# Install additional libraries
sudo apt install libgl1-mesa-dev libpulse-dev libasound2-dev libgtk-3-dev
```

### Arch Linux
```bash
# Install dependencies
sudo pacman -S base-devel cmake ninja git python qt6-base qt6-tools
```

### Fedora
```bash
# Install dependencies
sudo dnf install cmake ninja-build git python qt6-qtbase-devel qt6-qttools-devel
```

## macOS Build Setup

### Xcode Installation
1. Install Xcode from App Store
2. Install command line tools:
```bash
xcode-select --install
```

### Homebrew Dependencies
```bash
# Install Homebrew
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# Install dependencies
brew install cmake ninja git python qt6
```

## Cloning the Repository

### Standard Clone
```bash
git clone https://github.com/PCSX2/pcsx2.git
cd pcsx2
```

### Development Branch (Recommended)
```bash
git clone https://github.com/PCSX2/pcsx2.git
cd pcsx2
git checkout master  # or specific development branch
```

## Build Process

### Configure Build
```bash
# Create build directory
mkdir build
cd build

# Configure with CMake
cmake .. -G Ninja

# Windows alternative
cmake .. -G "Visual Studio 17 2022"
```

### Build Options
- **CMAKE_BUILD_TYPE**: Release, Debug, RelWithDebInfo
- **ENABLE_QT**: Enable Qt GUI (default: ON)
- **ENABLE_VULKAN**: Enable Vulkan renderer (default: ON)
- **ENABLE_CUBEB**: Enable Cubeb audio (default: ON)

### Advanced Configuration
```bash
# Debug build with PINE support
cmake .. -G Ninja \
  -DCMAKE_BUILD_TYPE=Debug \
  -DENABLE_PINE=ON \
  -DENABLE_DEV_BUILD=ON
```

### Compile
```bash
# Using Ninja
ninja

# Using Make
make -j$(nproc)

# Windows Visual Studio
cmake --build . --config Release
```

## Running the Build

### Executable Location
- **Windows**: `build\bin\PCSX2.exe`
- **Linux**: `build/bin/PCSX2`
- **macOS**: `build/bin/PCSX2.app`

### First Run
1. Launch the compiled executable
2. Complete first-time setup
3. Configure BIOS and plugins
4. Test with a game

## Development Features

### PINE Support
- **Enable PINE**: Required for CodeDesigner integration
- **Debug builds**: Include debugging symbols
- **Network testing**: Verify PINE server functionality

### Custom Builds
- **Feature flags**: Enable/disable specific features
- **Optimization levels**: Customize compiler optimizations
- **Plugin development**: Build custom plugins

## Troubleshooting Build Issues

### Common Problems

#### CMake Configuration Errors
- **Missing dependencies**: Install required libraries
- **Qt not found**: Set QT_DIR environment variable
- **Compiler issues**: Update Visual Studio/gcc

#### Compilation Errors
- **Out of memory**: Reduce parallel jobs (-j4 instead of -j8)
- **Missing headers**: Install development packages
- **Linker errors**: Check library paths

#### Runtime Issues
- **Missing DLLs**: Copy required DLLs to executable directory
- **Qt plugins**: Ensure Qt plugins are available
- **Graphics drivers**: Update GPU drivers

### Debug Builds
```bash
# Enable debug information
cmake .. -G Ninja -DCMAKE_BUILD_TYPE=Debug

# Build with symbols
ninja

# Run with debugger
gdb ./bin/PCSX2
```

### Clean Rebuild
```bash
# Clean build directory
rm -rf build
mkdir build
cd build

# Reconfigure and rebuild
cmake .. -G Ninja
ninja
```

## Advanced Build Options

### Cross-Compilation
- **Windows on Linux**: Using MinGW
- **ARM builds**: For specific platforms
- **Static linking**: Reduce dependencies

### Custom Toolchains
- **Clang**: Alternative compiler
- **ICC**: Intel C++ Compiler
- **Optimization flags**: Custom compiler options

### CI/CD Integration
- **GitHub Actions**: Automated building
- **Custom scripts**: Automated build pipelines
- **Artifact management**: Distribution of builds

## CodeDesigner Integration

### Building for Debugging
```bash
# Enable PINE and debug features
cmake .. -G Ninja \
  -DCMAKE_BUILD_TYPE=Debug \
  -DENABLE_PINE=ON \
  -DENABLE_DEV_BUILD=ON \
  -DENABLE_LOGGING=ON
```

### Testing Integration
1. **Build PCSX2** with PINE support
2. **Launch with PINE enabled**
3. **Connect CodeDesigner** to PINE port
4. **Test debugging features**

### Development Workflow
1. **Modify PCSX2 source**
2. **Rebuild PCSX2**
3. **Test with CodeDesigner**
4. **Iterate on changes**

## Contributing to PCSX2

### Development Setup
1. **Fork repository** on GitHub
2. **Create feature branch**
3. **Make changes**
4. **Test thoroughly**
5. **Submit pull request**

### Coding Standards
- **C++17**: Use modern C++ features
- **Code formatting**: Follow project style
- **Documentation**: Comment complex code
- **Testing**: Include unit tests

### Pull Request Process
1. **Describe changes** clearly
2. **Reference issues** if applicable
3. **Include screenshots** for UI changes
4. **Test on multiple platforms**

## Resources

### Documentation
- [PCSX2 Wiki](https://wiki.pcsx2.net/) - Development documentation
- [Building Guide](https://pcsx2.net/docs/advanced/building) - Official build guide
- [Contributing Guide](https://pcsx2.net/docs/contributing) - How to contribute

### Development Tools
- [GitHub Repository](https://github.com/PCSX2/pcsx2) - Source code
- [Discord Server](https://pcsx2.net/discord) - Developer chat
- [Forums](https://forums.pcsx2.net/) - Development discussion

### Build Systems
- [CMake Documentation](https://cmake.org/documentation/) - Build system docs
- [Ninja Manual](https://ninja-build.org/manual.html) - Ninja build tool
- [Qt Documentation](https://doc.qt.io/) - Qt framework docs

## Conclusion

Building PCSX2 from source enables advanced development and CodeDesigner integration. Follow this guide for successful compilation and start contributing to the PCSX2 project.