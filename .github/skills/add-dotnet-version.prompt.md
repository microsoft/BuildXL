---
description: "Add support for a new .NET version (e.g. net11) to the BuildXL build engine. Two-stage process; the user will tell you the new version number and whether they want Stage 1 or Stage 2."
tools:
  - powershell
---

# Add a New .NET Version to BuildXL

> This is a multi-step engineering task spanning ~35-40 files across two commits. Follow the steps in order — they match the dependency chain and surface failures early. Don't auto-commit or auto-push; the repo policy requires explicit user approval before any `git commit` or `git push` (see `.github/copilot-instructions.md`).

You are helping a BuildXL maintainer add a new .NET version to the build engine. The process was distilled from net6 → net7 → net8 → net9 → net10 upgrade history and the merge/rebase pain experienced during the net10 PR.

## Your Task

The user will tell you the new major version number and which stage to run:

- **Stage 1** — Small commit (3 files): teach `NugetSpecGenerator` about the new framework moniker. Must merge and deploy (~1 week) before Stage 2 begins.
- **Stage 2** — Large commit (~35-40 files): wire the new framework everywhere else.

If the user hasn't told you which stage, ask. If they're unsure whether Stage 1 has been deployed, check it (see "Stage 1 deployment check" under Stage 2 Prerequisites below).

## Placeholders Used Throughout This Skill

- `{X}` = new major version number (e.g., `11`)
- `{X-1}` = previous major version (e.g., `10`)
- `{X}.0.{patch}` = full runtime version (e.g., `11.0.1`)
- `Net{X}0` = the C# property name (e.g., `Net110` for `net11.0`)
- `{M}` = the version currently pinned by the MsBuild/VBCS qualifier (e.g., as of this writing, `9` — i.e. `Net9QualifierWithNet472`). MsBuild's deployment is fragile, so those tests stay on an older fixed version; that version is whatever's named in the `Net{M}QualifierWithNet472` interface in `Qualifiers.dsc` today. `{M}` is *not* necessarily `{X-1}`.

> .NET 10+ uses `net{X}.0` as the TFM, and the C# property follows the pattern `Net{major}{minor}0` (so `Net100` for 10.0, `Net110` for 11.0). Don't confuse this with the old .NET Framework moniker `Net10` — which is registered as `.NETFramework1.0` and already exists in the codebase. Use double-digit names (`Net100`, `Net110`, …) for the new versions.

## Repo State Snapshot (update when running)

Before you start, verify the current state of the repo so you know what "previous version" means. Run from the repo root:

```powershell
git --no-pager log -1 -- Shared/Scripts/BuildXLLkgVersion.cmd
Get-Content Public/Sdk/SelfHost/BuildXL/Qualifiers.dsc | Select-String 'targetFramework'
```

As of the last skill update (after the net10 PR):
- Default qualifier: `net9.0`
- Supported targetFrameworks: `net8.0`, `net9.0`, `net10.0`, `net472` (full-framework subset)
- Test framework: xUnit V3 (`xunitv3framework.dsc` — the older `xunitframework.dsc` is legacy)
- MsBuild / VBCS tests are intentionally isolated to `Net9QualifierWithNet472` and do **not** extend to net{X≥10}

---

## Common pitfalls (read before editing)

These are the eight things that historically caused the most pain during net version upgrades. Skim them before starting Stage 2.

### 1. Companion repo PR

The pipeline's "Building Test Project Distributed" step clones `https://dev.azure.com/mseng/Domino/_git/Domino.DistributedBuildTest` at a specific commit pinned via `TEST_COMMITID` in `Shared/Scripts/BuildDistributedTest.cmd`. That repo also needs net{X} wiring before this repo's pipeline will pass.

Timing: this is part of Stage 2 work, **not** a Stage 0 step. The companion repo itself uses bxl, so its PR depends on Stage 1's LKG bxl already knowing about net{X}. The order is:

1. Stage 1 lands in this repo and rolls into LKG (~1 week).
2. Open a PR in the companion repo with net{X} wiring (it'll build green now that LKG knows net{X}).
3. After it merges, bump `TEST_COMMITID` in your Stage 2 branch (in this repo) to the new companion master SHA.
4. Stage 2 PR merges, carrying the `TEST_COMMITID` bump.

See [Step 10b](#step-10b-companion-repo-dominodistributedbuildtest) for the file-level details.

### 2. The "cascade bump" trap

If you bump `aspVersion` to `{X}.0.{patch}`, expect a chain of transitive dependency bumps that need to land together to keep the asp{X} nuspec graph happy. The *scope* of the cascade has grown over time:

- **net6 / net7**: minimal — `aspVersion` was not always bumped at all; new {X}.0 packages just sat alongside the previous version's
- **net8**: bumped `Microsoft.Extensions.*` 7.0.0 → 8.0.0
- **net9**: added `Microsoft.Bcl.AsyncInterfaces` to the cascade (6.0.0 → 9.0.0)
- **net10**: largest cascade — the full set below

So when you start, *expect* that bumping `aspVersion` will pull in at least one or two of these, and treat the full table as "what we hit on net10, may grow further on net11+":

| Package | Pattern |
| --- | --- |
| `aspVersion` | `{X-1}.0.x` → `{X}.0.y` |
| `Microsoft.Extensions.Logging.Abstractions` | `{X-1}.0.x` → `{X}.0.y` (since net8) |
| `Microsoft.Bcl.AsyncInterfaces` | `{X-1}.0.x` → `{X}.0.y` (since net9) |
| `System.Diagnostics.DiagnosticSource` | `{X-1}.0.x` → `{X}.0.y` (since net10) |
| `System.Collections.Immutable` | `{X-1}.0.x` → `{X}.0.y` *(net472 trap — see #4)* (since net10) |
| `System.Threading.Tasks.Extensions` | minor bump (since net10) |
| `System.ValueTuple` | minor bump (since net10) |
| `Microsoft.Bcl.HashCode` | matched to whatever ext.config.* references (since net10) |

Discovery flow if you don't know in advance which packages will cascade: bump `aspVersion`, run `bxl.cmd -minimal`, let it complain about missing/incompatible packages in the asp{X} closure, bump those, repeat.

### 3. New BCL absorptions may force `CS0433` collisions

This is the one pitfall that genuinely depends on what's in the .NET {X} release notes — it didn't apply at all to net6/7/8/9 upgrades, and only surfaced on net10 because .NET 10 specifically pulled three external packages into the BCL:

- `System.Linq.Async` (added `System.Linq.AsyncEnumerable`)
- `System.Threading.AccessControl`
- `System.IO.Pipelines`

When BuildXL's code referenced the NuGet copies of those packages on net10, the compiler reported `CS0433` ("type exists in both …") because the same type was now in the BCL too.

Before starting Stage 2, **scan the .NET {X} release notes / "What's new" doc** for phrases like "absorbed into", "moved into the BCL", "now ships in", or "available without a NuGet reference". If anything BuildXL already imports as a NuGet package shows up, plan to skip those imports on net{X}. If nothing's listed, you may not need this step at all.

The skip pattern goes in **whichever .dsc file imports the package**, not in a central spot — net10 made these edits in three different files:
- `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.Packages.dsc` (for `System.Linq.Async`)
- `Public/Src/Utilities/Native/BuildXL.Native.dsc` (for `System.Threading.AccessControl`)
- `Public/Src/Engine/Processes/BuildXL.Processes.dsc` (for `System.IO.Pipelines`)

See [Step 9 → BCL-absorbed packages](#bcl-absorbed-packages-only-if-needed) for the skip syntax.

### 4. net472 System.Collections.Immutable manifest-mismatch trap

This first surfaced on net10 (no record of it hitting net6/7/8/9). It's worth knowing about because it's *silent until tests run* and the failure mode is misleading.

Mechanism: when net472 tests reference any of the bumped `Microsoft.Extensions.*` {X}.0 assemblies, MSBuild's auto-binding-redirect generator follows the highest version in the reference closure and bakes:

```xml
<bindingRedirect oldVersion="0.0.0.0-{X}.0.0.y" newVersion="{X}.0.0.y" />
```

…for `System.Collections.Immutable` into every test's auto-generated `.exe.config`. If the deployed `System.Collections.Immutable` dll is still at `{X-1}.0.x`, every net472 test that touches it dies with `HRESULT 0x80131040` / `FileLoadException`.

Fix: bump `System.Collections.Immutable` to `{X}.0.y` in `config.nuget.dotnetcore.dsc`.

Canary test:
```powershell
bxl.cmd Test.BuildXL.Cache.MemoizationStoreAdapter.dsc /q:ReleaseNet472 /server-
```

### 5. Hand-maintained `.exe.config` / `.dsc` redirect files don't auto-regenerate

When you bump a package that has a binding redirect in any of these files, update the redirect by hand:

- `Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config` *(this one is the most common foot-gun — the standalone net472 MSBuild.exe spawned by tests reads it)*
- `Public/Src/Tools/Tool.MsBuildGraphBuilder/Tool.MsBuildGraphBuilder.dsc`
- `Public/Src/Tools/UnitTests/MsBuildGraphBuilder/Test.Tool.MsBuildGraphBuilder.dsc`
- `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.dsc` (`bxlBindingRedirects`, `cacheBindingRedirects`)
- `Public/Sdk/SelfHost/BuildXL/Testing/XUnitV3/xunitv3framework.dsc`

Canary test (catches msbuild.exe.config bugs):
```powershell
bxl.cmd Test.BuildXL.FrontEnd.MsBuild.dsc Test.Tool.VBCSCompilerLogger.dsc /q:Release /server-
```

### 6. Assembly version encoding quirk

Many BCL-companion NuGet packages (`Microsoft.Bcl.AsyncInterfaces`, `System.Diagnostics.DiagnosticSource` on `net462`) encode the patch in the assembly version: package `{X}.0.y` → asm `{X}.0.0.y`. But other .NET core targets often use `{M}.0.0.0` even at patch versions. When writing binding redirects, look at the actual deployed dll's asm version — don't guess.

### 7. Some host packages aren't published at the new version

These NuGet packages stopped tracking the runtime version and are not expected to appear at `{X}.0`:

- `runtime.{rid}.Microsoft.NETCore.DotNetHostResolver`  (frozen at 8.0.x)
- `runtime.{rid}.Microsoft.NETCore.DotNetHostPolicy`     (frozen at 8.0.x)
- `Microsoft.NETCore.Platforms`                          (frozen at 7.x; we alias to `9.0` by name only)

**Strategy:** reuse the most recent published version's alias and rename the alias to `{X}.0`, e.g.:

```dscript
{ id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver",
  version: core{X-1}0Version,
  osSkip: [ "macOS", "unix" ],
  alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.{X}.0" },
```

Verify on nuget.org only if you suspect Microsoft has resumed publishing these.

### 8. cgmanifest is auto-generated — don't hand-edit

`bxl.cmd` always passes `/generateCgManifestForNugets:...` (see `Shared/Scripts/bxl.ps1`). You don't need to manually edit `cg/nuget/cgmanifest.json`. On merge conflicts, accept either side and let the next bxl run regenerate.

---

# STAGE 1 — NugetSpecGenerator (small commit, 3 files)

Stage 1 must be merged and deployed (~1 week) before Stage 2 begins. Deployment lives in `Shared/Scripts/BuildXLLkgVersion.cmd` (and `BuildXLLkgVersionPublic.cmd`) — Stage 2's default-qualifier build needs an LKG `bxl.exe` that already knows about net{X}.

You *can* technically work around the wait (manually point at a dev bxl, juggle qualifiers), but it costs you intellisense in the IDE and adds friction with every build. The convention since net8 has been: land Stage 1, wait the week, then start Stage 2 cleanly.

This stage teaches the NuGet spec generator about the new framework moniker.

## Stage 1 Steps

### 1. `Public/Src/FrontEnd/Nuget/NugetFrameworkMonikers.cs`

a. Add a new property (near line ~110, after the previous version's property):
```csharp
/// <nodoc />
public PathAtom Net{X}0 { get; }                          // e.g., Net110
```

b. Register it (near line ~193, after the previous version's `Register` call):
```csharp
Net{X}0 = Register(stringTable, "net{X}.0", ".NETCoreApp{X}.0", NetCoreVersionHistory);
```

c. Append `Net{X}0` to the end of `NetCoreAppVersionHistory` (near line ~195).

> Don't confuse this with the old .NET Framework moniker. The existing `Net10` property (registered as `"net10"` / `".NETFramework1.0"`) is .NET Framework 1.0.

### 2. `Public/Src/FrontEnd/Nuget/NugetSpecGenerator.cs`

Bump the version constant:
```csharp
public const int SpecGenerationFormatVersion = {previous + 1};
```

### 3. `Public/Src/FrontEnd/UnitTests/Nuget/NuSpecGeneratorTests.cs`

a. Update `CurrentSpecGenVersion` to match the new `SpecGenerationFormatVersion`.
b. In all expected spec strings, add `"net{X}.0"` to qualifier `targetFramework` union types.
c. Add `case "net{X}.0":` in switch statements (follows the previous version's case).
d. Add `|| qualifier.targetFramework === "net{X}.0"` in `addIfLazy` conditions.
e. Run the tests — they will fail with hash mismatches. Copy the actual hash values from the test output into the 3 `CurrentSpecHash` constants.

## Stage 1 Verification

```powershell
bxl.cmd Test.BuildXL.FrontEnd.Nuget.dsc /server-
```

After the tests pass, present the diff and stop. Wait for the user to commit and merge.

> Stage 2 cannot begin until `BuildXLLkgVersion.cmd` has been bumped to a build containing the new `SpecGenerationFormatVersion`. If it hasn't, stop here and wait.

---

# STAGE 2 — Main .NET Support (large commit, ~35-40 files)

## Stage 2 Prerequisites

### Stage 1 deployment check

Before starting Stage 2, confirm that Stage 1 has rolled out into the LKG build (run from the repo root):

```powershell
git --no-pager log -1 --format='%h %s' -- Shared/Scripts/BuildXLLkgVersion.cmd
```

If the most recent commit to that file post-dates Stage 1's merge, you're good. If not, ask the user to wait.

### Branch setup

From the repo root:

```powershell
$user = ($env:USERNAME).ToLower()
git checkout -b dev/$user/net{X}-stage2 main
```

Per `.github/copilot-instructions.md`: branch naming is `dev/<username>/<feature>`. No commit/push without explicit user approval. No `git --amend`, `rebase`, or `force-push`.

## Stage 2 Steps

Follow the steps in order — the dependency chain matters and intermediate sanity checks catch issues early.

### Step 1: Add NuGet packages and runtime downloads

Nothing consumes these yet; this step just adds package references.

#### `config.nuget.dotnetcore.dsc`

Add the version constant at the top (lines ~6-12):
```dscript
const core{X}0Version = "{X}.0.{patch}";    // e.g., "11.0.1"
```

Add NuGet packages (copy the net{X-1} block and update versions/aliases):
```dscript
// .NET {X}
{ id: "Microsoft.NETCore.App.Ref",
  version: core{X}0Version,
  alias: "Microsoft.NETCore.App.Ref{X}0",
  filesToExclude: [r`analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll`] },
{ id: "Microsoft.NETCore.Platforms",
  version: core{X-1}0VersionPlatforms,
  alias: "Microsoft.NETCore.Platforms.{X}.0" },

// win-x64
{ id: "Microsoft.NETCore.App.Host.win-x64",
  version: core{X}0Version,
  osSkip: [ "macOS", "unix" ],
  alias: "Microsoft.NETCore.App.Host.win-x64.{X}.0" },
{ id: "Microsoft.NETCore.App.Runtime.win-x64",
  version: core{X}0Version,
  osSkip: [ "macOS", "unix" ],
  alias: "Microsoft.NETCore.App.Runtime.win-x64.{X}.0" },
{ id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver",
  version: core{X-1}0Version,
  osSkip: [ "macOS", "unix" ],
  alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.{X-1}.0" },
{ id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy",
  version: core{X-1}0Version,
  osSkip: [ "macOS", "unix" ],
  alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.{X-1}.0" },

// (same pattern for osx-x64 and linux-x64)
```

> Per [pitfall #7](#7-some-host-packages-arent-published-at-the-new-version), `DotNetHostResolver` / `DotNetHostPolicy` / `Platforms` packages aren't published at `{X}.0`. Keep their `version:` at the most recent published major and just rename the alias to `{X}.0`.

If you're bumping `System.Collections.Immutable` / `System.Threading.Tasks.Extensions` / `System.ValueTuple` at the same time (see [pitfall #2](#2-the-cascade-bump-trap)), do those bumps here too.

#### `config.nuget.aspNetCore.dsc`

Bump `const aspVersion` to `"{X}.0.{patch}"` (this becomes the default for all `Microsoft.Extensions.*` references throughout the repo).

Add version constants for net{X} ref packages:
```dscript
const asp{X}RefVersion = "{X}.0.{patch}";
const asp{X}RuntimeVersion = "{X}.0.{patch}";
```

Add package entries (copy the net{X-1} block):
```dscript
{ id: "Microsoft.AspNetCore.App.Ref",
  version: asp{X}RefVersion,
  alias: "Microsoft.AspNetCore.App.Ref.{X}.0.0" },
{ id: "Microsoft.AspNetCore.App.Runtime.win-x64",
  version: asp{X}RuntimeVersion,
  alias: "Microsoft.AspNetCore.App.Runtime.win-x64.{X}.0.0",
  filesToExclude: [
    r`runtimes/win-x64/lib/net{X}.0/Microsoft.Extensions.Logging.Abstractions.dll`,
    r`runtimes/win-x64/lib/net{X}.0/Microsoft.Extensions.Logging.dll`] },
// (same for linux-x64 and osx-x64)
```

#### `config.dsc` — package versions

Bump each package listed in [pitfall #2](#2-the-cascade-bump-trap) (each in its own block in this file). Search for the previous version's `"{X-1}.0.x"` lines and update to `"{X}.0.y"`.

#### `config.dsc` — runtime downloads

Add `Download` entries (copy the net{X-1} block, update version/URL/hash):
```dscript
{
    moduleName: "DotNet-Runtime.win-x64.{X}.0",
    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/{X}.0.{patch}/dotnet-runtime-{X}.0.{patch}-win-x64.zip",
    hash: "VSO0:000000000000000000000000000000000000000000000000000000000000000000",
    archiveType: "zip",
},
// (same for osx-x64 → .tar.gz / archiveType: "tgz", and linux-x64)
```

> **TIP:** Use the all-zeros placeholder hash above (`VSO0:` + 66 hex chars, all 0s). bxl validates the hash *format* (length + hex), so anything non-hex like `"USE_PLACEHOLDER"` will fail with a parse error before the download even starts. When bxl actually tries to download, the error gives you the real hash — copy it into the config and re-run.

#### Sanity check

```powershell
bxl.cmd -minimal /server-
```

Common failures here:
- Package not yet on the internal NuGet feed → use the latest available patch
- Hash mismatch → copy from the error

### Step 2: Create the new framework definition

#### New directory: `Public/Sdk/Public/Managed/Frameworks/net{X}/`

Create `module.config.dsc`:
```dscript
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Managed.Frameworks.Net{X}.0",
    projects: [
        f`net{X}.0.dsc`,
    ]
});
```

Create `net{X}.0.dsc` — copy from `net{X-1}.0.dsc` and update:
- `qualifier: {targetFramework: "net{X}.0"}`
- Runtime imports: `Microsoft.NETCore.App.Runtime.{platform}.{X}.0`
- `DotNetHostResolver` / `HostPolicy`: keep on `{X-1}.0` if `{X}.0` packages don't exist
- `crossgenProvider`: update to `.{X}.0` runtime packages
- `supportedRuntimeVersion: "v{X}.0"`
- `assemblyInfoTargetFramework: ".NETCoreApp,Version=v{X}.0"`
- `runtimeConfigVersion: "{X}.0.{patch}"` (same as `core{X}0Version`)
- `conditionalCompileDefines`: add `"NET{X}_0"` and `"NET{X}_0_OR_GREATER"`
- `createDefaultAssemblies: importFrom("Microsoft.NETCore.App.Ref{X}0")`

#### New directory: `Public/Sdk/SelfHost/Libraries/Dotnet-Runtime-{X}-External/`

Create `module.config.dsc` with three module declarations (win-x64, osx-x64, linux-x64), and the three matching `DotNet-Runtime.{platform}.dsc` files. The pattern is identical to the `Dotnet-Runtime-{X-1}-External` directory — copy and bump.

### Step 3: Update framework resolution and types

#### `Public/Sdk/Public/Managed/Shared/frameworks.dsc`
- Add `"net{X}.0"` to type `DotNetCoreVersion`
- Add `|| targetFramework === 'net{X}.0'` to `isDotNetCore()`
- Add `"net{X}.0"` to `CoreClrTargetFrameworks` type

#### `Public/Sdk/Public/Managed/Frameworks/frameworks.dsc`
- Add `case "net{X}.0": return importFrom("Sdk.Managed.Frameworks.Net{X}.0").framework;`

#### `Public/Sdk/Public/Managed/Frameworks/helpers.dsc`
- Add `else if (version === 'net{X}.0')` block with imports from `DotNet-Runtime-{X}.{platform}`
- Add `const tool{X}Template = getDotNetCoreToolTemplate("net{X}.0");`
- Add `case "net{X}.0": return tool{X}Template;` in `getCachedDotNetCoreToolTemplate`

### Step 4: Update all places where the .NET version is referenced

#### `Public/Sdk/SelfHost/BuildXL/web.dsc`
- Add `net{X}0` parameter to the `importPackage()` function signature
- Add `importFrom("Microsoft.AspNetCore.App.Ref.{X}.0.0")` calls
- Add `importFrom("Microsoft.AspNetCore.App.Runtime.{platform}.{X}.0.0")` calls
- Add `case "net{X}.0": return net{X}0();` to the switch

#### `Public/Sdk/Public/Managed/managedSdk.dsc`
Add compile defines for win/osx/linux (search for the existing `CompileDebugNet{X-1}Win` pattern):
```dscript
...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "net{X}.0" && qualifier.configuration === "debug", "CompileDebugNet{X}Win", "CompileWin"),
...addIf(qualifier.targetRuntime === "osx-x64" && qualifier.targetFramework === "net{X}.0" && qualifier.configuration === "debug", "CompileNet{X}Osx", "CompileOsx"),
...addIf(qualifier.targetRuntime === "linux-x64" && qualifier.targetFramework === "net{X}.0" && qualifier.configuration === "debug", "CompileNet{X}Linux", "CompileLinux"),
```

#### `Public/Sdk/Public/Managed/Testing/XUnit/xunitframework.dsc` (xUnit V1/V2)  AND  `Public/Sdk/SelfHost/BuildXL/Testing/XUnitV3/xunitv3framework.dsc` (xUnit V3, current)
- Add `case "net{X}.0": return importFrom("Sdk.Managed.Frameworks.Net{X}.0").withQualifier({targetFramework: "net{X}.0"}).framework;`
- In `xunitv3framework.dsc`, net{X}.0 shouldn't need anything special — the existing `isDotNetCore(qualifier.targetFramework)` switches handle it once `isDotNetCore` recognizes net{X}.0 (from Step 3).

#### `Public/Sdk/SelfHost/BuildXL/Testing/QTest/qtestFramework.dsc`
- Add `case "net{X}.0":` under the existing case list (maps to `frameworkCore30`)

#### `Public/Sdk/Public/Managed/Tools/Csc/csc.dsc`
In `getDotNetCoreVersion()`, add the latest-version check (reorder so the highest version is checked first):
```dscript
if (cscArguments.defines.some(e => e === "NET{X}_0")) { return "net{X}.0"; }
```

#### `Public/Src/Tools/SymbolDaemon/Tool.SymbolDaemon.dsc`
Check the current file and add a net{X}.0 entry if needed (the exact pattern varies version to version).

### Step 5: Add source resolver in `config.dsc`

Add to the resolvers array:
```dscript
{ kind: "SourceResolver", modules: [f`Public\Sdk\SelfHost\Libraries\Dotnet-Runtime-{X}-External\module.config.dsc`] }
```

### Step 6: Update qualifiers

#### `Public/Sdk/SelfHost/BuildXL/Qualifiers.dsc`

Add `"net{X}.0"` to these existing interfaces:
- `DefaultQualifier`
- `DefaultQualifierWithNet472`
- `AllSupportedQualifiers`
- `Net8PlusQualifier`

Add a new `Net{X}Qualifier` interface:
```dscript
@@public
export interface Net{X}Qualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net{X}.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}
```

> Don't add net{X}.0 to `Net{M}QualifierWithNet472` (intentionally pinned for MsBuild/VBCS tests), and don't add a `Net{X}QualifierWithNet472` — those tests stay on the current MsBuild-pinned version.

#### `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.dsc`
- Add `|| qualifier.targetFramework === "net{X}.0"` to `isDotNetCoreOrStandard` and `isDotNetCore`
- Add `&& qualifier.targetFramework !== "net{X}.0"` to `restrictTestRunToSomeQualifiers`
- Update PolySharp check: `&& qualifier.targetFramework !== "net{X}.0"`
- Search for any other `qualifier.targetFramework !== "net{X-1}.0"` patterns and add net{X}

#### `config.dsc` — named qualifiers section

Add (copy from net{X-1} entries):
```dscript
DebugNet{X}:              { configuration: "debug",   targetFramework: "net{X}.0", targetRuntime: "win-x64" },
DebugDotNetCoreMacNet{X}: { configuration: "debug",   targetFramework: "net{X}.0", targetRuntime: "osx-x64" },
DebugLinuxNet{X}:         { configuration: "debug",   targetFramework: "net{X}.0", targetRuntime: "linux-x64" },
ReleaseNet{X}:            { configuration: "release", targetFramework: "net{X}.0", targetRuntime: "win-x64" },
```

(Same for other named qualifiers if any exist.)

### Step 7: MsBuild test isolation (verify; do NOT extend net{X} here)

MsBuild and VBCS tests are intentionally isolated to the `Net{M}QualifierWithNet472` qualifier because MSBuild's deployment is fragile. Don't add net{X}.0 to that qualifier or to the MsBuild test's `targetFramework` switches.

What you **do** need:

#### `Public/Src/FrontEnd/UnitTests/MsBuild/Test.BuildXL.FrontEnd.MsBuild.dsc`

In the `MsBuildGraphBuilder` deployment fallback, add net{X}.0 to the list of frameworks that fall back to the pinned graph builder (search for `MsBuildGraphBuilder.withQualifier({targetFramework:` to find the block):

```dscript
contents: [qualifier.targetFramework === "net8.0" || qualifier.targetFramework === "net{X}.0"
    ? importFrom("BuildXL.Tools").MsBuildGraphBuilder.withQualifier({targetFramework: "net{M}.0"}).deployment
    : importFrom("BuildXL.Tools").MsBuildGraphBuilder.deployment]
```

#### `Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config`

When you bump any packages in Step 1 (`Microsoft.Bcl.AsyncInterfaces`, `System.Diagnostics.DiagnosticSource`, `System.Collections.Immutable`, `System.Threading.Tasks.Extensions`, etc.), update the matching binding redirects in this file to point at the new assembly version. See pitfalls [#5](#5-hand-maintained-execonfig-dsc-redirect-files-dont-auto-regenerate) and [#6](#6-assembly-version-encoding-quirk) for how to figure out the asm version from a package version.

### Step 8: Update deployment packages

#### `Public/Src/Deployment/NugetPackages.dsc`

Add qualifier objects:
```dscript
const net{X}PackageQualifier      = { targetFramework: "net{X}.0", targetRuntime: "win-x64" };
const net{X}LinuxPackageQualifier = { targetFramework: "net{X}.0", targetRuntime: "linux-x64" };
```

Then add `.withQualifier(net{X}PackageQualifier).dll` lines for every library already listed. Search for `net{X-1}PackageQualifier` and duplicate each occurrence.

> **Style note:** The file mostly uses an interleaved-per-assembly style (one line per qualifier per assembly, grouped under comment headers like `// BuildXL.Utilities.Branding`). One section (the cache aggregator block, around lines 376-402 as of net10) uses a per-version-block style instead — net472 block, blank line, net8 block, blank line, etc. In that section, add net{X} as its own trailing block separated by a blank line, **not** interleaved with net{X-1}.

#### `Public/Src/Deployment/cache.NugetPackages.dsc`

Add const declarations at the top:
```dscript
const net{X}WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net{X}.0", targetRuntime: "win-x64" });
const net{X}OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net{X}.0", targetRuntime: "osx-x64" });
// ... same for MemoizationStore, DistributedCacheHost, CacheLogging
```

Then add `.dll` references everywhere the previous version is referenced.

### Step 9: Other files

#### `Public/Src/Cache/ContentStore/Hashing/PublicAPI/net{X}.0/`
- Create the directory
- Copy `PublicAPI.Shipped.txt` from the `net{X-1}.0` directory (~772 lines, public API surface)
- Create `PublicAPI.Unshipped.txt` with a single line: `#nullable enable`

#### `Shared/Scripts/bxl.ps1`
- Add `"net{X}.0"` to `[ValidateSet()]` for the `$DeployRuntime` parameter
- Add `/vsTargetFramework:net{X}.0` to the `-vs` arguments
- Add `elseif` blocks for both Release and Debug:
```powershell
elseif ($DeployRuntime -eq "net{X}.0") { $AdditionalBuildXLArguments += "/q:ReleaseNet{X}" }
elseif ($DeployRuntime -eq "net{X}.0") { $AdditionalBuildXLArguments += "/q:DebugNet{X}" }
```

#### BCL-absorbed packages (only if needed)

If [pitfall #3](#3-new-bcl-absorptions-may-force-cs0433-collisions) applies to net{X} — i.e., the .NET {X} release notes flag a package BuildXL imports as having been absorbed into the BCL — wrap each such import to be skipped on net{X}. Use whichever shape matches the surrounding code:

```dscript
// inline list shape
...(qualifier.targetFramework === "net{X}.0" ? [] : [importFrom("Some.Package").pkg]),

// addIf shape (preferred when the import is a single item in an array)
...addIf(qualifier.targetFramework !== "net{X}.0",
    importFrom("Some.Package").pkg),
```

The skip belongs in the .dsc file that *imports* the package, not in a central spec. For net10, the three skips went to `BuildXLSdk.Packages.dsc`, `BuildXL.Native.dsc`, and `BuildXL.Processes.dsc` (see pitfall #3 for the package-to-file mapping).

Also surface to the user: the `#if NET{X}_0_OR_GREATER` C# code changes from [Step 10a](#step-10a-conditional-c-code-paths-only-if-a-package-collides-with-bcl-on-netx) typically pair with these skips.

#### C# warning suppressions

The .NET {X} compiler will likely flag some newly-obsolete APIs in the code. For each new warning that surfaces under `/q:DebugNet{X}`:

1. Identify the warning code, file, and line.
2. Look up what the warning is recommending (the .NET {X} release notes / SDK obsoletion docs usually explain).
3. **Surface the warning to the user with both options** before doing anything:
   - **Option A — fix the code** by migrating to the recommended replacement API. Usually preferable, especially if the call site is small or the replacement is straightforward.
   - **Option B — suppress** with `#pragma warning disable XXXX` … `#pragma warning restore XXXX` around the call site, with a comment explaining why. Appropriate when the call site is deeply load-bearing, the replacement isn't drop-in, or the suppression is intentional for compatibility with older targets.

Don't blindly suppress. The default action is to ask. Past upgrades have done a mix of both:
- net9 added several `#pragma warning disable SYSLIB0014` blocks (e.g., in `App/Bxl/Program.cs`, `Engine/Dll/Engine.cs`, `Tools/ServicePipDaemon/ServicePipDaemon.cs`, `Tools/SymbolDaemon/Program.cs`) because the deprecated `ServicePointManager` setting was still being used by intentionally-obsolete code paths the team chose to keep.
- net8 fixed some `ServicePoint` deprecations in code rather than suppress them.

Suppressions added for previous versions are still in the tree; only revisit them if their associated warnings flare again on net{X}.

#### `cg/nuget/cgmanifest.json`

Don't hand-edit. bxl.cmd will regenerate via `/generateCgManifestForNugets`. On merge conflicts: accept either side, run any bxl build, commit the result.

#### `config.microsoftInternal.dsc`

If `main` has bumped `Microsoft.ComponentDetection.Contracts` or other internal packages while you were working, those may have auto-merged. Run any bxl build to validate; cgmanifest will sync.

### Step 10a: Conditional C# code paths (only if a package collides with BCL on net{X})

net10 needed this for `System.Linq.Async` (`AsyncEnumerable.Create` / `yield`). net{X+1}+ may need it for whichever packages are absorbed into the new BCL.

Pattern: wrap the legacy code in `#if !NET{X}_0_OR_GREATER` (keeping old net8/net9 behavior intact) and add a `#else` branch using BCL-native APIs. Hoist any shared setup (e.g., `Task.Run`) outside the `#if` so it isn't duplicated. Files to look at first (these had the pattern in net10):

- `Public/Src/Cache/ContentStore/Interfaces/Extensions/AsyncEnumerableExtensions.cs`
- `Public/Src/Cache/MemoizationStore/Interfaces/Sessions/MemoizationSessionExtensions.cs`
- `Public/Src/Cache/VerticalStore/MemoizationStoreAdapter/MemoizationStoreAdapterCache.cs`

### Step 10b: Companion repo Domino.DistributedBuildTest

The pipeline's "Building Test Project Distributed" step clones `https://dev.azure.com/mseng/Domino/_git/Domino.DistributedBuildTest` at the commit pinned in `Shared/Scripts/BuildDistributedTest.cmd` (`TEST_COMMITID`). That repo also needs net{X} wiring.

Order matters — see [pitfall #1](#1-companion-repo-pr) for why. The companion repo's PR must be opened **after** Stage 1 has rolled into LKG (so its own bxl knows about net{X}), and **before** this repo's Stage 2 PR merges (so `TEST_COMMITID` can be bumped to its merge SHA).

1. Create branch `dev/<user>/net{X}-stage2` in the companion repo, mirroring this stage:
   - `TestSolution/PrepSdk.cmd`: add net{X} SDK copy
   - `TestSolution/config.dsc`: add net{X} Download entry, qualifier, etc.

   Look at the analogous commit for net{X-1} as the template.

2. PR that branch to its main/master.
3. After it merges, update `TEST_COMMITID` in `Shared/Scripts/BuildDistributedTest.cmd` here to the new master SHA. Add this as a separate commit on the Stage 2 branch.

## Stage 2 Verification

Run these in order. Each one is fast (after the first prime-the-cache run). Don't proceed to the next step until the previous is green.

### 1. Minimal smoke test (default qualifier)
```powershell
bxl.cmd -minimal /server-
```
If this fails, you have a packaging error.

### 2. Minimal smoke test (new framework)
```powershell
bxl.cmd -minimal /q:DebugNet{X} /server-
```

### 3. Full default-qualifier build
```powershell
bxl.cmd /server-
```
Should pass since the default qualifier hasn't changed.

### 4. Full new-framework build
```powershell
bxl.cmd /q:DebugNet{X} /server-
```

This will likely surface a few issues. Fix one at a time. Common failure patterns:

- **BCL type collisions (CS0433)** → see [Step 9 → BCL-absorbed packages](#bcl-absorbed-packages-only-if-needed) and [Step 10a](#step-10a-conditional-c-code-paths-only-if-a-package-collides-with-bcl-on-netx)
- **Missing NuGet packages** → use the latest available
- **Hash mismatches in downloads** → copy the correct hash from the error
- **New compiler warnings** → see [Step 9 → C# warning suppressions](#c-warning-suppressions) — surface to user, don't just suppress

### 5. Canary tests (run after Step 4 passes and after every binding-redirect change)

```powershell
bxl.cmd Test.BuildXL.Cache.MemoizationStoreAdapter.dsc /q:ReleaseNet472 /server-
```
> Catches net472 `System.Collections.Immutable` binding-redirect manifest mismatches ([pitfall #4](#4-net472-systemcollectionsimmutable-manifest-mismatch-trap)).

```powershell
bxl.cmd Test.BuildXL.FrontEnd.MsBuild.dsc Test.Tool.VBCSCompilerLogger.dsc /q:Release /server-
```
> Catches stale `msbuild.exe.config` redirects ([pitfall #5](#5-hand-maintained-execonfig-dsc-redirect-files-dont-auto-regenerate)). These tests are what RunCheckInTests / the pipeline runs that `-minimal` doesn't.

```powershell
bxl.cmd Test.BuildXL.FrontEnd.MsBuild.dsc /q:ReleaseNet8 /server-
```
> Same as above for the ReleaseNet8 qualifier.

**Watch for this symptom in cache tests:**

> `"Service still not running after waiting 5 seconds"`

This is a silent assembly-binding failure — the service host process dies on startup with a `FileLoadException` that gets swallowed, and the test only sees a startup timeout. The visible error is misleading; turn on Fusion logging to find the real cause:

1. Run `fuslogvw.exe` (ships with the Windows SDK) elevated.
2. Settings → "Log bind failures to disk" (and pick a log path).
3. Re-run the failing pip. Refresh fuslogvw to see which assembly failed and what version was requested vs deployed.
4. Map the answer back to a binding-redirect / package-version mismatch.

### 6. Full pre-checkin validation (optional, very slow)
```cmd
RunCheckInTests.cmd
```
The pipeline runs this. If you've passed step 5 you'll likely pass this too, but it's the closest thing to the cloud build.

## Merge / Rebase Strategy

Main moves while you work. When you sync, expect these mechanical conflicts:

### Package version bumps in `config.dsc`, `config.nuget.dotnetcore.dsc`, `config.nuget.aspNetCore.dsc`

**Resolution rule:** per package, pick whichever side's version yields the actual deployed assembly. Concretely:
- If you bumped X to `{X}.0.y` and main bumped X to `{X-1}.0.(z+1)`, **your bump wins**.
- If main bumped some package you didn't touch (e.g., `System.Reflection.Metadata 9.0.15 → 9.0.16`), **theirs wins**.

### Binding redirects in `BuildXLSdk.dsc`, `xunitv3framework.dsc`, `Tool.MsBuildGraphBuilder.dsc`, `Test.Tool.MsBuildGraphBuilder.dsc`, `msbuild.exe.config`

Same rule per redirect: the redirect target must match the merged package version.

### `cgmanifest.json`

Accept either side; bxl will regenerate.

---

## Out of Scope for These 2 Commits

- Making net{X} the default qualifier (separate commit, done later after validation)
- Pipeline YAML updates under `.azdo/` for rolling/PR builds with the new qualifier
- `SBOMUtilities` .dsc updates (separate if needed)
- Removing old .NET version support (net{X-2}) — separate effort

---

## Reference: Key Files and Their Roles

### Config / package files
- `config.dsc` — root config: resolvers, downloads, named qualifiers, package versions
- `config.nuget.dotnetcore.dsc` — NuGet packages for .NET Core runtime
- `config.nuget.aspNetCore.dsc` — NuGet packages for ASP.NET Core
- `config.microsoftInternal.dsc` — Microsoft-internal packages (rarely changed during net{X} add)

### Framework definition (new dirs created per version)
- `Public/Sdk/Public/Managed/Frameworks/net{X}/` — framework spec and module config
- `Public/Sdk/SelfHost/Libraries/Dotnet-Runtime-{X}-External/` — runtime wrapper modules

### Type system / qualifiers
- `Public/Sdk/SelfHost/BuildXL/Qualifiers.dsc` — all qualifier interfaces
- `Public/Sdk/Public/Managed/Shared/frameworks.dsc` — `DotNetCoreVersion` type, `isDotNetCore()`

### SDK logic
- `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.dsc` — `isDotNetCore`, test restrictions, PolySharp, binding redirects
- `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.Packages.dsc` — central package list (one location where BCL-absorption skips can land; others land in per-component .dsc files)
- `Public/Sdk/Public/Managed/managedSdk.dsc` — compile defines per platform/framework
- `Public/Sdk/Public/Managed/Frameworks/frameworks.dsc` — framework resolution switch
- `Public/Sdk/Public/Managed/Frameworks/helpers.dsc` — runtime tool templates
- `Public/Sdk/Public/Managed/Tools/Csc/csc.dsc` — C# compiler version detection

### Testing
- `Public/Sdk/Public/Managed/Testing/XUnit/xunitframework.dsc` — xUnit V1/V2 framework mapping (legacy)
- `Public/Sdk/SelfHost/BuildXL/Testing/XUnitV3/xunitv3framework.dsc` — xUnit V3 framework mapping (current); has binding redirects
- `Public/Sdk/SelfHost/BuildXL/Testing/QTest/qtestFramework.dsc` — QTest framework mapping

### Hand-maintained binding-redirect files (update when bumping packages)
- `Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config` — critical: `msbuild.exe` spawned by tests
- `Public/Src/Tools/Tool.MsBuildGraphBuilder/Tool.MsBuildGraphBuilder.dsc`
- `Public/Src/Tools/UnitTests/MsBuildGraphBuilder/Test.Tool.MsBuildGraphBuilder.dsc`

### Web
- `Public/Sdk/SelfHost/BuildXL/web.dsc` — ASP.NET Core framework packages

### Deployment
- `Public/Src/Deployment/NugetPackages.dsc` — BuildXL NuGet package contents
- `Public/Src/Deployment/cache.NugetPackages.dsc` — cache NuGet package contents

### NuGet frontend (Stage 1)
- `Public/Src/FrontEnd/Nuget/NugetFrameworkMonikers.cs` — framework moniker registry
- `Public/Src/FrontEnd/Nuget/NugetSpecGenerator.cs` — spec generation version
- `Public/Src/FrontEnd/UnitTests/Nuget/NuSpecGeneratorTests.cs`

### Scripts
- `Shared/Scripts/bxl.ps1` — build script with deploy runtime options
- `Shared/Scripts/BuildDistributedTest.cmd` — pins `TEST_COMMITID` for the companion repo
- `Shared/Scripts/BuildXLLkgVersion.cmd` — LKG bxl.exe version; Stage 2 prerequisite

### Other
- `Public/Src/Cache/ContentStore/Hashing/PublicAPI/net{X}.0/` — API compatibility files (new dir)
- `cg/nuget/cgmanifest.json` — auto-generated; leave alone
