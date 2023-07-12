This example demonstrates a hybrid build using MSBuild and DScript frontends. In this build, we create a process pip, via DScript
resolver, that copies the result `Hello.exe` of building the simple Hello World C# project to `Hello.copy.exe`.

# Prerequisite
1. Install Visual Studio.

# Instructions

1. Open a Developer command prompt for VS.
2. Restore project: `msbuild /t:restore`.
2. Set the environment variable `BUILDXL_BIN` to the BuildXL binary folder containing `bxl.exe`. For example, if your BuildXL repo
is in `D:\BuildXL`, then building BuildXL itself using `bxl.cmd -Minimal` will put the `bxl.exe` in `D:\BuildXL\Out\Bin\debug\win-x64`.
3. Run `build.cmd`.

The build outputs will be located in the `bin` folder. For further configuration options, see [MsBuildResolver interface definition](../../../Public/Sdk/Public//Prelude//Prelude.Configuration.Resolvers.dsc).

