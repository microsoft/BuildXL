# Copilot Instructions for BuildXL

## BuildXL context

BuildXL is a distributed, cached, incremental build engine. Frontends construct
a pip graph, then the scheduler executes it. Pips declare inputs and outputs,
and sandboxed file-access monitoring enforces those declarations. Build
correctness and caching are content-based rather than timestamp-based.

Important concepts:

- A pip is a node in the build DAG, such as a process, copy, write, service, or
  IPC operation.
- Frontends include DScript, MSBuild, JavaScript, Ninja, NuGet, and Download.
- Qualifiers parameterize builds, commonly by configuration and platform.
- Sealed directories declare directory contents in advance so directory
  dependencies can be cached safely.

Primary references:

- [Developer guide](../Documentation/Wiki/DeveloperGuide.md)
- [Core concepts](../Documentation/Wiki/CoreConcepts.md)
- [DScript guide](../Documentation/Wiki/DScript/Introduction.md)
- [Module system](../Documentation/Wiki/Modules.md)

## Build definitions and DScript

`.dsc` files define the build and are the source of truth. `.csproj` and `.sln`
files are generated for IDE support; never edit them. Generate them with
`bxl -vs`, `bxl -vs -cache`, or `bxl -vsall`.

DScript is TypeScript-derived but is not TypeScript:

- Top-level values can only be `const` declarations or functions and are
  therefore immutable. `let` values are allowed inside functions; `var` values
  are disallowed.
- The `qualifier` keyword exposes build parametrization.
- `export` makes values accessible to other specs under the same module;
  `@@public` makes an export visible to other modules.
- Dependencies are declared with `import` or `importFrom`.

DScript path literals are typed:

```typescript
f`path/to/file.cs`       // File
d`path/to/directory`     // Directory
p`path/to/output`        // Output path
a`name`                  // Path atom
r`relative/path`         // Relative path
```

Follow nearby `.dsc` patterns rather than assuming TypeScript syntax. Common
source patterns are:

```typescript
const recursiveSources = globR(d`.`, "*.cs");
const directFiles = [f`First.cs`, f`Second.cs`];
```

When adding a source file, check whether its project uses a glob or an explicit
file list. Add the file to the `.dsc` list when needed. Do not update a generated
project.

Modules use `module.config.bm` or `module.config.dsc`. Module names are globally
unique and normally hierarchical. Respect `allowedDependencies`; add imports
rather than bypassing the module dependency graph.

BuildXL sandbox correctness depends on observing inputs and outputs. Unobserved
file accesses can fail a build or make it uncacheable.

## Building and testing

Run builds from the repository root. On Windows, invoke the batch file through
`cmd /c "call ..."` and pass `/server-` so agents do not leave a BuildXL server
running:

```powershell
cmd /c "call bxl.cmd -minimal /server-"
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Collections.dsc /server-"
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Collections.dsc -TestClass Test.BuildXL.Utilities.Collections.BitSetTests /server-"
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Collections.dsc -TestMethod Test.BuildXL.Utilities.Collections.BitSetTests.RoundToValidBitCount /server-"
cmd /c "call bxl.cmd /q:ReleaseNet10 /server-"
```

On Linux, use `./bxl.sh`. Microsoft internal developers need `--internal` for
internal dependencies; external contributors must omit it. The first run may
require `sudo`.

```bash
./bxl.sh --internal --minimal /server-
./bxl.sh --internal --test-class Test.BuildXL.Utilities.Collections.BitSetTests /server-
./bxl.sh --internal --test-method Test.BuildXL.Utilities.Collections.BitSetTests.RoundToValidBitCount /server-
```

The first build may restore the LKG and credential provider, so allow at least
five minutes. Build output ends with `Build Succeeded` or `Build FAILED`. On
failure, use the printed log directory and inspect `BuildXL.err` for the
authoritative errors.

`-TestClass` and `-TestMethod` succeed when they match no tests. Verify the fully
qualified name exists and confirm the expected tests actually ran.

Use the smallest relevant validation first:

```powershell
cmd /c "call bxl.cmd <target-test-project>.dsc /server-"
cmd /c "call bxl.cmd -minimal /server-"
cmd /c "call bxl.cmd /server-"
```

The standard build and test suite is slow. `RunCheckInTests.cmd` performs
multiple configurations and fingerprint checks and is normally left to PR CI.
Use a Release qualifier when validating performance-sensitive changes.

Manually invoked benchmarks should remain `BuildXLSdk.test` projects, with the
entire test run gated by `skipTestRun` and an `Environment.getFlag` build
parameter. This keeps benchmarks compiled but prevents automation from
scheduling them. Run the utilities benchmarks with:

```powershell
cmd /c "call bxl.cmd Test.BuildXL.Utilities.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet10 /server-"
```

## Code conventions

Follow `.editorconfig` and nearby code. In C#:

- Private instance, static, and thread-static fields use `m_`, `s_`, and `t_`
  prefixes respectively.
- Constants and public members use PascalCase; parameters and locals use
  camelCase.
- Method and test names use PascalCase without underscores.
- Always use braces for control statements.
- Use `var` when the type is apparent from the right-hand side; use an explicit
  type for built-ins or when the type is not obvious.

Every new C# file starts with:

```csharp
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
```

## Git

- Name branches `dev/[username]/[short-kebab-case-description]`.
- Do not amend, rebase, force-push, or otherwise rewrite history unless the
  user explicitly requests it. Add a new commit instead.
- Do not push unless the user explicitly requests it. Make local commits
  only when instructed by the user.

## Worktrees

Place parallel worktrees under the sibling
`../BuildXL.Internal.worktrees/<feature>` directory. Worktrees share the main
worktree's `Out\Cache`, so builds can reuse cache entries.

Builds from different worktrees also share the machine-global `B:` subst drive.
`RunInSubst.exe` serializes builds with `.SubstLock`; start the build normally
and let it queue behind the worktree currently using the drive. A queued build
may appear hung. Never modify or delete `.SubstLock`, manually unmap or remap
`B:`, or terminate processes to bypass the queue. When diagnosing which
worktree owns the subst mapping, use the absolute path of the running
`RunInSubst.exe`.
