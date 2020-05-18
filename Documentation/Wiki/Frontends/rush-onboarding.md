# Building Rush-based repos with BuildXL
BuildXL provides native support for repositories based on [Rush](https://rushjs.io/). This is achieved with a particular [frontend](../Frontends.md) type that knows how to translate Rush projects into pips BuildXL can then schedule and execute.

BuildXL integration with Rush is a way to accelerate a 'rush build' command with safe caching and distribution. Other Rush commands that usually occur before build (e.g. 'rush update' or 'rush install') are for now outside of BuildXL scope.

## The basics
Moving an existing Rush repo to BuildXL may need some adjustments to conform to extra build guardrails BuildXL imposes to guarantee safe caching and distribution. Many repos though may need little or no changes, but that really depends on how well 'disciplined' a repo is.

Let's start with the basic configuration BuildXL needs. A Rush resolver needs to be added in the main BuildXL configuration file. Most of the onboarding work is about adjusting the configuration of this resolver to the particularities of a repository. The only mandatory information is where the main Rush configuration file (rush.json) is to be found:

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

Each Rush resolver makes all Rush projects configured in the corresponding `rush.json` to belong to a single module. `moduleName` defines the name of this module, which can later be reference from other resolvers (for example see [Pushing Rush artifacts to Drop](rush-drop.md)).

If the repository is well-behaved, this is everything you need to do. The full set of options to configure the Rush resolver can be found [here](../../../Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc) under the ```RushResolver``` interface.

## Most common onboarding steps
The basic configuration shown above may not be enough for some cases. These are the most common problems a repository can face:
### Missing project-to-project dependencies
BuildXL is very strict when it comes to declare dependencies on other artifacts produced during the build, since that guarantees a sound scheduling. So missing dependencies is something relatively common to encounter when moving to BuildXL. 


Missing dependencies will usually manifest as a dependency violation error, indicating what a pip produced a file consumed by another pip in an undeclared manner. The fix is usually about adding the missing dependency in the corresponding ```package.json```.

You can try running the [JavaScript dependency fixer](../Advanced-Features/Javascript-dependency-fixer.md), an analyzer that will attempt to fix missing dependencies by observing the dependency violation errors coming from a previous build.

### Rewrites
We call a rewrite the case of a two different pips writing to the same file (regardless of whether those writes race or not). Rewrites are problematic since it is not clear whether a dependency on the file being rewritten is supposed to see the original or the rewritten version of it. With coordinators like Rush, it is not possible to statically specify rewrites, so BuildXL blocks rewrites coming from Rush. The recommendation is to refactor the code to avoid the rewrite, and instead produce a new file with the rewritten content, where now dependencies can decide to consume the original or the new file.

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



## More advanced scenarios
There are some additional configuration options in the Rush frontend that may be required in more advanced scenarios.

* [Specify what to build](rush-commands.md).
* [Getting cache hits in a distributed scenario](rush-cachehits.md).

