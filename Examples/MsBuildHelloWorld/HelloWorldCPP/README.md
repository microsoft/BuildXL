This example demonstrates how to build a simple Hello World VCX project using MSBuild frontend.

# Instructions

1. Open a CMD console.
2. Set the environment variable `BUILDXL_BIN` to the BuildXL binary folder containing `bxl.exe`. For example, if your BuildXL repo
is in `D:\BuildXL`, then building BuildXL itself using `bxl.cmd -Minimal` will put the `bxl.exe` in `D:\BuildXL\Out\Bin\debug\win-x64`.
3. Run `build.cmd`.

The build outputs will be located in the `Debug` folder. For further configuration options, see [MsBuildResolver interface definition](../../../Public/Sdk/Public//Prelude//Prelude.Configuration.Resolvers.dsc).