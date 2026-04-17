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
- Generate VS solution: `bxl -vs` (or `bxl -vs -cache` to include cache projects, or `bxl -vsall` for all projects)
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
RunCheckInTests.cmd                 # Full pre-checkin validation (multiple configs + fingerprint checks — very slow, usually left to PR CI)
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

**File header**: Every `.cs` file must start with:
```csharp
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
```

**`var` usage**: Use `var` when the type is apparent from the right-hand side (e.g., `var customer = new Customer();`). Use explicit types for built-in types and when the type isn't obvious.

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

### How to run a build

```powershell
# Windows — Minimal build (bxl.exe + deps, no tests) — ~2-5 min first run, seconds with cache hits
cmd /c "call bxl.cmd -minimal /server-"

# Windows — Build + tests for a specific component
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Collections.dsc /server-"

# Windows — Run a single test method
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Collections.dsc -TestMethod Test.BuildXL.Utilities.Collections.BitSetTests.RoundToValidBitCount /server-"

# Linux — equivalents use bxl.sh (add --internal for Microsoft internal developers)
./bxl.sh --internal --minimal /server-
./bxl.sh --internal Test.BuildXL.Utilities.Collections.dsc /server-
```

**Use `cmd /c "call bxl.cmd ..."`** (Windows) or `./bxl.sh` (Linux) from the repo root. The command blocks until completion and returns exit code 0 (success) or non-zero (failure). Use a long timeout (300+ seconds) — the first run in a session involves credential provider setup and LKG package download before the build starts.

### Key flags for agent builds

| Flag | Purpose |
|------|---------|
| `/server-` | Prevents a persistent bxl server process from being left running after the build |
| `-Minimal` | Build only bxl.exe + dependencies (fastest sanity check) |

### Checking build results

Console output ends with `Build Succeeded` or `Build FAILED`. The exit code reflects this (0 = success, non-zero = failure). Compile errors (e.g., `error CS1002: ; expected`) and test failures (e.g., `error DX0064: ... failed with exit code 1`) appear inline in stdout with file paths and line numbers.

On failure, the log directory path is printed to stdout (e.g., `Log Directory: Q:\src\BuildXL.Internal\Out\Logs\20260417-141028`). Check `BuildXL.err` in that directory for the full error details and stack traces.

### Log directory contents

| File | Purpose |
|------|---------|
| `BuildXL.err` | **Authoritative result** — missing = success, has content = errors |
| `BuildXL.log` | Full build log with all details |

### The `bxl` server process

When running **without** `/server-`, a `bxl` server process remains running after the build to speed up subsequent builds. If you've changed `config.dsc` or other build configuration, kill the server before re-running to avoid stale graph state:
```powershell
Get-Process bxl -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id }
```
