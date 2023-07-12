# Walkthrough: Building and running a simple program

In this walkthrough, we will specify a build that consists of compiling a C++ program, running the resulting executable,
and deploying the result of running the executable to a designated output folder. The example in this walkthrough will
expose the uses of transformer functions in DScript built-in *Sdk.Transformers* to create process pip, write-file pip, and
copy-file pip. The example in this walkthrough can be found in [HelloWorld](../../../../Examples/Walkthrough/HelloWorld).

## Prerequisites

The example in this walkthrough runs both on Linux and Windows. For Linux, the example has been tested on Ubuntu 20.04
and Ubuntu 22.04. For Windows, since we are going to use `g++` compiler tool to compile our example C++ program,
you need to install the latest version of Mingw-w64 via MSYS2, which provides up-to-date native builds of GNU C++ tools
and libraries. You can download the latest installer from the [MSYS2 page](https://www.msys2.org/), and then follow
the installation instructions on the MSYS2 website to install Mingw-w64 install the GNU compiler toolset.

## Build specification

Our build specification consists only of a single module called `HelloWorld`, as shown in the configuration file
`config.dsc`:
```typescript
config({
    resolvers: [
        {
            kind: "DScript",
            modules: [ f`module.config.dsc` ]
        },
    ],
    mounts: Context.isWindowsOS()
        ? [
            {
                name: a`MSys`,
                path: p`C:/msys64`,
                trackSourceFileChanges: true,
                isReadable: true
            },
          ]
        : []
});
```
Especially for Windows build, since we are going to use Mingw, installed in `C:\msys64` by default, we need be
able to track files in that installation folder. To that end, we specify a readable and trackable mount for
`C:\msys64`. For Linux build, we do not need to add another mount because BuildXL automatically adds `/bin`, `/usr/bin`,
`/usr/lib`, and `/usr/include` as readable and trackable mounts.

The C++ program to be compiled in this build is a program that copies a file from one location (first argument) to
another (second argument):
```c++
#include <fstream>

int main(int argc, char **argv)
{
    std::ifstream in_file(argv[1]);
    std::ofstream out_file(argv[2]);

    out_file << in_file.rdbuf();
    return 0;
} 
```

The DScript spec `HelloWorld.dsc` describes what the build should do. The first step in the build is to compile
the C++ program itself by creating a process pip using the `Transformer.execute` function:
```typescript
// Compile main.cpp to main.exe.
const outputDir = d`${Context.getMount("ObjectRoot").path}`;
const mainExePath = p`${outputDir}/main.exe`;
const mainCompileResult = Transformer.execute({
    tool: {
        exe: Context.isWindowsOS() ? f`C:/msys64/ucrt64/bin/g++.exe` : f`/usr/bin/g++`,
        dependsOnCurrentHostOSDirectories: true,
        prepareTempDirectory: true,
        untrackedDirectoryScopes: Context.isWindowsOS()
            ? [d`C:/msys64/ucrt64`]
            : undefined
    },
    arguments: [
        Cmd.argument(Artifact.input(f`main.cpp`)),
        Cmd.option("-o", Artifact.output(mainExePath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});
```
The tool that the process pip is going to execute is `g++`. By specifying it as an executable (`exe`) in the `tool`
definition, the process pip establishes a static dependency on the `g++` file. For simplicity, we untrack file
accesses under system directories, like `/bin`, `/lib`, `/usr/bin` because we decide that they are not relevant for caching
the pip. By untracking a directory, the runtime monitoring will not report any file access under that directory. In Linux,
this is done by setting `dependsOnCurrentHostOSDirectories` to `true`. For Windows, since we are using Mingw, we manually
untrack the installation folder of `Mingw`, i.e., `C:\msys64\ucrt64`.

When running, the `g++` compiler can create temporary files in the temporary directory. To guarantee a reproducible build,
we need to create a unique temporary directory for the pip, and redirect `TEMP` or `TMP` environment varibles to that
directory. To this end, we simply set `prepareTempDirectory` to `true`.

The resulting executable `main.exe` will be produced in the BuildXL's object root. By default this object root is
the `Out/Objects` folder, where the `Out` folder is adjacent to the configuration file `config.dsc`. 
The arguments for the process specified in the pip is `main.cpp -o Out/Objects/main.exe`. By "tagging" `main.cpp`
with `Artifact.input`, we declare a static dependency on `main.cpp`. We also declare `Out/Objects/main.exe`
as an output path for the pip.

When running on Windows, `g++.exe` needs to find other tools using the `PATH` environment variable. We made this
variable visible for the pip, but we restrict the value to the path needed by `g++.exe`.

The next step is to create a file `main.in` that `main.exe` will read as an input. For this, we are creating a write-file
pip:
```typescript
// Write input file for main.exe.
const mainInput = Transformer.writeAllLines(p`${outputDir}/main.in`, ["Hello, world!"]);
```
This pip will write "Hello, world!" at `Out/Objects/main.in`.

Once we have pips for `main.exe` and for writing input file `main.in`, we create a process pip that will execute `main.exe`
on `main.in`:
```typescript
// Run main.exe to produce main.out.
const mainExe = mainCompileResult.getOutputFile(mainExePath);
const mainOutputPath = p`${outputDir}/main.out`;
const mainRunResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(mainInput)),
        Cmd.argument(Artifact.output(mainOutputPath))
    ],
    workingDirectory: outputDir,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});
```
This process pip simply run `Out/Objects/main.exe Out/Objects/main.in Out/Objects/main.out`. Note that, by setting `main.exe`
as the executable of this pip, this pip establishes a dependency on the pip the produces `main.exe`. Also, the content of
`main.exe` will be used as part of the cache key of this pip.

Finally, we simply duplicate `main.out` to `main_copy.out` by copying it using a copy-file pip:
```typescript
// Copy main.out to main_copy.out.
const mainOutputCopy = Transformer.copyFile(mainRunResult.getOutputFile(mainOutputPath), p`${outputDir}/main_copy.out`);
```

The constructed build graph looks like the following:

![Walkthrough HelloWorld](Images/WalkthroughHelloworld.png)


## Running the example

You can run the example by invoking this command:
```console
> ./bxl /c:config.dsc
```
The output of the run will be like:
```console
Microsoft (R) Build Accelerator.
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: 14b5a016-0000-0000-0000-86fa773a6e7e
[0:05] 100.00%  Processes:[2 done (0 hit), 0 executing, 0 waiting] Files:[2/2]
[0:05] -- Cache savings: 0.000% of 2 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/HelloWorld/Out/Logs
```
The build has 2 process pips as also shown by the build graph. If you check the `Out/Objects` folder, you will
see the `main_copy.out` file containing "Hello, world!".

If we run the build again without changing anything, we will have 100% cache hit, i.e., cache hits for both process pips:
```console
Microsoft (R) Build Accelerator.
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: 91e6c246-0000-0000-0000-90f8498cda90
[0:04] 100.00%  Processes:[2 done (2 hit), 0 executing, 0 waiting] Files:[2/2]
[0:04] -- Cache savings: 100.000% of 2 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/HelloWorld/Out/Logs
```