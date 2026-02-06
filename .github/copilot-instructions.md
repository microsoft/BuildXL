# Copilot Instructions for BuildXL

## Primary Reference
**Read [Documentation/Wiki/DeveloperGuide.md](../Documentation/Wiki/DeveloperGuide.md)** for build commands, test commands, code style, and contribution instructions.

## Critical Context for Agents

### Build System is DScript, not MSBuild
- `.dsc` files define builds — these are the source of truth
- `.csproj`/`.sln` files are **generated** for IDE support only — do not edit them
- Generate VS solution with `bxl -vs`

### Making Changes
1. Edit the `.cs` source files as normal
2. If adding new files, they must be referenced in the appropriate `.dsc` file
3. Validate with `bxl.cmd <changed-component>.dsc`
4. Run related tests with `-TestClass` or `-TestMethod` flags

### Code Style (enforced by .editorconfig)
- Private instance fields: `m_fieldName`
- Private static fields: `s_fieldName`
- Constants: `PascalCase`
- Always use braces for control statements

### Source Layout
```
Public/Src/
├── Engine/      # Core build engine, scheduler
├── Cache/       # Caching infrastructure
├── FrontEnd/    # Language frontends (MSBuild, JS, Ninja)
├── Utilities/   # Shared libraries
└── */UnitTests/ # Tests alongside components
```

### Common Pitfalls
- **Typos in test filters**: If `-TestMethod` matches nothing, the build still passes — verify filter is correct
- **Generated files**: Don't edit `.csproj` files; edit `.dsc` instead
- **Qualifiers**: Default is Debug; use `/q:Release` or `/q:ReleaseDotNetCore` for release builds
