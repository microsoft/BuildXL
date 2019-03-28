# Shared Opaque Directories
These are a generalization of [Exclusive opaque directories](./Opaque-Sealed-Directories.md). Their key differentiator is that multiple pips can contribute to the same directory. The content of a shared directory, similarly to exclusive opaque directories, is not known at spec evaluation time and the set of files is treated as a black box until consumed.

## Shared Opaque Directory Validation Rules

* Only a shared opaque directory, as a unit, can be consumed, no single file can be accessed directly.
* Shared opaque directories are not allowed under graph artifacts that impose immutability constraints, e.g. 'all directories' source sealed directories or fully sealed directories. Similarly, shared opaque directories cannot overlap with exclusive sealed directories.
* In order to read from a shared opaque directory, an explicit dependency on it has to be declared. Observe that even though multiple pips can contribute to a shared opaque directory, in order to be able to read a specific file from it, a dependency on the specific shared opaque of the pip that produced that file needs to be declared.
* Similarly to statically declared outputs, having two pips produce the same file under a shared opaque is not allowed and it is flagged as a violation. Rewriting an input file is also blocked. The only way to rewrite a file is to statically declare it as a rewritten file. 

## Limitations
* Specifying a drop pip on a shared opaque directory is not supported yet.
* Specifying a filter on a shared opaque directory implies including all the pips that contribute to that directory. See [Filtering](/BuildXL/User-Guide/How-to-run-BuildXL/Filtering) for more details.
* The content of all shared opaque directories that are part of a build are scrubbed before the build starts. Removing old outputs before a pip runs is also the behavior for explicitly declared outputs, but the difference here is that *all* the content of declared shared opaque directories is scrubbed before the build start, independently of the pips that are actually going to run. This behavior will be improved in the future, but the intrinsic dynamic behavior of shared directories adds significant complexity to the problem.

## What's the main difference between using shared opaques and statically declaring outputs?
The key advantage of shared directories is that specs don't need to be as precise. This means less pressure for the spec author to know the exact behavior of tools, and more concise specs for the same system.

On the other hand, there is a significant trade-off on build determinism: statically declaring outputs allows BuildXL to catch many non-deterministic behaviors statically, before anything runs: double writes, non-deterministic probes, etc. By using shared opaque directories, many of these static checks become dynamic, and purely based on observations. This means, for example, that if that a given pip introduces a non-deterministic behavior, it will only be flagged if that pip actually runs. A filter can rule that pip out, and the build will succeed. This trade-off needs to be carefully considered. The recommendation is to statically declare as much as possible.

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