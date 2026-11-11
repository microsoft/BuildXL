---
description: "Add a new .NET target framework to BuildXL, or finish its preview-to-GA transition. Use the two-stage process for a new major version."
tools:
  - powershell
---

# Add a New .NET Version to BuildXL

Adding a target framework is not automatically a shared-dependency upgrade.
Keep existing targets working, make the new version opt-in, and validate both
the compiler's references and the binaries actually deployed.

Do not commit, push, rewrite history, change the default framework, or bypass
build failures without the user's explicit approval.

## Establish the scope

For a new major version, ask which stage the user wants if it is unclear:

- **Stage 1:** teach the NuGet spec generator the new framework moniker.
  Merge and deploy this before Stage 2.
- **Stage 2:** add packages, framework definitions, qualifiers, deployments,
  and validation for the new target.

For an existing preview/RC target moving to GA, use the smaller
[Preview-to-GA checklist](#preview-to-ga-checklist); do not repeat Stage 1.

In the steps below, `{X}` is the new major, `net{X}.0` is its TFM, and
`{version}` is the exact runtime release, including a preview suffix if needed.
The C# moniker property is `Net{X}0` (`Net110` for `net11.0`), not the legacy
`Net10` property for .NET Framework 1.0.

Before editing, record the current branch/HEAD, dirty files, requested version,
default qualifier, supported frameworks/platforms, and MSBuild/VBCS-pinned
qualifier from the current tree. Do not rely on a snapshot from a previous
upgrade. Preserve unrelated work.

Review official .NET and ASP.NET release notes, breaking changes, and known
issues for the releases being adopted. Check API/BCL changes, hosting,
platform support, and performance. Report relevant risks and inaccessible
documentation; passing local tests does not close those gaps.

## Stage 1: NuGet framework recognition

| File | Change |
| --- | --- |
| `Public\Src\FrontEnd\Nuget\NugetFrameworkMonikers.cs` | Add/register `Net{X}0` as `net{X}.0` / `.NETCoreApp{X}.0` and append it to the .NET Core framework history. |
| `Public\Src\FrontEnd\Nuget\NugetSpecGenerator.cs` | Increment `SpecGenerationFormatVersion`. |
| `Public\Src\FrontEnd\UnitTests\Nuget\NuSpecGeneratorTests.cs` | Match the format version and update expected framework unions, cases, and conditions. Update expected spec hashes from the resulting test output after reviewing the differences. |

From the repository root on Windows:

```powershell
cmd /c "call .\bxl.cmd Test.BuildXL.FrontEnd.Nuget.dsc /server-"
```

Confirm the intended tests ran or have matching cached results. Present the
diff and results; publication requires user approval.

Before Stage 2, verify that the LKG selected by
`Shared\Scripts\BuildXLLkgVersion.cmd` (and the public pin where applicable)
actually contains Stage 1's moniker and generator-format change. Trace the
artifact's source revision, accounting for cherry-picks. A later pin date or
an elapsed week is not proof. If inclusion cannot be established, stop and
report the missing prerequisite rather than switching build modes.

## Stage 2: Add the target without upgrading older targets

### 1. Set the dependency policy first

Add versioned .NET and ASP.NET reference/runtime packages alongside existing
ones. **Preserve `aspVersion`, shared `Microsoft.Extensions.*` / `System.*`
pins, and all `ForVBCS` aliases by default.** Do not bump them merely to match
the new runtime's major version.

Use the existing framework-package and compiler/deployment precedence
mechanisms before introducing per-framework aliases, recursive dependency
rewriting, or package skips. Only change a shared dependency when a concrete
requirement has been demonstrated, and validate its older-target impact.

Keep these two decisions separate:

- **Compilation:** `Public\Sdk\Public\Managed\helpers.dsc` selects targeting-pack
  references over overlapping package references. Deployment preference cannot
  fix `CS0433` or a compiler/API mismatch.
- **Runtime deployment:** `Shared.Factory.createFrameworkPackage` and managed
  deployment preference select the framework's own runtime files. Check the
  transitive runtime closure, not just direct package pins. A successful build
  does not prove a service can start.

Package-level preference is not an individual binary flag. Check callers that
deploy extracted `.runtime` binaries if changing where preference is stored.

Audit existing `runtimeContentToSkip` and package `filesToExclude` lists.
An exclusion that was appropriate for an older target can remove a required
DLL from the new deployment. In particular, do not assume ASP.NET's embedded
`Microsoft.Extensions.*` implementations can be removed just because shared
NuGet copies are present.

If precedence logic itself needs changes, test ordinary packages, duplicate
ordering, exclusions, rediscovery, and unrelated dependencies. Preserve
original binary identity for visited/excluded tracking; cloning a binary to
set preference must not resurrect an excluded file. Follow the tests in
`Public\Sdk\UnitTests\Deployment\Test.FrameworkPrecedence.dsc`.

### 2. Add and verify packages and archives

| Location | Required changes |
| --- | --- |
| `config.nuget.dotnetcore.dsc` | Add `core{X}0Version`, `Microsoft.NETCore.App.Ref`, and versioned host/runtime packages for each supported RID. Follow existing analyzer exclusions and `osSkip` patterns. |
| `config.nuget.aspNetCore.dsc` | Add `asp{X}RefVersion`, `asp{X}RuntimeVersion`, and reference/runtime entries. Do not change the shared `aspVersion` by default. |
| `config.dsc` | Add standalone runtime Download entries and the external-runtime SourceResolver module. Keep version comments, URLs, archive types, and hashes consistent. |

Cover **win-x64, osx-x64, osx-arm64, and linux-x64** wherever supported.
Do not copy an old three-platform list. Preserve intentional component-level
platform restrictions.

Verify every requested package version is published and available through the
configured internal feed; public release metadata alone does not establish
internal-feed availability. If a public metadata endpoint fails, an official
alternative such as NuGet v2 can verify publication without weakening TLS.
Report internal-feed failures separately. Do not silently downgrade, change
feeds, or start a dependency cascade to make restore pass.

Native HostResolver/HostPolicy packages and `Microsoft.NETCore.Platforms` may
not track the new runtime version. Check actual publication and reuse existing
published pins/aliases where appropriate; do not assume `{X-1}` exists or
create new aliases solely to match the new framework's name.

Use official release metadata for archive URLs. SHA512 values in that metadata
are **not** BuildXL VSO0 hashes. Normally, use the Download resolver to obtain
the actual VSO0 values:

```powershell
cmd /c "call .\bxl.cmd /f:output='*\frontend\Download\DotNet-Runtime*' /server-"
```

For a new entry, a temporary VSO0 placeholder must contain 66 hexadecimal
digits. For an update, the previous hash can remain temporarily. Match each
reported actual hash to its exact URL, update it, and rerun successfully.
Inspect the printed `BuildXL.err`: only content-hash mismatches are expected,
not unrelated restore or engine failures.

If restore blocks this workflow and the user wants code changes with validation
deferred, download the official archives separately, verify their published
SHA512 values, and compute VSO0 with an existing BuildXL hashing tool:

```powershell
& "<existing-build>\ContentStoreApp.exe" HashFile "-path:<archive>" "-hashType:Vso0"
```

Keep the URL/hash evidence, remove temporary archives, and report that this
prepares hashes only; it does not validate restore, extraction, or tests.

### 3. Wire the framework through the current tree

Copy the nearest supported framework as a starting point, then audit the
following locations. Do not mechanically replace every old version string.

| Area | Locations and checks |
| --- | --- |
| Framework definition | Create `Public\Sdk\Public\Managed\Frameworks\net{X}\module.config.dsc` and `net{X}.0.dsc`. Update imports, standard references, runtime/configuration identity, crossgen providers, and `conditionalCompileDefines`. Cover every supported RID and retain the appropriate native host aliases. |
| Standalone runtime modules | Create `Public\Sdk\SelfHost\Libraries\Dotnet-Runtime-{X}-External\` from the existing pattern, including platform module declarations and wrappers. |
| Framework resolution | Extend `Public\Sdk\Public\Managed\Shared\frameworks.dsc`, `Frameworks\frameworks.dsc`, and `Frameworks\helpers.dsc`: TFM unions, predicates, resolver switches, runtime templates, and cached tool selection. |
| Qualifiers | Audit `Public\Sdk\SelfHost\BuildXL\Qualifiers.dsc` and named qualifiers in `config.dsc`. Include applicable `WithoutMacOSArm64` / other restricted variants without broadening their platform support. Add the new framework-specific interface and Debug/Release qualifiers. |
| Managed SDK and selfhost | Audit `Public\Sdk\Public\Managed\managedSdk.dsc`, `Public\Sdk\SelfHost\BuildXL\BuildXLSdk.dsc`, and `BuildXLSdk.Packages.dsc` for framework predicates, compile defines, test restrictions, and PolySharp conditions. |
| ASP.NET | Extend `Public\Sdk\SelfHost\BuildXL\web.dsc` reference/runtime selection for every supported RID. Preserve the framework package's identity and deployment preference. |
| Test runners | Check xUnit V3, any still-used legacy xUnit mapping, and QTest framework selection. Existing generic predicates may already cover the new target. |
| Compiler and tools | Check `Public\Sdk\Public\Managed\Tools\Csc\csc.dsc` define-based runtime detection (highest version first), plus explicit framework choices in tools such as SymbolDaemon. |
| Package deployments | Extend `Public\Src\Deployment\NugetPackages.dsc` and `cache.NugetPackages.dsc`, preserving nearby grouping and platform restrictions. Check produced package contents, not only qualifier declarations. |
| API baselines | Add the new framework under `Public\Src\Cache\ContentStore\Hashing\PublicAPI\`, following the previous target's shipped/unshipped baseline pattern. |
| Build wrappers | Audit runtime/qualifier/IDE framework lists in `Shared\Scripts\bxl.ps1` and other platform wrappers where explicit version lists exist. |

Do not change the default qualifier as part of enabling an opt-in target.
MSBuild/VBCS may intentionally use a pinned framework plus net472: discover
that qualifier from the current definitions, preserve it, and update any
new-framework graph-builder fallback to the actual pinned version.

When release notes or compilation reveal BCL overlap, inspect the selected
reference assemblies before adding conditional package skips. Add conditional
C# code only for an actual API difference and preserve older-target behavior.
For new obsolete-API warnings, explain the migration versus compatibility
suppression tradeoff to the user; do not blindly suppress.

### 4. Keep redirects and governance consistent

If a shared package really changes, inspect the deployed net472 assembly's
identity and update only its matching binding redirects. Package version and
assembly version are not interchangeable; do not guess the patch encoding.

Check these hand-maintained locations:

- `Public\Src\FrontEnd\UnitTests\MsBuild\msbuild.exe.config`
- `Public\Src\Tools\Tool.MsBuildGraphBuilder\Tool.MsBuildGraphBuilder.dsc`
- `Public\Src\Tools\UnitTests\MsBuildGraphBuilder\Test.Tool.MsBuildGraphBuilder.dsc`
- `Public\Sdk\SelfHost\BuildXL\BuildXLSdk.dsc`
- `Public\Sdk\SelfHost\BuildXL\Testing\XUnitV3\xunitv3framework.dsc`

Adding or servicing a framework runtime alone does not imply changing these
redirects. Never change MSBuild's own assembly identity to a runtime version.

Normally, let `bxl.cmd` generate `cg\nuget\cgmanifest.json` via
`/generateCgManifestForNugets`. A build failing before NuGet resolution completes
may leave the old manifest untouched. After merging, resolve the package pins
first, regenerate, and review the manifest against that resolved set.

If the user explicitly requests a code-only manifest update while restore is
blocked, use a guarded, deterministic synchronization of the established
package IDs to their declared versions. Verify JSON validity, expected entry
count, and no unrelated registration or formatting changes. Do not describe
this as build-generated or claim it proves successful restore.

### 5. Coordinate companion changes and CI coverage

`Shared\Scripts\BuildDistributedTest.cmd` pins `TEST_COMMITID` for
`Domino.DistributedBuildTest`. After Stage 1 is in LKG, update that repo's SDK
preparation and framework/runtime wiring. Once its PR merges, pin the merged
commit here before declaring distributed validation ready.

Add the new Debug/Release qualifiers to the appropriate PR/rolling validation
jobs (including `.azdo\pr\primary-pr.yml` where applicable). This may be a
separate commit, but an opt-in framework is not CI-covered merely because its
qualifier exists. Preserve the pipeline's bootstrap/default-framework role.
Changing the default or removing old framework support remains separate work.

## Validate in increasing scope

Run from the repository root. On Windows, use `cmd /c "call .\bxl.cmd ..."`
and `/server-`. Allow the normal worktree build queue to run: never change
subst mappings, delete `.SubstLock`, or bypass `RunInSubst`. On other platforms,
use the repository's normal build wrapper and dependency mode.

1. **Downloads, restore, and minimal builds:** verify the archive hashes, then
   build the default and new qualifiers. Preserve the original failure if
   either is blocked; do not change modes or disable checks to hide it.

   ```powershell
   cmd /c "call .\bxl.cmd -minimal /server-"
   cmd /c "call .\bxl.cmd -minimal /q:DebugNet{X} /server-"
   ```

2. **Focused tests before full builds:** use the matrix below. Test the new
   target and older targets affected by shared SDK/deployment changes. Use
   actual supported qualifiers for intentionally pinned MSBuild/VBCS tests.

   | Coverage | What it must demonstrate |
   | --- | --- |
   | SDK deployment / `Test.FrameworkPrecedence.dsc` | Framework/package overlap resolves correctly; ordinary conflicts and exclusions still work. New SDK tests producing pips need their expected `.lkg` snapshots. |
   | gRPC startup / IPC | A real service can load its deployed dependency closure and start. |
   | SBOM generation | The generation operation executes with the deployed assemblies; compiling its project is insufficient. |
   | `Test.Tool.VBCSCompilerLogger.dsc` | Compiler-host and net472 binding behavior remains valid. |
   | `Test.BuildXL.FrontEnd.MsBuild.dsc` and `Test.Tool.MsBuildGraphBuilder.dsc` | Subprocess deployments, redirects, and pinned-framework fallbacks work. |
   | net472 cache/service canaries | Shared-package changes have not broken assembly binding or child-process startup. |

   Verify fully qualified test selectors and inspect XML/TRX or equivalent
   results for counts. `-TestClass` / `-TestMethod` can succeed with no matches.
   Distinguish executed passes, matching cached passes, skipped tests, and
   unsupported qualifiers.

3. **Broader validation:** build/test the new Debug and Release qualifiers and
   affected older frameworks. Validate Linux and macOS, including ARM64 where
   supported, through local runs or CI; Windows success is not cross-platform
   confirmation. Keep performance-sensitive checks in Release. The full
   `RunCheckInTests.cmd` matrix is expensive and normally left to CI.

For failures, inspect the printed log directory and `BuildXL.err`, then the
failing process's own output and deployed files:

| Symptom | First check |
| --- | --- |
| `CS0433` / duplicate types | Actual compiler references and targeting-pack precedence. |
| `FileLoadException`, `MissingMethodException`, service startup timeout | Loaded/deployed assembly identities, transitive versions, redirects, and child-process diagnostics. A timeout alone does not establish a binding failure. |
| Expected-exception test fails differently | Whether the MSBuild/service subprocess crashed before reaching the intended scenario. |
| Missing DLL in gRPC/SBOM execution | Runtime deployment contents and stale skip/exclusion lists. |
| Windows-only success | OS-specific errors/assertions, runtime assets, and sandbox behavior on the failing platform. Do not disable the platform's test to conceal the difference. |

Separate new regressions from baseline failures with evidence. Do not silently
work around an unrelated engine failure. If validation is explicitly deferred
to CI, record the exact revision, commands attempted, logs, blockers, and
unvalidated platforms instead of reporting the upgrade as fully tested.

## Preview-to-GA checklist

Keep this a version/artifact update unless GA exposes a specific compatibility
issue:

1. Verify the final .NET and ASP.NET package versions, official release notes,
   archive URLs, and configured-feed availability. An SDK version is separate;
   update an SDK pin only if the repo or companion project actually has one.
2. Update the new target's runtime/ASP.NET constants and `runtimeConfigVersion`,
   all supported standalone archive URLs/comments/VSO0 hashes, and the matching
   governance records.
3. Leave older framework versions, shared packages, `ForVBCS` pins, native host
   aliases, and binding redirects unchanged unless there is a demonstrated
   reason to change them.
4. Search tracked source/configuration and the manifest for the old prerelease
   string. Review the exact diff, not just the absence of that string.
5. Revalidate the changed target and relevant canaries, or explicitly record
   the user's decision to defer validation. Preview test results are not GA
   test results.

The patch-servicing checklist is in
`..\instructions\update-dotnet-runtime.instructions.md`. Do not run
`Shared\Scripts\updateDotNet.ps1` blindly for a single-target update: its
normal scope can include other supported runtime versions.

## Finish and publish

Review the diff for unexpected shared-package upgrades, dropped dependencies,
stale versions, unrelated generated changes, and whitespace errors. When
merging, preserve the intended package baseline rather than automatically
choosing the higher major; redirects must match the final deployed assemblies.

Summarize what changed, actual validation results, remaining risks, and the
state of companion/CI work. With user approval, stage only intended files,
commit with the required trailers, push without rewriting history, and verify
the remote HEAD. Do not include local PR drafts or review artifacts.
