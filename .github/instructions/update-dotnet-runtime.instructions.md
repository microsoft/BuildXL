# Updating .NET Runtime Dependencies

Use this skill when the user asks to update .NET runtime packages (e.g., `Microsoft.NETCore.App.Runtime.*`, ASP.NET Core, or related System.* packages).

## Overview

BuildXL pins .NET runtime package versions across several files. An update requires bumping version strings **and** assembly binding redirect versions in lockstep. Forgetting the binding redirects causes `FileLoadException` at test time on .NET Framework (net472) targets.

## Step 1: Determine the latest stable versions

Query NuGet for the latest stable (non-preview) versions of each major .NET release used by the repo:

- **8.0.x**: `https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.win-x64/index.json`
- **9.0.x**: same URL — look for the highest `9.0.*` entry without `-preview`/`-rc`

## Step 2: Update NuGet package versions

Update these files, replacing the old version strings with the new ones:

### `config.nuget.dotnetcore.dsc`
- `core80Version` constant (e.g., `"8.0.26"` → `"8.0.27"`)
- `core90Version` constant (e.g., `"9.0.15"` → `"9.0.16"`)
- Hardcoded version strings for `System.Formats.Asn1`, `System.Text.Json`, and `System.Collections.Immutable`

### `config.nuget.aspNetCore.dsc`
- `aspVersion`, `asp8RefVersion`, `asp8RuntimeVersion`, `asp9RefVersion`, `asp9RuntimeVersion`

### `config.dsc`
- All packages with hardcoded versions matching the old version (e.g., `System.Diagnostics.DiagnosticSource`, `System.Reflection.Metadata`, `System.Threading.Tasks.Dataflow`, `Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Channels`, `System.IO.Hashing`, `System.IO.Pipelines`, `System.Text.Encodings.Web`, `System.Security.Cryptography.Xml`, `System.Security.Cryptography.Pkcs`, `Microsoft.Bcl.Cryptography`, `System.Reflection.MetadataLoadContext`, `System.Resources.Extensions`, `System.CodeDom`, `System.Text.Encoding.CodePages`, `Microsoft.Extensions.Logging.Abstractions`, `System.Formats.Nrbf`)
- **Do NOT change** packages with the `ForVBCS` alias — those stay at their pinned older versions intentionally.

### `Public/Sdk/Public/Managed/Frameworks/net8/net8.0.dsc`
- `runtimeConfigVersion` (e.g., `"8.0.26"` → `"8.0.27"`)

### `Public/Sdk/Public/Managed/Frameworks/net9/net9.0.dsc`
- `runtimeConfigVersion` (e.g., `"9.0.15"` → `"9.0.16"`)

### `cg/nuget/cgmanifest.json`
- Bulk-replace all occurrences of the old 8.0.x and 9.0.x version strings with the new ones. This file has many entries (50+); use a bulk find-and-replace.

## Step 3: Update assembly binding redirects

.NET NuGet package version `X.Y.Z` maps to assembly version `X.Y.0.Z`. For example, NuGet `9.0.15` → assembly `9.0.0.15`, NuGet `9.0.16` → assembly `9.0.0.16`.

Update `oldVersion` and `newVersion` in all binding redirect entries across these files:

### `Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config`
This is the **custom MSBuild config for net472 tests**. It contains XML `<bindingRedirect>` entries for many assemblies. Bulk-replace the old assembly version (e.g., `9.0.0.15`) with the new one (e.g., `9.0.0.16`) in both `oldVersion` and `newVersion` attributes.

### `Public/Sdk/SelfHost/BuildXL/BuildXLSdk.dsc`
- `bxlBindingRedirects()` function — update `Microsoft.Bcl.AsyncInterfaces` redirect

### `Public/Sdk/SelfHost/BuildXL/Testing/XUnitV3/xunitv3framework.dsc`
- Update redirects for `System.Collections.Immutable` and `Microsoft.Bcl.AsyncInterfaces`

### `Public/Src/Tools/Tool.MsBuildGraphBuilder/Tool.MsBuildGraphBuilder.dsc`
- Update `Microsoft.Bcl.AsyncInterfaces` redirect

### `Public/Src/Tools/UnitTests/MsBuildGraphBuilder/Test.Tool.MsBuildGraphBuilder.dsc`
- Update `System.Text.Json` redirect

## Step 4: Verify no stale references

Run these searches to ensure nothing was missed:

```bash
# Search for old NuGet versions in .dsc files
grep -r '"OLD_VERSION"' --include="*.dsc" .

# Search for old assembly versions in .dsc and .config files
grep -r 'OLD_ASSEMBLY_VERSION' --include="*.dsc" --include="*.config" .
```

Replace `OLD_VERSION` with the version you're upgrading FROM (e.g., `9.0.15`) and `OLD_ASSEMBLY_VERSION` with its assembly equivalent (e.g., `9.0.0.15`).

## Step 5: Build and test

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

## Common failure modes

| Symptom | Cause | Fix |
|---------|-------|-----|
| `FileLoadException: Could not load 'System.Threading.Tasks.Dataflow, Version=X.Y.0.Z'` | Binding redirect in `msbuild.exe.config` points to old assembly version | Update `msbuild.exe.config` binding redirects |
| `MissingMethodException` on `ImmutableArray<T>` | Two copies of `System.Collections.Immutable` with different assembly versions loaded | Check `runtimeContentToSkip` and `ForVBCS` aliases are correct |
| Tests pass but expect wrong exception type | MSBuild subprocess crashes with `FileLoadException` instead of the expected error | Binding redirect mismatch in `msbuild.exe.config` |

## Key design notes

- **`ForVBCS` packages**: Some packages have a `ForVBCS` alias pinned to an older version (e.g., `System.Collections.Immutable.ForVBCS` at `8.0.0`). These exist because the VBCSCompiler runs in the context of older VS/MSBuild installations. **Do not update these.**
- **`msbuild.exe.config`**: This is a custom copy of MSBuild's config with manually maintained binding redirects. It's deployed alongside `MSBuild.exe` for net472 test scenarios. Its redirects must match the deployed assembly versions.
- **Version constants**: `config.nuget.dotnetcore.dsc` uses `core80Version` and `core90Version` constants that are referenced by many package entries. Updating the constant automatically updates all dependent packages.
- **`cgmanifest.json`**: This is the Component Governance manifest. It must list the exact versions of all NuGet packages used, for compliance tracking. It will get updated as a side effect of running a validation build.
