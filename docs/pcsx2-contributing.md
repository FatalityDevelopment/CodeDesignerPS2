[← Back to Documentation Index](README.md)

# PCSX2 Contributing Guide

This guide covers how to contribute to the PCSX2 project, including code contributions, testing, documentation, and community involvement.

## Ways to Contribute

### Code Contributions
- **Bug fixes**: Fix reported issues
- **Feature implementation**: Add new functionality
- **Performance improvements**: Optimize existing code
- **Code cleanup**: Improve code quality and maintainability

### Testing and Quality Assurance
- **Bug reporting**: Report issues with detailed information
- **Compatibility testing**: Test games and report results
- **Regression testing**: Verify fixes don't break existing functionality

### Documentation
- **User guides**: Improve setup and usage documentation
- **Developer docs**: Document code and APIs
- **Wiki maintenance**: Update PCSX2 wiki pages
- **Translation**: Help translate PCSX2 into other languages

### Community Support
- **Forum moderation**: Help users on forums
- **Discord support**: Assist users on Discord
- **Tutorial creation**: Make video/text tutorials
- **Tool development**: Create utilities for PCSX2 users

## Getting Started

### Development Environment
1. **Set up build environment** (see [building guide](pcsx2-building.md))
2. **Clone repository**: `git clone https://github.com/PCSX2/pcsx2.git`
3. **Build PCSX2**: Follow build instructions
4. **Test basic functionality**: Ensure your build works

### Development Workflow
1. **Choose an issue** from GitHub issues
2. **Create a branch** for your changes
3. **Implement the fix/feature**
4. **Test thoroughly**
5. **Submit a pull request**

## Code Contribution Process

### Finding Issues to Work On
- **GitHub Issues**: [github.com/PCSX2/pcsx2/issues](https://github.com/PCSX2/pcsx2/issues)
- **Good first issues**: Look for "good first issue" label
- **Bounty issues**: Issues with monetary rewards
- **Forum requests**: User-reported problems

### Issue Labels
- **bug**: Bug fixes
- **enhancement**: New features
- **documentation**: Documentation improvements
- **help wanted**: Community assistance needed
- **good first issue**: Suitable for newcomers

### Branch Naming
- **feature/name**: New features
- **fix/issue-number**: Bug fixes
- **refactor/name**: Code refactoring
- **docs/name**: Documentation changes

### Commit Messages
- **Clear and descriptive**: Explain what and why
- **Reference issues**: Include issue numbers (#123)
- **Conventional format**: `fix: resolve crash in GS renderer`

## Coding Standards

### C++ Guidelines
- **C++17 standard**: Use modern C++ features
- **RAII**: Resource acquisition is initialization
- **Smart pointers**: Use `std::unique_ptr`, `std::shared_ptr`
- **Exceptions**: Use for error handling
- **Const correctness**: Use `const` appropriately

### Code Style
- **Braces**: K&R style (opening brace on same line)
- **Naming**: `camelCase` for functions/variables, `PascalCase` for classes
- **Indentation**: Tabs for indentation, spaces for alignment
- **Line length**: 120 characters maximum

### Code Organization
- **Header files**: `.h` extension
- **Source files**: `.cpp` extension
- **Include guards**: `#pragma once` preferred
- **Forward declarations**: Use when possible

## Pull Request Process

### Before Submitting
1. **Test your changes**:
   - Build succeeds on your platform
   - Basic functionality works
   - No regressions in existing features
   - Test on multiple games if applicable

2. **Code quality**:
   - Follow coding standards
   - Add comments for complex code
   - Update documentation if needed
   - Include unit tests if applicable

3. **Commit hygiene**:
   - Logical commit progression
   - Clear commit messages
   - Squash fixup commits

### Creating a Pull Request
1. **Push your branch** to your fork
2. **Create PR** on GitHub
3. **Fill out the template**:
   - Description of changes
   - Testing performed
   - Screenshots for UI changes
   - Related issues

4. **Request review** from maintainers

### Review Process
- **Automated checks**: CI/CD pipeline runs
- **Code review**: Maintainers review code
- **Testing**: Additional testing may be requested
- **Feedback**: Address reviewer comments
- **Approval**: PR gets approved and merged

## Testing Guidelines

### Unit Testing
- **Framework**: Google Test (gtest)
- **Location**: `pcsx2/tests/`
- **Coverage**: Aim for good test coverage
- **CI**: Tests run on every PR

### Compatibility Testing
- **Game testing**: Test on various games
- **Regression testing**: Ensure no existing functionality breaks
- **Edge cases**: Test unusual scenarios

### Performance Testing
- **Benchmarks**: Use built-in benchmarking tools
- **Performance regression**: Monitor for performance changes
- **Memory usage**: Check for memory leaks

## Documentation Contributions

### User Documentation
- **Setup guides**: Improve installation instructions
- **Troubleshooting**: Add solutions to common problems
- **FAQs**: Update frequently asked questions
- **Tutorials**: Create step-by-step guides

### Developer Documentation
- **Code comments**: Document complex functions
- **API docs**: Document public interfaces
- **Architecture**: Explain system design
- **Build instructions**: Keep build docs current

### Wiki Contributions
- **PCSX2 Wiki**: [wiki.pcsx2.net](https://wiki.pcsx2.net)
- **Game pages**: Update game compatibility info
- **How-to guides**: Create tutorial pages
- **Reference material**: Technical documentation

## Translation Contributions

### Interface Translation
- **Crowdin platform**: [crowdin.com/project/pcsx2-emulator](https://crowdin.com/project/pcsx2-emulator)
- **Languages**: Help translate to your native language
- **Proofreading**: Review existing translations

### Documentation Translation
- **Markdown files**: Translate documentation
- **Wiki pages**: Translate wiki content
- **Consistency**: Maintain terminology consistency

## Community Support

### Forum Moderation
- **Help users**: Answer questions on forums
- **Bug triage**: Help categorize user reports
- **Solution sharing**: Share solutions to common problems

### Discord Support
- **Real-time help**: Assist users on Discord
- **Bug reports**: Help users report issues properly
- **Community building**: Foster positive community

### Content Creation
- **YouTube tutorials**: Create video guides
- **Written guides**: Write blog posts or articles
- **Tool development**: Create utilities for users

## Advanced Contributions

### Core Development
- **Emulation core**: Improve CPU/GPU emulation
- **Plugin system**: Develop new plugins
- **Performance optimization**: Speed improvements
- **Compatibility fixes**: Fix game-specific issues

### Research and Development
- **Reverse engineering**: Analyze PS2 hardware
- **Algorithm improvements**: Better emulation techniques
- **New features**: Innovative functionality
- **Cross-platform work**: Port to new platforms

### Infrastructure
- **Build system**: Improve build process
- **CI/CD**: Enhance automated testing
- **Website**: Improve pcsx2.net
- **Tools**: Develop developer tools

## Recognition and Rewards

### Contributor Recognition
- **GitHub contributors**: Listed in repository
- **Forum badges**: Recognition on forums
- **Credits**: Mentioned in release notes
- **Hall of fame**: Special recognition for major contributors

### Monetary Rewards
- **Bounties**: Paid bounties for specific features
- **Sponsorship**: GitHub Sponsors program
- **Donations**: Support from community
- **Grants**: Development grants for major work

## Getting Help

### Communication Channels
- **GitHub Discussions**: Development discussion
- **Discord #dev-talk**: Developer chat
- **Forums**: Development section
- **IRC**: Real-time developer chat

### Finding Mentors
- **Experienced developers**: Ask for guidance
- **Code reviews**: Learn from feedback
- **Pair programming**: Work with other developers
- **Study code**: Learn from existing codebase

### Resources
- **PCSX2 Wiki**: [wiki.pcsx2.net](https://wiki.pcsx2.net)
- **Developer Docs**: In-source documentation
- **PS2 documentation**: Hardware references
- **Community blogs**: Developer blogs and tutorials

## Code of Conduct

### Respectful Communication
- **Be respectful**: Treat all contributors with respect
- **Constructive criticism**: Provide helpful feedback
- **Inclusive language**: Use inclusive and welcoming language
- **Professionalism**: Maintain professional conduct

### Collaboration Guidelines
- **Teamwork**: Work well with other contributors
- **Credit**: Give credit where due
- **Transparency**: Be open about your work
- **Accountability**: Take responsibility for your contributions

## Conclusion

Contributing to PCSX2 is a rewarding experience that helps improve the emulator for millions of users. Whether you're fixing bugs, adding features, improving documentation, or helping users, your contributions make a difference. Start small, learn as you go, and don't hesitate to ask for help from the community.