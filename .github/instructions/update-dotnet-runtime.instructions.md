# Updating .NET Runtime Dependencies

Use this skill when the user asks to update .NET runtime packages (e.g., `Microsoft.NETCore.App.Runtime.*`, ASP.NET Core, or related System.* packages).

## Overview

BuildXL pins .NET runtime package versions across several files. An update requires bumping version strings **and** assembly binding redirect versions in lockstep. Forgetting the binding redirects causes `FileLoadException` at test time on .NET Framework (net472) targets.

After reviewing the release notes below, the helper updates the runtime/ASP.NET version constants and runtime URLs:

```cmd
powershell -NoProfile -File Shared\Scripts\updateDotNet.ps1
```

Normal invocation applies those edits, then offers an optional archive preparation build. Supporting-package pins, framework runtime configuration, binding redirects, and hashes remain manual. This optional build does not replace the checklist and validation steps below.

## Step 1: Determine the latest stable versions

The helper reads the .NET download pages for the latest versions and the official release metadata for direct download URLs. Versions can also be checked on NuGet:

- **8.0.x**: `https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.win-x64/index.json`
- **9.0.x**: same URL — look for the highest `9.0.*` entry without `-preview`/`-rc`
- **10.0.x**: same URL — look for the highest `10.0.*` entry without `-preview`/`-rc`

### Review release notes and breaking changes

Before editing, read Microsoft's official .NET and ASP.NET Core release notes,
breaking-change documentation, and known issues for each version being updated.
Cover the releases between the currently pinned and target versions, including
intermediate releases when skipping patches. Check for changes relevant to
BuildXL's runtime APIs, dependencies, hosting, platform behavior, and performance.

Use these findings to guide the update and diagnose any build, test, or runtime
issues it exposes. Resolve relevant compatibility issues at their root cause
rather than bypassing failing checks or changing build modes to hide them.

Alert the user to relevant or concerning breaking changes, known issues, and
potential regressions, even if local validation passes. Include the affected
versions, links to the official notes, the likely BuildXL impact, and any fix
or unresolved risk. Distinguish confirmed impact from concerns that still need
validation; if the notes cannot be accessed, report that gap explicitly.

## Step 2: Update NuGet package versions

Review the constants changed by the helper and manually update the remaining package pins and framework configuration:

### `config.nuget.dotnetcore.dsc`
- `core80Version` constant (e.g., `"8.0.26"` → `"8.0.27"`)
- `core90Version` constant (e.g., `"9.0.15"` → `"9.0.16"`)
- `core100Version` constant (e.g., `"10.0.8"` → `"10.0.9"`)
- Hardcoded version strings for `System.Formats.Asn1`, `System.Text.Json`, and `System.Collections.Immutable`
- Update the comment referencing the latest HostResolver/HostPolicy 8.0.x version

### `config.nuget.aspNetCore.dsc`
- `aspVersion`, `asp8RefVersion`, `asp8RuntimeVersion`, `asp9RefVersion`, `asp9RuntimeVersion`, `asp10RefVersion`, `asp10RuntimeVersion`

### `config.dsc`
- Serviced pins: `System.Diagnostics.DiagnosticSource`, `System.Reflection.Metadata`, `System.Formats.Nrbf`, `System.Threading.Tasks.Dataflow`, `System.IO.Hashing`, `Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Channels`, `System.Security.Cryptography.Xml`, `System.Text.Encodings.Web`, `System.Security.Cryptography.Pkcs`, `Microsoft.Bcl.Cryptography`, `System.Reflection.MetadataLoadContext`, `System.Resources.Extensions`, `System.IO.Pipelines`, `System.CodeDom`, `System.Text.Encoding.CodePages`, and `Microsoft.Extensions.Logging.Abstractions`
- Include older pins such as `System.Security.Cryptography.ProtectedData`, `System.Configuration.ConfigurationManager`, and `System.Diagnostics.EventLog`; not every previous pin necessarily matches the old runtime patch.
- **Do NOT change** packages with the `ForVBCS` alias — those stay at their pinned older versions intentionally.

Check the published versions of supporting packages separately; they do not necessarily publish every runtime patch. Keep each changed package and its corresponding redirect aligned, and do not upgrade unrelated packages.

### `Public/Sdk/Public/Managed/Frameworks/net8/net8.0.dsc`
- `runtimeConfigVersion` (e.g., `"8.0.26"` → `"8.0.27"`)

### `Public/Sdk/Public/Managed/Frameworks/net9/net9.0.dsc`
- `runtimeConfigVersion` (e.g., `"9.0.15"` → `"9.0.16"`)

### `Public/Sdk/Public/Managed/Frameworks/net10/net10.0.dsc`
- `runtimeConfigVersion` (e.g., `"10.0.8"` → `"10.0.9"`)

### `cg/nuget/cgmanifest.json`
- Do not hand-edit. The normal `bxl.cmd` wrapper regenerates it from the resolved packages. Review the generated diff after building.

## Step 3: Update assembly binding redirects

For the serviced assemblies, NuGet package version `X.Y.Z` normally maps to assembly version `X.Y.0.Z`. Confirm the actual deployed net472 assembly identities when validating, and update only the redirects for those assemblies.

Manually update `oldVersion` and `newVersion` for the changed assemblies across these files:

### `Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config`
This is the **custom MSBuild config for net472 tests**. Keep its metadata/resource and other serviced redirects aligned with the deployed packages. MSBuild's own assembly identity, such as `Microsoft.Build.Utilities.Core` 15.1.0.0, is not a .NET servicing patch version.

### `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.dsc`
- `cacheBindingRedirects()` function — update `Microsoft.Bcl.AsyncInterfaces` redirect

### `Public/Sdk/SelfHost/BuildXL/Testing/XUnitV3/xunitv3framework.dsc`
- Update redirects for `System.Collections.Immutable` and `Microsoft.Bcl.AsyncInterfaces`

### `Public/Src/Tools/Tool.MsBuildGraphBuilder/Tool.MsBuildGraphBuilder.dsc`
- Update `Microsoft.Bcl.AsyncInterfaces` redirect

### `Public/Src/Tools/UnitTests/MsBuildGraphBuilder/Test.Tool.MsBuildGraphBuilder.dsc`
- Update `System.Text.Json` to the selected package's assembly version, even if the previous redirect used an older major.

## Step 4: Update Download resolver runtime URLs and hashes

The `config.dsc` file contains Download resolver entries for standalone .NET runtimes (used for Linux builds and certain test scenarios). Update the URLs and content hashes for all **12 entries** (four platforms × three versions):

### Update URLs
The helper updates the version in each runtime URL:
- `DotNet-Runtime.win-x64.10.0`, `DotNet-Runtime.osx-x64.10.0`, `DotNet-Runtime.osx-arm64.10.0`, `DotNet-Runtime.linux-x64.10.0`
- `DotNet-Runtime.win-x64.9.0`, `DotNet-Runtime.osx-x64.9.0`, `DotNet-Runtime.osx-arm64.9.0`, `DotNet-Runtime.linux-x64.9.0`
- `DotNet-Runtime.win-x64.8.0`, `DotNet-Runtime.osx-x64.8.0`, `DotNet-Runtime.osx-arm64.8.0`, `DotNet-Runtime.linux-x64.8.0`

Also update the comment line above each group (e.g., `// DotNet Core Runtime 10.0.8` → `// DotNet Core Runtime 10.0.9`).

### Update VSO0 content hashes manually
After updating URLs (leave the old hashes temporarily), run a filtered build to let BuildXL compute and report the correct hashes:

The helper's optional build uses `/f:tag='extract'`, selecting all tagged archive extractions and their download dependencies, not just .NET runtimes. The output filter below limits the work to the .NET runtime archives. Either can report the expected hash mismatches; copying the reported hashes remains manual.

```powershell
# Changed downloads fail with "Invalid content" errors showing the actual hashes.
cmd /c "call bxl.cmd /f:output='*\frontend\Download\DotNet-Runtime*' /server-"
```

The error output will show lines like:
```
Invalid content for url '...dotnet-runtime-10.0.9-win-x64.zip'. The content hash was expected to be: 'VSO0:OLD_HASH' but the downloaded files hash was 'VSO0:NEW_HASH'.
```

Match each URL to its `config.dsc` entry and manually copy the `NEW_HASH` into that entry's `hash:` field. Check all 12 entries; unchanged archives need no new hash.

After updating the reported hashes, verify with:
```powershell
cmd /c "call bxl.cmd /f:output='*\frontend\Download\DotNet-Runtime*' /server-"
# Should now succeed with "Build Succeeded"
```

Inspect `BuildXL.err` in the printed log directory if there are other failures; do not mistake an unrelated failure for an expected hash mismatch.

## Step 5: Verify no stale references

Run these searches to ensure nothing was missed:

```bash
# Search for old NuGet versions in .dsc files
grep -r '"OLD_VERSION"' --include="*.dsc" .

# Search for old assembly versions in .dsc and .config files
grep -r 'OLD_ASSEMBLY_VERSION' --include="*.dsc" --include="*.config" .
```

Replace `OLD_VERSION` with the version you're upgrading FROM (e.g., `9.0.15`) and `OLD_ASSEMBLY_VERSION` with its assembly equivalent (e.g., `9.0.0.15`).

## Step 6: Build and test

Run a minimal build first, then the VBCSCompilerLogger and MSBuild tests (the most sensitive tests for binding redirect issues):

```powershell
# Minimal build — verifies packages resolve and compile
cmd /c "call bxl.cmd -minimal /server-"

# VBCSCompilerLogger test — exercises MSBuild net472 with binding redirects
cmd /c "call bxl.cmd Test.Tool.VBCSCompilerLogger.dsc /server-"

# MSBuild frontend and graph builder tests — exercises msbuild.exe.config binding redirects
cmd /c "call bxl.cmd Test.BuildXL.FrontEnd.MsBuild.dsc /server-"
cmd /c "call bxl.cmd Test.Tool.MsBuildGraphBuilder.dsc /server-"
```

Use a long timeout (300+ seconds) for builds.

Confirm the expected tests actually ran or have matching cached results; an empty selection is not validation.

## Common failure modes

| Symptom | Cause | Fix |
|---------|-------|-----|
| `FileLoadException: Could not load 'System.Threading.Tasks.Dataflow, Version=X.Y.0.Z'` | Binding redirect in `msbuild.exe.config` points to old assembly version | Update `msbuild.exe.config` binding redirects |
| `MissingMethodException` on `ImmutableArray<T>` | Two copies of `System.Collections.Immutable` with different assembly versions loaded | Check `runtimeContentToSkip` and `ForVBCS` aliases are correct |
| Tests pass but expect wrong exception type | MSBuild subprocess crashes with `FileLoadException` instead of the expected error | Binding redirect mismatch in `msbuild.exe.config` |

## Key design notes

- **`ForVBCS` packages**: Some packages have a `ForVBCS` alias pinned to an older version (e.g., `System.Collections.Immutable.ForVBCS` at `8.0.0`). These exist because the VBCSCompiler runs in the context of older VS/MSBuild installations. **Do not update these.**
- **`msbuild.exe.config`**: This is a custom copy of MSBuild's config with manually maintained binding redirects. It's deployed alongside `MSBuild.exe` for net472 test scenarios. Its redirects must match the deployed assembly versions.
- **Version constants**: `config.nuget.dotnetcore.dsc` uses `core80Version`, `core90Version`, and `core100Version` constants that are referenced by many package entries. Updating the constant automatically updates all dependent packages.
- **`cgmanifest.json`**: This is the Component Governance manifest. It must list the exact versions of all NuGet packages used, for compliance tracking. It will get updated as a side effect of running a validation build.
- **Download resolver runtime URLs**: These standalone runtime downloads are used for Linux builds and certain test scenarios. They must be kept in sync with the NuGet package versions. The "run build to fail, then update hashes" workflow is the fastest and most reliable approach — BuildXL computes VSO hashes as it downloads and reports mismatches with the exact correct value.
