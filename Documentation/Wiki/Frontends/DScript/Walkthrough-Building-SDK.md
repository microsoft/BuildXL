# Walkthrough: Writing a DScript SDK

In this walkthrough, we will write a DScript SDK for building a small C++ project consisting of more than one module.
We will also see how one module consumes the result of another module. The example in this walkthrough can be found
in [Writing SDK](../../../../Examples/Walkthrough/SimpleSDK).

## Prerequisites

The example in this walkthrough runs both on Linux and Windows.
For Windows, since we are going to use `g++` compiler tool to compile our example C++ program,
you need to install the latest version of Mingw-w64 via MSYS2, which provides up-to-date native builds of GNU C++ tools
and libraries. You can download the latest installer from the [MSYS2 page](https://www.msys2.org/), and then follow
the installation instructions on the MSYS2 website to install Mingw-w64 install the GNU compiler toolset.

The example in this walkthrough will use concepts that have been explained in [Hello World](./Walkthrough-Building-Hello-World.md) and [Using Input/Output Directories](./Walkthrough-Building-InputOutputDirectories.md)
walkthroughs, so we recommend that you should to read those walkthroughs first.

## A small C++ project

The project that we are going to build has the following structure:
```
Examples/Walkthrough/SimpleSDK/
├── Include
│   └── hello.h
└── Src
    ├── HelloApp
    │   └── main.cpp
    └── HelloLib
        ├── greetings.h
        └── hello.cpp
```
The project has 2 modules, `HelloLib` and `HelloApp`. The former one creates a library that will be used to create
the application in the latter module. The `Include` folder contains headers needed by the `.cpp` files in both modules.

The following are the files in the `HelloLib` module:
```cpp
// greetings.h
char* Greetings();

// hello.cpp
#include "hello.h"
#include "greetings.h"
#include <iostream>

void SayHello()
{
    std::cout << Greetings() << std::endl;
}

char* Greetings()
{
    return "Hello, World!";
}
```
And the following is the `main.cpp` file in the `HelloApp` module:
```cpp
// main.cpp
#include "hello.h"

int main()
{
    SayHello();
    return 0;
}
```
Both modules `#include` the `hello.h` header file from the `Include` folder:
```cpp
// hello.h
void SayHello();
```

We are going to write our DScript SDK in the `Sdk` folder adjacent to the `Src` and `Include` folders. Thus, our build
specification has the following organization:
```
Examples/Walkthrough/SimpleSDK/
├── config.dsc
├── Include
│   └── hello.h
├── Sdk
│   ├── module.config.dsc
│   └── MySdk.dsc
└── Src
    ├── HelloApp
    │   ├── HelloApp.dsc
    │   ├── main.cpp
    │   └── module.config.dsc
    └── HelloLib
        ├── greetings.h
        ├── hello.cpp
        ├── HelloLib.dsc
        └── module.config.dsc
```

## Simple SDK

To build the project, we need to
1. compile `hello.cpp` to `hello.o`,
2. compile `main.cpp` to `main.o`, and
3. link `hello.o` and `main.o` to `app.exe`
One can call `Transformer.execute` in the DScript specs `HelloLib.dsc` and `HelloApp.dsc` to create process pips for
compilation and linking. However, this approach is not sustainable. For example, if we need to uniformly change
the compiler flag, then we need to modify all DScript specs.

We write an SDK to as an abstraction of the compilation and linking processes. First, the SDK declares an interface
used for compilation:
```typescript
@@public
export interface CompileArgs
{
    cFile: File;
    includes?: File[];
    includeSearchDirs?: StaticDirectory[];
    optimize?: boolean;
}
```
This interface abstracts away the details of compiler options. For our purpose, we only need to specify the `.cpp` file,
the `.h` header files (optional), the search directories (optional), and whether or not we want to enable optimization.

The SDK also determines the compiler tool used for compilation and linking:
```typescript
const compilerTool = {
    exe: Context.isWindowsOS() ? f`C:/msys64/ucrt64/bin/g++.exe` : f`/usr/bin/g++`,
    dependsOnCurrentHostOSDirectories: true,
    prepareTempDirectory: true,
    untrackedDirectoryScopes: Context.isWindowsOS()
        ? [d`C:/msys64/ucrt64`]
        : undefined
};
```
Note that the compiler tool is not exposed out of the SDK. In this way, the author of the SDK can change the compiler tool
easily (e.g., `g++` to `clang++`) without worrying that the consumer of the SDK making assumption about the compiler tool.

For compilation, we write the following `compile` function:
```typescript
@@public
export function compile(args: CompileArgs) : File
{
    const outDir = Context.getNewOutputDirectory("compile");
    const objFile = p`${outDir}/${args.cFile.name.changeExtension(".o")}`;
    const result = Transformer.execute({
        tool: compilerTool,
        arguments: [
            Cmd.argument("-c"),
            Cmd.flag("-O3", args.optimize),
            Cmd.argument(Artifact.input(args.cFile)),
            Cmd.options("-I ", Artifact.inputs(args.includeSearchDirs)),
            Cmd.option("-o ", Artifact.output(objFile)),
        ],
        dependencies: args.includes || [],
        workingDirectory: d`${args.cFile.parent}`,
        environmentVariables: Context.isWindowsOS()
            ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
            : undefined
    });
    return result.getOutputFile(objFile);
}
```
The function takes a `CompileArgs` object as an input and returns an object file. For the compile pip, we establish
dependencies on the `.cpp` file by calling `Artifact.input(args.cFile)`. We also establish directory dependencies
on all the include search directories. The output file is an object file whose location is determined by BuildXL.

The interface and function for linking object files can be written in a similar manner.

## Using the SDK

To use the SDK, one simply `import` it. Let's take a look at the `HelloApp.dsc` spec file:
```typescript
import * as MySdk from "MySdk";
import * as HelloLib from "HelloLib";

const includeDir = Transformer.sealSourceDirectory(d`../../Include`, Transformer.SealSourceDirectoryOption.allDirectories);

const obj = MySdk.compile({
    cFile: f`main.cpp`,
    includeSearchDirs: [includeDir],
    optimize: true
});

const exe = MySdk.link({
    objFiles: [obj, HelloLib.obj],
    output: p`${Context.getMount("ObjectRoot").path}/app.exe`
});
```
First, we import our SDK (named "MySdk" in this case). Then, to compile `main.cpp`, we simply call `MySdk.compile`. Note
that we specify the `Include` directory as an input directory, so that we need to seal it first.

Next, we link `main.o` and `hello.o` resulting from compiling `hello.cpp` in `HelloLib` module. To access the value of
`hello.o` in `HelloLib` module, we `import` the module and select its `obj` declaration.

## Running the example

You can run the example by invoking this command:
```console
> ./bxl /c:config.dsc
```
The output of the run will be like:
```console
Microsoft (R) Build Accelerator.
Copyright (C) Microsoft Corporation. All rights reserved.

[0:00] -- Telemetry is enabled. SessionId: bb41ed1d-0000-0000-0000-03040fecfe28
[0:05] 100.00%  Processes:[3 done (0 hit), 0 executing, 0 waiting]                                                  
[0:05] -- Cache savings: 0.000% of 3 included processes. 0 excluded via filtering.
Build Succeeded
    Log Directory: /BuildXL/Examples/Walkthrough/SimpleSDK/Out/Logs
```

There are 3 process pips: 2 compile pips (for `main.cpp` and `hello.cpp`), and 1 link pip (for linking `main.o`
and `hello.o`). After the run, the folder `Out/Objects` will have the `app.exe` file that you can run:
```console
> ./Out/Objects/app.exe
Hello, World!
```