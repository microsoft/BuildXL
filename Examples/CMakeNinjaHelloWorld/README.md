# Instructions
1. Set the environment variable `BUILDXL_BIN` to the BuildXL binary folder containing `bxl.exe`. For example, if your BuildXL repo is in `D:\BuildXL`, then building BuildXL itself using `bxl.cmd -Minimal` will put the `bxl.exe` in `D:\BuildXL\Out\Bin\debug\win-x64`.
1. Point the environment variable `BUILDXL_BIN` to the BuildXL binary folder path. For example, if you set up BuildXL in `D:\BuildXL` and built the repository, then `D:\BuildXL\Out\Bin\debug\win-x64` should be the value.
1. Run `.\build.ps1` from PowerShell, or equivalently `build.bat` from the command line prompt. This will:
    1. Generate the `build.ninja` specification by running `cmake -GNinja` in this directory. The next step depends on the `CMakeNinjaHelloWorld\build.ninja` file existing as the output of this invocation.
    1. Run the build with BuildXL using the Ninja resolver
1. Subsequent runs of this script should be fully cached. The Ninja-generation step (step 2) should be re-run if the CMake specifications change.

For further configuration options for the Ninja resolver, see [Resolvers](../../Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc).