# Opaque Sealed Directories
BuildXL requires all process inputs and outputs to be known statically. 
In some scenarios, knowing a process output is not possible before runtime.
To support such scenarios, we introduce the concept of opaque sealed directories (OD).

A process can declare one of its outputs as `directory` which may contain an arbitrary set of files and directories. An OD is similar to a zip file -- you know there is some content in an archive, but dont know exactly what until you open (consume) it.

## Opaque Sealed Directory Validation Rules
* Only one process can write to a single OD. That is, any OD is exlusively owned by a single pip.
* One process can produce multiple opaque sealed directories.
* Only whole OD can be consumed as input, no single file can be accessed directly.
* No ODs are allowed inside other ODs.

## Internals
Opaque Sealed Directory is represented as a special kind of `SealDirectory` pip in a pip graph. The difference is that when a pip for opaque sealed directory is created,
it has no content associated with it; the content is populated only during runtime, when we actually observe the outputs of the process pip.

Contents of opaque sealed directories are cached. At runtime only one pip, that declared OD as its output, can write output to the opaque sealed directory. 
Note that internally we add opaque sealed directory path to the untracked scope, so any kinds of activity (rewrites, etc.) are allowed within it.

## Known Use Cases
* Non-deterministic names: Example: An output that is dynamically named based on the build number that changes every day. Such outputs will be put into an opaque sealed directory, so there is no need to declare a (statically unknown) output name. The output of this directory will be consumed by a downstream pip.

* Office
  * Metabuild needs to produce a build spec per source file, but with an unknown number of source files.
  * Some projects unzip compressed files.
  * Integration with OACR that produces unpredictable outputs.

* Javac: javac creates a .class file for each class in your project. Typically, one .class file is created for each .java file,  but if a .java file contains anonymous or inner classes, then more .class files are created and it's impossible to know statically how many without doing some non-trivial static analysis of the .java files.

## Language support & API
There are several ways to deal with Opaque Sealed Directories in DScript.
We assume that most of the usage would come via leveraging some sort of transformers.

How to produce an opaque sealed directory:

First, you need to get a directory. Note that at this stage the directory is not opaque yet, and doesn't even exist. For example,

```ts
// This will return an object representing a directory object that has `opaque` in its path in the BuildXL output folder.
const a_directory: Directory = Context.getNewOutputDirectory("opaque");
// Note that this directory is not created just yet.
```

or if you want a particular location you can simple write:

```ts
const a_directory: Directory = d`path/to/a/directory`;
```
Make sure that getting a directory like this doesn't create a double write. 

The next step is to specify a process pip via `Transformer.execute`. At the moment the following options are available to specify (output) opaque sealed directories:

* As a part of command-line argument:
```ts
Transfomer.execute({
    tool: <your tool name>,
    workingDirectory: d`.`,
    arguments: [
        Cmd.option(“—outputDirectory “, Artifact.output(a_directory)), // Artifact.output marks a_directory as an (opaque) output directory.
        // OR just
        Cmd.argument(Artifact.output(a_directory)),
    ]})
```

* As implicit output:
```ts
Transfomer.execute({
    tool: <your tool name>,
    workingDirectory: d`.`,
    arguments: [ /*other args*/ ],
    implicitOutputs: [a_directory] // No need to mark as output.
});
```

To consume an opaque sealed directory:

* Get ```Transformer.ExecuteResult``` from the pip execution that produced an opaque sealed directory
* Obtain ```StaticDirectory``` from it (you have to specify the name of the opaque sealed directory)  ```outDir: StaticDirectory = executeResult.getOutputDirectory(a_directory) ```
* Now you can get an artifact that can be used as input argument for the consuming transformer: ```Artifact.input(outDir)```

## Code Samples
Complete example (see OpaqueDirectory from our Samples):
```ts
import * as BatchScript from "BatchScript";
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const opaqueDir = Context.getNewOutputDirectory("opaque");
const producer = BatchScript.BatchScript.runScript({
    tool: {
        exe: f`./produceFilesInOD.cmd` // Creates random files under its output directory.
    },
    arguments: [
        Cmd.argument(Artifact.output(opaqueDir)), // we pass the directory which is used as an opaque sealed directory.
    ],
    workingDirectory: d`.`
});

const outDir : StaticDirectory = producer.getOutputDirectory(opaqueDir);
const staticDir = Context.getNewOutputDirectory("static");

export const consumer = BatchScript.BatchScript.run({
    tool: {
        exe: f`./enumFilesFromOD.cmd`, // Enumerates all files under the given path.
    },
    arguments: [
        Cmd.argument(Artifact.input(outDir)), // Opaque output to consume.
        Cmd.argument(Artifact.output(p`${staticDir}/output.txt`)), // output dir
    ],
        workingDirectory: d`.`
});
```
