# Walkthrough: Using input/output directories

In this walkthrough, we will specify a build that uses input and output directories, and show the caching behavior.
The example in this walkthrough can be found in [Using Input/Output Directories](../../../Examples/Walkthrough/InputOutputDirectories/).

## Prerequisites

The example in this walkthrough runs both on Linux and Windows. For Linux, the example has been tested on Ubuntu 20.04
and Ubuntu 22.04. For Windows, since we are going to use `g++` compiler tool to compile our example C++ program,
you need to install the latest version of Mingw-w64 via MSYS2, which provides up-to-date native builds of GNU C++ tools
and libraries. You can download the latest installer from the [MSYS2 page](https://www.msys2.org/), and then follow
the installation instructions on the MSYS2 website to install Mingw-w64 install the GNU compiler toolset.

The example in this walkthrough will use concepts that have been explained in [Hello World](./Walkthrough-Building-Hello-World.md) walkthrough,
so we recommend that you should to read that walkthrough first.

## Build specification

Our build specification consists only of a single module named `InputOutputDirectories`. In this build we will
compile a C++ program, and use that program to copy files from one directories to another:
```cpp
#include <fstream>
#include <string>

int main(int argc, char **argv)
{
    std::ifstream in_file1(std::string(argv[1]) + "/" + "1.txt");
    std::ifstream in_file2(std::string(argv[1]) + "/" + "2.txt");

    std::ofstream out_file1(std::string(argv[2]) + "/" + "1.txt");
    std::ofstream out_file2(std::string(argv[2]) + "/" + "2.txt");

    out_file1 << in_file1.rdbuf();
    out_file2 << in_file2.rdbuf();
    return 0;
} 
```
That is, given an input directory and an output directory as arguments, the program will copy files `1.txt`
and `2.txt` from the input directory to the output directory. In the 
[Hello World](./Walkthrough-Building-Hello-World.md) walkthrough we have seen how to compile a C++ program.

Now, we want to use the program to copy files `Input/1.txt` and `Input/2.txt` to some output folder, called "Staging".
To this end, we can specify those files explicity as static dependencies of the pip that will run the program. However,
if the program author later decides to also copy `Input/3.txt`, then the specification needs to change to include
`Input/3.txt` as a static dependency. Now, what if we specify `Input/3.txt` as a dependency from the beginning?
This will give an unoptimized cached build, i.e., if `Input/3.txt` changes, then the pip
will re-run (cache miss) although the program execution did not read `Input/3.txt`. To address this issue, we will
declare `Input` as an input directory, as shown below:
```typescript
// Run main.exe, read files in Input directory, and produce output files in Staging directory.
const mainExe = mainCompileResult.getOutputFile(mainExePath);
const sealedInputDir = Transformer.sealSourceDirectory(d`Inputs`, Transformer.SealSourceDirectoryOption.allDirectories);
const stagingDirPath = Context.getNewOutputDirectory("Staging");
const mainRunStagingResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(sealedInputDir)),
        Cmd.argument(Artifact.output(stagingDirPath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});
```

To use `Input` as an input directory, we need to seal that directory first, i.e., declaring the content of the directory.
Since `Input` is a source directory, we can simply use `Transformer.sealSourceDirectory` function. Then, we specify that
directory as an input to the pip by "tagging" it using `Artifact.input`.

We also declare the staging directory as the output directory. The location of the staging directory is determined by
BuildXL by calling `Context.getNewOutputDirectory` function. Typically, it is located deep under the `Out/Objects` folder.
Calling `Context.getNewOutputDirectory` guarantees that you will have a unique directory for output.

The next step is to copy files from the staging directory to the final directory `Out/Objects/Final`. Here, we will use
the output directory of the previous process pip as an input directory of the following process pip:
```typescript
// Run main.exe, read files in Staging directory, and produce output files in Final directory.
const stagingDir = mainRunStagingResult.getOutputDirectory(stagingDirPath);
const finalDirPath = d`${outputDir}/Final`;
const mainRunFinalResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(stagingDir)),
        Cmd.argument(Artifact.output(finalDirPath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});
```

## Running the example
You can run the example by invoking this command:
```console
> ./bxl /c:config.dsc
```
The output of the run will be like:
```console
Microsoft (R) Build Accelerator.
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: 7f920fe5-0000-0000-0000-9f1c76ae973a
[0:05] 100.00%  Processes:[3 done (0 hit), 0 executing, 0 waiting]
[0:05] -- Cache savings: 0.000% of 3 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/InputOutputDirectories/Out/Logs
```
The build has 3 process pips, one for compiling the program, and the others are the ones that copy the files.

Now, if we modify `Input/3.txt`, we will get 100% cache hit rate because that file was not accessed during
the previous build:
```console
> echo foo >> Input/3.txt
> ./bxl /c:config.dsc
Microsoft (R) Build Accelerator.
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: 99e4b7d2-0000-0000-0000-1957c5dfe5b3
[0:04] 100.00%  Processes:[3 done (3 hit), 0 executing, 0 waiting]                                   
[0:04] -- Cache savings: 100.000% of 3 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/InputOutputDirectories/Out/Logs
```

However, if we modify `Input/1.txt` or `Input/2.txt`, all the pips that perform the file copies will re-run:
```console
> echo foo >> Input/1.txt
> ./bxl /c:config.dsc
Microsoft (R) Build Accelerator. Build: 0.1.0-devBuild, Version: [Developer Build]
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: d95cbf38-0000-0000-0000-d87b1be3898e
[0:04] 100.00%  Processes:[3 done (1 hit), 0 executing, 0 waiting]                                
[0:04] -- Cache savings: 33.333% of 3 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/InputOutputDirectories/Out/Logs
```
Note that we still have a cache hit from the pip that compiles the program itself.