# Building JavaScript-based repos with BuildXL
BuildXL provides native support for JavaScript-based repositories. This is achieved with a particular [frontend](../Frontends.md) type that knows how to translate JavaScript projects into units of work BuildXL can then schedule and execute. 

High level, and ignoring some particularities that could make a repository need to provide extra information, a single BuildXL configuration file that tells BuildXL that is dealing with a JavaScript-based repo is the only artifact that should be needed to accelerate a JavaScript build with safe caching and distribution. 

Currently BuildXL knows how to talk to particular JS coordinators/package managers: [Rush](https://rushjs.io/), [Yarn](https://yarnpkg.com/) (on workspaces) and [Lage](https://github.com/microsoft/lage).

Almost all the BuildXL configuration options are the same regardless of the coordinator. In the examples that follow, we will use Rush to exemplify different scenarios, even though any other supported coordinator can be used instead. Check the [coordinator-specific options](js-coordinator-options.md) to understand the differences.

Interested in running package install under BuildXL as well and benefit from caching? Check [here](js-package-install.md).

## The basics
Moving an existing JavaScript repo to BuildXL may need some adjustments to conform to extra build guardrails BuildXL imposes to guarantee safe caching and distribution. Many repos though may need little or no changes, but that really depends on how well 'disciplined' a repo is.

Let's start with the basic configuration BuildXL needs. A `config.dsc` BuildXL configuration file needs to become part of the repo and a JavaScript resolver needs to be added to it. Most of the onboarding work is about adjusting the configuration of this resolver to the particularities of a repository. The only mandatory information what kind of resolver this is (e.g. 'Rush') and where is the root of the repo:

```typescript
config({
    resolvers: [
    {
        kind: "Rush",
        moduleName: "my-repo",
        root: d`.`,
    }
  ]
});
```

Here `root` specifies the directory where `rush.json` is supposed to be found. In this case, we are saying this file should be found in the same directory where this `config.dsc` is.

Each JavaScript resolver makes all its projects belong to a single BuildXL module. `moduleName` defines the name of this module, which can later be referenced from other resolvers to enable ['hybrid' build scenarios](js-hybrid-builds.md), where different coordinators or build add-ons can be part of the same repo.

If the repository is well-behaved, this is everything you need to do. The full set of options to configure A JavaScript resolver can be found [here](../../../Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc).

## Most common onboarding issues [](#most_common_issues)
The basic configuration shown above may not be enough for some cases. These are the most common problems a repository can face:
### Missing project-to-project dependencies
BuildXL is very strict when it comes to declaring dependencies on other artifacts produced during the build, since that guarantees a sound scheduling. So missing dependencies is something relatively common to encounter when moving to BuildXL. 


Missing dependencies will usually manifest as a dependency violation error, indicating that a pip produced a file consumed by another pip in an undeclared manner. The fix is usually about adding the missing dependency in the corresponding ```package.json```.

The violation in this case will manifest as a missing dependency error. For example, the error may look like this one:
```
[8:02.248] error DX0500: [PipDEFF01E0D23D9CF9, cmd.exe (sp-client - @ms/sp-client-release), sp-client, _ms_sp_client_release_build, {}] - Disallowed file accesses were detected (R = read, W = write):
Disallowed file accesses performed by: C:\windows\system32\cmd.exe
 MD [W] d:\dbs\el\osct\prototypes\sp-mixed-reality-workbench\temp\combined-strings.json

MD  = Missing dependency

Violations related to pip(s):
PipD5A149C7492C4333, cmd.exe (sp-client - @ms/sp-mixed-reality-workbench), sp-client, _ms_sp_mixed_reality_workbench_build, {}
```

This is telling us the violation is related to a write happening on the file `combined-strings.json`. If we look into the main BuildXL log, we'll find extra details for it:

```
[8:02.218] verbose DX5025: Detected dependency violation: [PipDEFF01E0D23D9CF9, cmd.exe (sp-client - @ms/sp-client-release), sp-client, _ms_sp_client_release_build, {}] Undeclared access on an output file: This pip accesses path 'd:\dbs\el\osct\prototypes\sp-mixed-reality-workbench\temp\combined-strings.json', but 'PipD5A149C7492C4333, cmd.exe (sp-client - @ms/sp-mixed-reality-workbench), sp-client, _ms_sp_mixed_reality_workbench_build, {}' writes into it. Even though the undeclared access is allowed, it should only happen on a source file.
```

The problem here is about `@ms/sp-client-release` not being allowed to read `combined-strings.json` produced by `@ms/sp-mixed-reality-workbench` without explicitly declaring it as a dependency. When there is not a declared dependency, BuildXL interprets reads as happening on source files, but sources files are not supposed to be written during the build.

You can try running the [JavaScript dependency fixer](../Advanced-Features/Javascript-dependency-fixer.md), an analyzer that will attempt to fix missing dependencies by observing the dependency violation errors coming from a previous build.

### Rewrites
We call a rewrite the case of two different pips writing to the same file (regardless of whether those writes race or not). Rewrites are problematic since it is not clear whether a dependency on the file being rewritten is supposed to see the original or the rewritten version of it. BuildXL will allow rewrites where the same content is written, since in that case read races are not problematic, but it will block different-content ones.

In order to fix this problem, the recommendation is to refactor the code to avoid the rewrite, and instead produce a new file with the rewritten content, where now dependencies can decide to consume the original or the new file.

A rewrite will often manifest as a violation that looks like this:

```
error DX0500: [Pip92F92BFFF7426431, cmd.exe (odsp-common - @ms/odsp-build), odsp-common, _ms_odsp_build_test, {}] - Disallowed file accesses were detected (R = read, W = write):
Disallowed file accesses performed by: C:\windows\system32\cmd.exe
 DW d:\dbs\el\osco\odsp-build\lib\CollectLibraryFilesTask.d.ts

 DW = Double Write

Violations related to pip(s):
Pip942CE2B1A4D58F7A, cmd.exe (odsp-common - @ms/odsp-build), odsp-common, _ms_odsp_build_build, {}
```
This means two pips (build and test pips for `@ms/odsp-build`) are both writing the same file.

There is however an escape hatch when rewrites occur. A file (or a directory scope) can be flagged as `untracked`. This means BuildXL will ignore the rewrite on that particular file/scope. It is worth noting this is an **unsafe** option. On one hand, it opens the door to non-deterministic builds, since consumers may get different versions of the file on each run. On the other hand, untracking a file will cause BuildXL to ignore changes on that file when deciding whether a pip needs to be re-run or it is safe to retrieve from the cache. So underbuilds are possible. Let's see an example:

```typescript
config({
    resolvers: [
    {
        kind: "Rush",
        ...
        untrackedFiles: [
            f`src\parts\aFileThatIsRewritten.js`,
        ]
    }]
});
```
Similarly BuildXL will also monitor source files that are written during the build. The same sort of race reads can occur in this case. BuildXL will flag as an error any write to a source file that produces different content whenever there is a reader that is not well-ordered with respect to the write, meaning that the content it might see is not deterministic. In this case, this type of error is produced:

```
[7:14.969] verbose DX5047: Detected dependency violation: [PipFE702B684F6491F3, cmd.exe (sp-client - @ms/babylonjs-bundle), sp-client, _ms_babylonjs_bundle_build, {}] This pip writes to path 'd:\dbs\el\osct\libraries\babylonjs-bundle\lib\babylonjs.manifest.json', but the file was not created by this pip. This means the pip is attempting to rewrite a file without an explicit rewrite declaration. This may introduce non-deterministic behaviors in the build.
```

### Stale outputs are present from a previous non-BuildXL build
This case can be understood as a special case of a source rewrite, with the difference that the 'source' is actually a file that was produced before BuildXL ran. BuildXL does not have any connection to a source control system, so a true source file and an output previosuly produced by a different build system actually looks the same. Let's say that `rush build` was run on a repo and that produced some outputs. If BuildXL happens to run afterwards, it will detect that projects are trying to write into existing files and will flag this as an error if any read race occurs against a different-content write.

Consider this will not be the case when previous outputs are produced by a previous BuildXL build, since that's something BuildXL can recognize and deal with.

The recommended solution is to clean stale outputs from previous non-BuildXL builds (`git -xdf` will do the trick on a git based repo). Another option which is pretty convenient when onboarding is to have two cloned instances of the same repo, one for building the repo as usual and the other one for building with BuildXL.

### Outputs are written outside the corresponding project root

BuildXL tries to keep where outputs are rewritten more or less under control. Each project is automatically allowed to write files under its own project root, and any file written outside of it will be flagged as an undeclared output. In order to declare additional directories where outputs may happen, there are two ways. An entry in the main config file can be added:

```typescript
config({
    resolvers: [
    {
        kind: "Rush",
        ...
        additionalOutputDirectories: [d`C:\deploy`]
    }]
});
```
which specifies that any project can write under `C:\deploy`. Alternatively, a per-project configuration file can be added by dropping a `bxlconfig.json` file at the root of the corresponding project:

```typescript 
// src/my-project/bxlconfig.json
{
  "outputDirectories": [
      "<workspaceDir>/deploy", 
      "C:/deploy"]
}
```
Here `my-project` is declaring it is going to write under two additional directories. Absolute paths can be provided as well as relative paths, that are interpreted relative to the project root. A special token `<workspaceDir>` can be used to denote the root of the repo (and avoid a proliferation of `\..\..`).

A project writing outside of its project root without declaring any extra output directory will usually manifest in a violation that looks like this one:

```
[8:30.223] verbose DX5008: Detected dependency violation: [PipDEFF01E0D23D9CF9, cmd.exe (sp-client - @ms/sp-client-release), sp-client, _ms_sp_client_release_build, {}] Missing output declaration: This pip wrote an unexpected output to path 'd:\dbs\el\osct\deploy\api-json\content-handler.api.json'. Declare this file as an output of the pip.
```
## Advanced scenarios
There are some additional configuration options that may be required in more advanced scenarios.

* [Specify what to build](js-commands.md)
* [Getting cache hits in a distributed scenario](js-cachehits.md)
* [Performance considerations](js-perf.md)
* [Beyond JavaScript - hybrid builds](js-hybrid-builds.md)
* [Other misc options](js-misc-options.md)
* [Package installation under BuildXL](js-package-install.md)
