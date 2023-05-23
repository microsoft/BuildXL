# Working with DScript

As a general-purpose build engine, BuildXL is not tied to any programming language. It can build C, C++, C#, Java,
Python, etc., so long as you can provide BuildXL with a pip graph (see [Core Concepts](../CoreConcepts.md)).
BuildXL has multiple frontends that can transform specifications for other build engine into pip graphs.
For example, BuildXL can understand MsBuild specification using its MsBuild frontend. BuildXL also has a JavaScript
frontend that understands several JavaScript orchestrator frameworks, like Rush, Yarn, and Lage. For information
about BuildXL's frontends, see [Frontends](../Frontends.md).

In this section we are going to use DScript language that is developed specifically for BuildXL to construct pip graphs.
*DScript* is a language for describing data flow in a subset of TypeScript language. The evaluation/interpretation of
DScript specifications only constructs a pip graph, but does not execute the pips in that graph. Only when the pip graph
construction is finished, the scheduler will start executing the pips according to the dependency order.

Detailed exposition on DScript can be found in [DScript](../DScript/Introduction.md). 

## Build specifications in DScript

A DScript build is identified by a configuration file `config.dsc`. This configuration file tells us what to build and
contains build-wide configuration. The build specifications are divided into modules, each of which is identified by
a file `module.config.dsc` that specifies the name of the module. Each module can have more than one DScript specification
files with a `.dsc` extension.

Consider we are building a small build engine application consisting of a scheduler and a cache. We have the following
directory structure:
```console
.
├── App
│   ├── App.cs
│   ├── App.dsc
│   └── module.config.dsc
├── Cache
│   ├── Cache.cs
│   ├── Cache.dsc
│   └── module.config.dsc
├── config.dsc
└── Scheduler
    ├── module.config.dsc
    ├── Scheduler.cs
    ├── Scheduler.dsc
    └── Utils
        ├── Utils.cs
        └── Utils.dsc
```
We have three modules, `App`, `Scheduler`, and `Cache`. The `App` module consumes the libraries produced by
the `Scheduler` and `Cache` modules, and produces an executable. Each `module.config.dsc` only specifies the name
of the module. For example, `Scheduler/module.config.dsc` has the following content:
```typescript
module({name: "Scheduler"});
```

Let's start with the `Scheduler` module. This module has 2 DScript specs, `Utils.dsc` and `Scheduler.dsc`. The former one
is used to build the scheduler's utilities that will be used to build the scheduler itself in the latter spec. The DScript spec `Utils.dsc` has the following content:
```typescript
function compileLibrary(csFiles: File[]) : File { ... }

namespace Utils
{
    export const dll = compileLibrary(f`Utils.cs`);
}
```
It has a function to compile a source into a library, and the output library will be denoted by the `dll` declaration
inside the `Utils` namespace. Note that the `dll` declaration is exported using the `export` keyword. This makes
the `dll` declaration visible in another spec in the same module.

To build the scheduler, the DScript spec `Scheduler.dsc` contains the following compilation:
```typescript
function compileLibrary(csFiles: File[], libFiles: File[]) : File { ... }

@@public
export const dll = compileLibrary(f`Scheduler.cs`, Utils.dll);
```
Note that the library compilation for the scheduler uses to the utility library by referring to the `dll` declaration
in the `Utils` namespace declared in `Utils.dsc`. This also establishes a dependency relation between the scheduler and
its utilities. Also note that, besides being exported, the `dll` declaration in `Scheduler.dsc` has `@@public` attribute.
This makes the declaration visible from other modules.

Similar to the `Scheduler` module, the `Cache` module also has the following spec:
```typescript
function compileLibrary(csFiles: File[]) : File { ... }

@@public
export const dll = compileLibrary(f`Cache.cs`);
```

Now, the app module uses the scheduler and cache libraries to compile the build engine executable. The `App.dsc` spec contains the following declarations:
```typescript
import * as Scheduler from "Scheduler";
import * as Cache     from "Cache";

function compileExecutable(csFiles: File[], libFiles: File[]) : File { ... }

@@public
export const exe = compileExecutable(f`App.cs`, [Scheduler.dll, Cache.dll]);
```
Consuming the `dll` values from the `Scheduler` and `Cache` modules is done simply by importing the modules using `import`
statements.

## File/Directory literals

DScript has special types for path/file/directory literals, which are different from string. DScript uses a backtick notation prefixed with `p`, `f`, and `d` to denote path, file, and directory, respectively. The file literal 
`` f`Scheduler.cs` `` denotes the source file `Scheduler.cs`. The output file itself is referred to by the declaration
that produces the output file, and not by file literals.

Being strongly typed avoids common path manipulation bugs in specifications when paths are represented as string literals.

## Transformers by examples

There are 3 main kinds of pip that are used for performing builds, i.e., process pip, write-file pip, and copy-file pip.
A process pip represents a process that will be executed/launched during the build, and it consists of information for
executing the process, i.e., executable, arguments, and working directory. A process pip can also specify
input/output files/directories, environment variables, paths/directories that file-access monitoring should
ignore (also referred to as *untracked* directories/paths). 

A write-file pip is used to write string content to a file. A copy-file pip is used to copy a file to some location.
Although both write-file pip and copy-file pip can be represented as a process pip, they were introduced for build
efficiency, i.e., executing write-file and copy-file pips does not launch any process, but only amounts to calling API
for writing a file or for copying a file, respectively.

To construct such pips and insert them into the pip graph, DScript has a built-in SDK called *`Sdk.Transformers`* 
containing these functionalities.

To construct a process pip, DScript uses the `Transformer.execute` method:
```typescript
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const compile = Transformer.execute({
    tool: {
        exe: f`/usr/bin/g++`,
        dependsOnCurrentHostOSDirectories: true,
        runtimeDependencies: [f`/usr/lib64/ld-linux-x86-64.so.2`],
        prepareTempDirectory: true
    },
    arguments: [
        Cmd.argument(Artifact.input(f`file.cpp`)),
        Cmd.argument("-c"),
        Cmd.option("-o ", Artifact.output(p`file.o`)),
        Cmd.options("-D", ["FOO", "BAR"]),
    ],
    workingDirectory: d`.`,
    environmentVariables: [
        { name: "ENV1", value: "1" },
        { name: "ENV2", value: "2" }
    ],
    dependencies: glob(d`headers`, "*.h"),
    unsafe: {
        untrackedScopes: [d`/lib`]
    }
});
const objFile = compile.getOutputFile(p`file.o`);
```
The above declaration creates a process pip that will execute the following command:
```console
/usr/bin/g++ file.cpp -c -o file.o -DFOO -DBAR
```
When specifying the `g++` tool, we also specify a static dependency to `/usr/lib64/ld-linux-x86-64.so.2` because `g++`
will access that file. Also, `g++` often accesses temporary files in `/tmp`. To have reproducible builds, by specifying
`prepareTempDirectory: true`, BuildXL redirects the temporary folder for the pip to a unique location. BuildXL ensures
that the temporary folder is empty before executing the pip. By specifying `dependsOnCurrentHostOSDirectories: true`,
we tell BuildXL to disable runtime file-access monitoring for some system folders like `/var`, `/etc`, `/sys`, etc. that
are irrelevant for caching, i.e., any access to files under those folders will not be observed by BuildXL.

In the `arguments` section, the source file `file.cpp` is specified as an input (dependency) using `Artifact.input`, and
the path `file.o` is specified as an output path using `Artifact.output`. The source file `file.cpp` can `#include` headers 
from the `headers` directory. To avoid file-access violations, the pip declares that all `.h` files in the `headers` 
directory as its dependencies.

The pip will execute with environment variables, `ENV1` and `ENV2`. With `untrackedScopes`, the pip tells BuildXL
to turn off file-access monitoring when accessing all files under `/lib`.

Since `Transformer.execute` can specify multiple output files, to get the object file in the above example, one can
call `` getOutputFile(p`file.o`) ``. The resulting object file is denoted by the `objFile` declaration, and can be
passed to other `Transformer.execute` calls as a dependency. Consider an example where there is another process
that will link the object file:
```typescript
const link = Transformer.execute({
    tool: { ... },
    arguments: [ ... ],
    workingDirectory: ...,
    dependencies: [ objFile ]
});
```
Here the resulting object file is declared as a dependency of the `link` process pip. In this way, DScript establishes
a dependency relation between the `compile` process pip and the `link` process pip.

Interface declarations of `Transformer.execute` can be found in [Transformer.dsc](../../../Public/Sdk/Public/Transformers/Transformer.Execute.dsc).

There are many variants of transformer methods for creating write-file pips. One method that is often used is to write
multiple lines:
```typescript
import {Transformer} from "Sdk.Transformers";
const writtenFile = Transformer.writeAllLines(p`written.txt`, ["line 1", "line 2"]);
```
The above method call will create a write-file pip that writes
```
line 1
line 2
```
to `written.txt`.

Complete methods for creating write-file pips can be found in [Transformer.Write.dsc](../../../Public/Sdk/Public/Transformers/Transformer.Write.dsc).

To create a copy-file pip, DScript uses `Transformer.copyFile` method:
```typescript
import {Transformer} from "Sdk.Transformers";
const copyFile = Transformer.copyFile(f`fileToCopy.txt`, p`copy.txt`);
```
The above method call will create a copy-file pip that copies `fileToCopy.txt` to `copy.txt`.

Details about methods for creating copy-file pips can be found in [Transformer.Copy.dsc](../../../Public/Sdk/Public/Transformers/Transformer.Write.dsc).

> Note that when specifying input files, DScript uses the prefix `f`, but when specifying output files, DScript
  declare them as paths using the prefix `p`.

## Input/Output directories

Besides files, process pips can specify directories as inputs and outputs. For inputs, directories need to be "sealed"
first, i.e., declaring the members of the directories. Unlike input directories, output directories are always sealed.

One can seal input directories by using the following transformer methods:
```typescript
import {Transformer} from "Sdk.Transformers";

const outputFile = Transformer.execute({ ... outputs: [p`Dir/output.txt`], ...}).getOutputFile(p`Dir/output.txt`);
const sealedDir = Transformer.sealDirectory(
    d`Dir`,
    [
        f`Dir/file1.txt`,
        f`Dir/file2.txt`,
        outputFile
    ]);
const sealedSourceAll = Transformer.sealSourceDirectory(d`All`, Transformer.SealSourceDirectoryOption.allDirectories);
const sealedSourceTop = Transformer.sealSourceDirectory(d`Top`, Transformer.SealSourceDirectoryOption.topDirectoryOnly);
const result = Transformer.execute({ ... dependencies: [sealedDir, sealedSourceTop, sealedSourceAll], ...});
```

The first sealed directory `sealedDir` is obtained by sealing the directory `Dir`, and explicitly specifying the members
that the consuming pip are allowed to access as dependencies, e.g., if the consuming pip that declares a dependency on
`sealedDir` accesses file `Dir/file3.txt` during its execution, then BuildXL will fail the pip a file access violation.
In the above example, the specified member in a sealed directory can also be an output of another pip.
Note that the sealed directory can have members other than the specified ones. Note also that the specified members are
an overapproximation to what the pip may access when it executes.

For convenience, without having to list all the members, the `Sdk.Transfomer` module also has
`Transformer.sealSourceDirectory` for sealing a source directory, i.e., a directory where any membership modification
or any write to any file in it is disallowed. One can include all members in the source directory recursively, or only
the top-level ones.

Often a process pip can produce output files where specifying them one-by-one is hard or even impossible, particularly
when the user is not familiar with how the tool specified by the pip works. BuildXL supports two kinds of output directory,
exclusive and shared output directories. Only one process pip can produce an exclusive output directory, but multiple
processes can output to the same shared output directory, as shown in the following example:
```typescript
import {Transformer} from "Sdk.Transformers";

const exclusive = Transformer.execute({ ... outputs: [d`ExclusiveDir`] ...});
const shared1 = Transformer.execute({ ... outputs: [{directory: d`SharedDir`, kind: "shared"}] ...});
const shared2 = Transformer.execute({ ... outputs: [{directory: d`SharedDir`, kind: "shared"}] ...});
const result = Transformer.execute({ ... dependencies: [exclusive, shared1, shared2], ...});
```
The process pips corresponding to `shared1` and `shared2` declarations may produce different sets of files in `SharedDir`
directory during their executions.

Details on sealed directories and output directories can be found in [Sealed Directories](../Advanced-Features/Sealed-Directories.md).

## File monitoring and caching implication

BuildXL relies on runtime file-access monitoring for correct caching. When executing a process pip, the file-access
monitoring produces a set of observations (file reads, path probes, directory enumerations). The runtime monitoring
does not report accesses to files specified explicitly as dependencies in the pip specification. The runtime monitoring
only produces observations for directory enumerations, absent path probes, and files accesses within directories specified
as dependencies.

The pip specification and the observations are used to compute the cache key for the pip. For files
specified explicitly as dependencies in the pip specification, and for files read in the observations, BuildXL uses their
content hashes to compute the cache key. Thus, any change to those files will result in the re-execution of the pip.
For directory enumerations, BuildXL computes fingerprints of the memberships of the enumerated directories, and uses
those fingerprints as part of the cache key. Thus, any addition or removal of a file in those enumerated directories
will result in the re-execution of the pip.

Recall that the runtime monitoring observes file accesses within directories specified as dependencies, but not files
explicitly specified as dependencies. Consider a scenario where a header folder has 100 header files (`file0.h`, ...,
`file99.h`):
```typescript
const sealedHeader = Transformer.sealSourceDirectory(d`Header`, Transformer.SealSourceDirectoryOption.allDirectories);
const p = Transformer.execute({ ... dependencies: [f`file.cpp`, ...glob(d`Header`, "*")]});
const q = Transformer.execute({ ... dependencies: [f`file.cpp`, sealedHeader]});
```
Let *P* and *Q* be the process pips corresponding to the declaration `p` and `q`, respectively. In the declaration of `p`,
all header files in `Header` are specified explicitly as dependencies by globbing the directory.
Suppose that `file.cpp` contains only
```cpp
#include "Header/file0.h"
int main() { return 0; }
``` 

During *P*'s execution, there is no observation to any file in `Header` because all header files are specified explicitly.
Then when caching *P*, all content hashes of all files in `Header` are included in the cache key. Now, if `file99.h`
changes, although it is not used at all by *P*, *P* will get a cache miss and be re-executed because `file99.h`'s
content hash is part of the cache key.

During *Q*'s execution, there will be a file read observation on `file0.h` and thus the file's content hash will be 
included in the cache key, so if it changes later, *Q* will be re-executed. But if `file99.h` changes, *Q* will get a cache
hit because the content hash of that file is not part of the cache key.

BuildXL caching mechanism is more complicated than what's been discussed in this section. For details, please refer
to [BuildXL Two-Phase Caching](../Advanced-Features/Two-Phase-Cache-Lookup.md).

## See also

* [The core concept of BuildXL](../CoreConcepts.md)
* [DScript as a build language](../DScript/Introduction.md)
* [Walkthrough: Building and running a simple program](./Walkthrough-Building-Hello-World.md)
* [Walkthrough: Using input and output directories](./Walkthrough-Building-InputOutputDirectories.md)
* [Walkthrough: Writing a DScript SDK](./Walkthrough-Building-SDK.md)

