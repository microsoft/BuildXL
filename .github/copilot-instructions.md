# Copilot Instructions for BuildXL

## Primary References
- **[Documentation/Wiki/DeveloperGuide.md](../Documentation/Wiki/DeveloperGuide.md)** — Build commands, test commands, code style
- **[Documentation/Wiki/CoreConcepts.md](../Documentation/Wiki/CoreConcepts.md)** — Architecture, pips, caching, sandboxing
- **[Documentation/Wiki/DScript/Introduction.md](../Documentation/Wiki/DScript/Introduction.md)** — DScript language guide
- **[Documentation/Wiki/Modules.md](../Documentation/Wiki/Modules.md)** — Module system and dependencies

## What BuildXL Is
BuildXL (Build Accelerator) is a build engine for large-scale distributed, cached, and incremental builds. It enforces correctness through **sandboxed process execution** with full file-access monitoring (Detours on Windows, ptrace/eBPF on Linux). Builds are content-based (not timestamp-based).

### Key Abstractions
- **Pip** (Primitive Indivisible Process): A node in the build DAG — can be a process, file copy, write-file, service, or IPC call. Each pip declares its inputs and outputs explicitly.
- **Pip Graph**: A DAG where edges represent output→input dependencies between pips. Built in phase 1 (graph construction), then executed in phase 2.
- **Frontends**: Pluggable languages that construct the pip graph — DScript (native), MsBuild, JavaScript (Rush/Yarn/Lage), Ninja, NuGet, Download. Multiple frontends can be mixed in one build.
- **Qualifiers**: Build parametrization (like configuration×platform). Accessed via the `qualifier` keyword in DScript.
- **Sealed Directories**: Directories whose contents are declared up-front, enabling caching of builds with directory dependencies.

### Two Build Phases
1. **Graph Construction**: Frontends evaluate specs and produce the pip graph (cacheable).
2. **Execution**: Scheduler runs pips in dependency order, checks cache, sandboxes processes, caches outputs.

## Build System is DScript, not MSBuild
- **`.dsc` files define builds** — these are the source of truth
- `.csproj`/`.sln` files are **generated** for IDE support only — **do not edit them**
- Generate VS solution: `bxl -vs` (or `bxl -vs -cache` to include cache projects)
- Root config: `config.dsc` defines resolvers, qualifiers, and module references

### DScript Basics
DScript is a TypeScript-based language with key differences: no `void` functions, immutable by default (`const`), no classes, no `null`/`undefined`.

**Path literals** (unique to DScript):
- `f`path/to/file.cs`` — file reference
- `d`path/to/dir`` — directory reference
- `p`path/to/output`` — output path (promise)
- `a`name`` — path atom
- `r`relative/path`` — relative path

**Import/export**:
```typescript
import {Result} from "ModuleName";           // named import
const x = importFrom("ModuleName").value;    // inline import
@@public
export const myLib = ...;                     // public export (visible to other modules)
```

**Globbing for sources**:
```typescript
const sources = globR(d`.`, "*.cs");          // recursive glob for .cs files
const files = glob(d`src`, "*.txt");          // non-recursive glob
```

### Module System
Each module has a `module.config.bm` (or `module.config.dsc`) file:
```typescript
module({
    name: "BuildXL.Utilities.Core",    // globally unique hierarchical name
    projects: globR(d`.`, "*.dsc"),    // auto-discover .dsc files (or omit for auto)
});
```

**Key conventions**:
- Module name follows `Company.Product.Component` pattern
- One module ≈ one team's component (like a solution file)
- Dependencies are declared via `import`/`importFrom` in .dsc specs
- Use `allowedDependencies` in module config to constrain dependency graph

### Adding a New Source File
1. Create the `.cs` file in the appropriate directory
2. If the `.dsc` spec uses explicit file lists, add the file with `f`filename.cs`` syntax
3. If the spec uses `globR(d`.`, "*.cs")`, the file is picked up automatically
4. Ensure the namespace matches the module's naming convention

### Adding a New Project/Module
1. Create a directory with your `.dsc` spec file
2. Create a `module.config.bm` defining the module name and projects
3. Reference the module in the parent resolver (in `config.dsc` or via glob patterns)
4. Export public values with `@@public` decorator

## Build & Test Commands

Use `bxl.cmd` on Windows and `bxl.sh` on Linux. The examples below use `bxl.cmd`; substitute `./bxl.sh` and Linux-style flags (e.g., `--minimal`, `--deploy-dev`, `--use-dev`, `--release`, `--test-class`, `--test-method`) on Linux.

### Common Build Commands
```bash
# Windows
bxl.cmd -minimal                    # Quick build (bxl.exe + deps only)
bxl.cmd                             # Standard build + tests
bxl.cmd -all                        # Build everything
bxl.cmd /q:ReleaseNet8              # Build with specific qualifier
bxl -deploy dev -minimal            # Build and deploy debug bxl.exe
bxl -use dev                        # Use locally-built bxl.exe

# Linux
# --internal is for Microsoft internal developers accessing internal dependencies (internal NuGet feeds, etc.).
# Non-Microsoft developers should omit it. On Windows this is auto-detected from machine information,
# but on Linux it must be passed explicitly.
./bxl.sh --internal --minimal       # Quick build
./bxl.sh --internal                 # Standard build + tests
./bxl.sh --internal --release       # Release build
./bxl.sh --internal --deploy-dev --minimal  # Build and deploy debug bxl
./bxl.sh --internal --use-dev       # Use locally-built bxl
```

### Running Tests
```bash
# Target a specific test project
bxl IntegrationTest.BuildXL.Scheduler.dsc

# Run a specific test class (Windows / Linux)
bxl IntegrationTest.BuildXL.Scheduler.dsc -TestClass IntegrationTest.BuildXL.Scheduler.BaselineTests
./bxl.sh --internal --test-class IntegrationTest.BuildXL.Scheduler.BaselineTests

# Run a specific test method (Windows / Linux)
bxl IntegrationTest.BuildXL.Scheduler.dsc -TestMethod IntegrationTest.BuildXL.Scheduler.BaselineTests.VerifyGracefulTeardownOnPipFailure
./bxl.sh --internal --test-method IntegrationTest.BuildXL.Scheduler.BaselineTests.VerifyGracefulTeardownOnPipFailure

# Run only tests (filter by tag)
bxl "/f:tag='test'"
```

### Useful Flags
| Flag | Purpose |
|------|---------|
| `/f` or `/Filter` | Filter expression to build specific pips |
| `/q` or `/Qualifier` | Set build qualifier (e.g., `/q:DebugNet8`) |
| `/cv` or `/ConsoleVerbosity` | Output level: Off, Error, Warning, Informational, Verbose |
| `/cacheMiss+` | Analyze cache misses |
| `/Phase` | Stop at phase: Parse, Evaluate, Schedule, Execute |
| `/IncrementalScheduling+` | Enable incremental scheduling |

### Debugging
```bash
set BuildXLDebugOnStart=1           # Triggers debugger popup on launch
# Or add in code: System.Diagnostics.Debugger.Launch();
bxl -use Dev                        # Rebuild and run with local bxl.exe
```

### Pre-Checkin Validation
```bash
bxl.cmd -minimal                    # Quick sanity check
bxl.cmd                             # Runs all unit tests (slow)
```

## Code Style (enforced by .editorconfig)

| Element | Convention | Example |
|---------|-----------|---------|
| Private instance fields | `m_` + camelCase | `m_processCount` |
| Private static fields | `s_` + camelCase | `s_defaultTimeout` |
| Constants | PascalCase | `MaxRetryCount` |
| Public members | PascalCase | `GetProcessInfo()` |
| Parameters/locals | camelCase | `processId`, `filePath` |
| Control statements | Always use braces | Even for single-line `if`/`for` |

## Source Layout
```
BuildXL.Internal/
├── Public/Src/
│   ├── App/              # Entry points (bxl.exe)
│   ├── Cache/            # Caching infrastructure
│   ├── Engine/           # Core build engine, scheduler, processes
│   ├── FrontEnd/         # Language frontends (DScript, MSBuild, JS, Ninja)
│   ├── Pips/             # Pip definitions and data structures
│   ├── Sandbox/          # OS sandboxing (Detours, ptrace, eBPF)
│   ├── Tools/            # Supporting tools
│   ├── Utilities/        # Shared libraries
│   └── */UnitTests/      # Tests alongside components
├── Private/              # Internal-only code
├── Shared/               # Shared across public/private
├── config.dsc            # Root build configuration
├── bxl.cmd / bxl.sh      # Build entry points
└── Documentation/Wiki/   # Architecture and developer docs
```

## Common Qualifiers
- `Debug` (default), `Release`
- `DebugNet472`, `ReleaseNet472`
- `DebugDotNetCoreMac`, `DebugLinux` (platform-specific)
- Specify with `/q:` flag, e.g., `/q:DebugNet8`
- **Note**: If a qualifier is invalid, the build fails fast with error DX11250 listing all available qualifiers

## Git Branch Naming Convention
When creating branches, always use the format: `dev/[username]/[feature-description]`
- `[username]` should be normalized to a valid ref component (lowercase, no spaces or backslashes) or use a ref-safe value like the GitHub username
- `[feature-description]` is a short kebab-case summary of the change
- Example: `dev/mpysson/fix-refs-hardlink-test`

## Git Workflow Rules
- **Do not rewrite git history** — no `--amend`, `rebase`, or `force-push`. Always make new commits with changes unless explicitly requested by the user.
- **Do not push to remote branches** unless the user explicitly asks. Commit locally by default.

## Common Pitfalls
- **Typos in test filters**: If `-TestMethod` matches nothing, the build still **succeeds silently** — always verify the filter matches an actual test
- **Generated files**: Never edit `.csproj` or `.sln` files; edit `.dsc` specs instead
- **Qualifiers**: Default is Debug; use `/q:Release` to do release builds for testing performance optimizations
- **Sandbox violations**: Undeclared file accesses cause build failures or prevent caching — all inputs/outputs must be declared in the pip spec
- **DScript immutability**: Variables are `const` by default; there's no `let` reassignment or mutable state

## Running Builds from Agents / Automated Tooling

### How builds are launched
Both `bxl.cmd` (Windows) and `bxl.sh` (Linux) are **synchronous** — they block until the build completes and propagate the exit code. Wait for the script's process to exit to know when the build is done.

- **Windows**: `bxl.cmd` calls `powershell Bxl.ps1`, which uses `Start-Process bxl.exe` + `Wait-Process`
- **Linux**: `bxl.sh` directly invokes the `bxl` binary and captures `$?`

### Important: `bxl` server process
BuildXL runs in **server mode** by default. After a build completes, a `bxl` server process remains running in the background to speed up subsequent builds. **Do not** check for running `bxl` processes to determine if a build is still running — the server process will always be present. Instead, wait for the `bxl.cmd`/`bxl.sh` process to exit.

### Checking build and test results
Build console output is noisy (credential provider output, progress bars) and may not flow cleanly to the calling shell. **Do not rely on console output** to determine success or failure.

Instead:

1. **Run `bxl.cmd` (Windows) or `bxl.sh` (Linux)** and wait for the process to exit
2. **Check `Out/Logs/<latest>/BuildXL.err`** (newest timestamped subdirectory) — this is the authoritative result:
   - **Empty or missing** → build and all tests succeeded
   - **Has content** → contains errors, including test failure messages and stack traces
3. **For more details**, error messages in `BuildXL.err` may reference additional log files — follow those paths for full output
