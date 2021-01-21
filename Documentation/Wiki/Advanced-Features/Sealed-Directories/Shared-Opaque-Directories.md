# Shared Opaque Directories
These are a generalization of [Exclusive opaque directories](./Opaque-Sealed-Directories.md). Their key differentiator is that multiple pips can contribute to the same directory. The content of a shared directory, similarly to exclusive opaque directories, is not known at spec evaluation time and the set of files is treated as a black box until consumed.

## Shared Opaque Directory Validation Rules

* Only a shared opaque directory, as a unit, can be consumed, no single file can be accessed directly.
* Shared opaque directories are not allowed under graph artifacts that impose immutability constraints, e.g. 'all directories' source sealed directories or fully sealed directories. Similarly, shared opaque directories cannot overlap with exclusive sealed directories.
* In order to read from a shared opaque directory, an explicit dependency on it has to be declared. Observe that even though multiple pips can contribute to a shared opaque directory, in order to be able to read a specific file from it, a dependency on the specific shared opaque of the pip that produced that file needs to be declared.
* Similarly to statically declared outputs, having two pips produce the same file under a shared opaque is not allowed and it is flagged as a violation. Rewriting an input file is also blocked. The only way to rewrite a file is to statically declare it as a rewritten file. 

## Limitations
* Specifying a filter on a shared opaque directory implies including all the pips that contribute to that directory. See [Filtering](../../How-To-Run-BuildXL/Filtering.md) for more details.
* The content of all shared opaque directories that are part of a build are scrubbed before the build starts. Removing old outputs before a pip runs is also the behavior for explicitly declared outputs, but the difference here is that *all* the content of declared shared opaque directories is scrubbed before the build start, independently of the pips that are actually going to run. This behavior will be improved in the future, but the intrinsic dynamic behavior of shared directories adds significant complexity to the problem.
* [Incremental Scheduling](../Incremental-Scheduling.md) does not support pips that produce shared opaque directories, i.e., such pips are marked perpetually dirty.

## What's the main difference between using shared opaques and statically declaring outputs?
The key advantage of shared directories is that specs don't need to be as precise. This means less pressure for the spec author to know the exact behavior of tools, and more concise specs for the same system.

On the other hand, there is a significant trade-off on build determinism: statically declaring outputs allows BuildXL to catch many non-deterministic behaviors statically, before anything runs: double writes, non-deterministic probes, etc. By using shared opaque directories, many of these static checks become dynamic, and purely based on observations. This means, for example, that if that a given pip introduces a non-deterministic behavior, it will only be flagged if that pip actually runs. A filter can rule that pip out, and the build will succeed. 

Another potential downside of shared opaque directories is that their extensive usage might negatively affect the performance of [cache lookups](../Two-Phase-Cache-Lookup.md) under certain conditions. If a pip specifies only shared opaque directories as its inputs, the resulting weak fingerprint of that pip will be very weak, i.e., the engine will have to go though a considerably larger set of cache entries to check for a match. Additionally, if such a pip is not stable in terms files it accesses every time it is executed, there might be (what is colloquially known) a pathset explosion: a rapid growth of a number of matching pathsets to a point where it starts to severely degrade the performance. 

These trade-offs needs to be carefully considered. The recommendation is to statically declare as much as possible.

## Language support & API
How to produce a shared opaque sealed directory:

First, you need to get a directory. For example, 

```ts
const aDirectory: Directory = Context.getNewOutputDirectory("sharedOpaque");
// Note that this directory is not created just yet.
```

The next step is to specify a process pip via ```Transformer.execute```. At the moment the following options are available to specify shared opaque directories:

* As a part of command-line argument:
```ts
Transfomer.execute({
    tool: <your tool name>,
    workingDirectory: d`.`,
    arguments: [
        Cmd.option(“—outputDirectory “, Artifact.sharedOpaqueOutput(aDirectory)), // Artifact.output marks aDirectory as shared opaque output directory.
        // OR just
        Cmd.argument(Artifact.sharedOpaqueOutput(aDirectory)),
    ]})
```

* As an output with no command-line correlate:
```ts
Transfomer.execute({
    tool: <your tool name>,
    workingDirectory: d`.`,
    arguments: [ /*other args*/ ],
    outputs: [{directory: aDirectory, kind: "shared"}] 
});
```

To consume a shared opaque directory:

* Get `Transformer.ExecuteResult` from the pip execution that produced a shared opaque sealed directory
* Obtain `StaticDirectory` from it; you have to specify the name of the shared opaque sealed directory:  `outDir: StaticDirectory = executeResult.getOutputDirectory(aDirectory)`
* Now you can get an artifact that can be used as input argument for the consuming transformer: `Artifact.input(outDir)`

### Nested Shared Opaque Directories

A pip can produce several shared opaque directories that are nested inside one another. In such a case, the files produced by that pip are attributed to the 'closest' shared opaque directory they are under. 

Let's say a pip declares two shared opaque directories:
```ts
const dir1: Directory = Context.getNewOutputDirectory("sharedOpaque");
const dir2: Directory = Context.getNewOutputDirectory("sharedOpaque/someSubDir/sharedOpaque2");
...
Transfomer.execute({
    ...
    outputs: [{directory: dir1, kind: "shared"}, {directory: dir2, kind: "shared"}] 
});

```

Let's also assume for this example that that pip is going to write two files:
```
./sharedOpaque/fileA
./sharedOpaque/someSubDir/sharedOpaque2/fileB
```

When the pip is executed, `dir1` wil only contain `fileA` and `dir2` will contain `fileB`. Since these directories are normal shared opaque directories, they follow the same rules. For example, if another pip needs to consume both of these files, it must declare dependency on both of the shared opaque directories. 

## Filtered Shared Opaque Directory

There are legitimate scenarios where you might want to restrict which files are accessed by a pip. For statically declared files it is easy - you just need to list the minimal set of files the pip should have access to and BuildXL will take care of the rest. However, as pointed above, in case of shared opaque directories, pips must take dependency on the whole shared opaque directory. To somewhat alleviate this limitation, BuildXL provides an API that can filter content of a shared opaque directory and put the matched files inside of a new shared opaque directory.

```ts
const sharedOpaque: Directory = ...
const executionResult = Transformer.execute(...);
const producedSharedOpaque = executionResult.getOutputDirectory(sharedOpaque);

const filteredSharedOpaque: SharedOpaqueDirectory = Transformer.filterSharedOpaqueDirectory(
    directory: producedSharedOpaque,
    contentFilter: {
        kind: "Exclude",    // can be either Include or Exclude
        regex: ".*\\.log$"  // a regular expression
    }
);
```
For each file inside of a given shared opaque directory, the specified regex filter is applied to the full file path. Based on the kind of a content filter and whether regex was a match or not, the files are added to / omitted from the resulting directory:

|               | <span style="font-weight:normal">Regex matched</span> | <span style="font-weight:normal">Regex did not match</span> |
|---------------|---------|---------|
| Kind: Include | Added   | Skipped |
| Kind: Exclude | Skipped | Added   |

A filtered shared opaque directory behaves as any other shared opaque directory.

## Composite Shared Opaque Directory

Sometimes there is a need to have an artifact that represents a combined set of shared opaque directories produced by multiple pips. To address this, BuildXL provides a way to create a new shared opaque directory that is composed of other shared opaque directories:

```ts
const sharedOpaque: Directory = ...

const executionResultPipA = Transformer.execute(...);
const producedSharedOpaquePipA = executionResultPipA.getOutputDirectory(sharedOpaque);

const executionResultPipB = Transformer.execute(...);
const producedSharedOpaquePipB = executionResultPipB.getOutputDirectory(sharedOpaque);

const compositeSharedOpaque: SharedOpaqueDirectory = Transformer.composeSharedOpaqueDirectories(
    root: sharedOpaque,
    directories: [
        producedSharedOpaquePipA,
        producedSharedOpaquePipB
    ],
    contentFilter: {
        kind: "Exclude",
        regex: ".*\\.log$"
    }
);
```

The root of a composite shared opaque directory can be any directory that is a common ancestor to all the provided directories. In the provided example, all three shared opaque directories share the same path, but it is not a requirement, i.e., you can compose any shared opaque directories (including composite ones) as long as they are under the specified root. The `contentFilter` specifies an optional filter to be applied to the resulting content of the created directory (and it works the same way the filtered shared opaque does).

A composite shared opaque directory behaves as any other shared opaque directory.