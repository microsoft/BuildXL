# Instructions

1. Set the environment variable `BUILDXL_BIN` to the BuildXL binary folder containing `bxl.exe`. For example, if your BuildXL repo is in `D:\BuildXL`, then building BuildXL itself using `bxl.cmd -Minimal` will put the `bxl.exe` in `D:\BuildXL\Out\Bin\debug\win-x64`.
1. Point the environment variable BUILDXL_BIN to the BuildXL binary folder path. For example, if you set up BuildXL in D:\BuildXL, then D:\BuildXL\Out\Bin\debug\win-x64 should be the value.
1. Be aware that BuildXL requires the Ninja executable to evaluate the build specification. This means that `ninja.exe` should be in your `PATH`.   
1. Run .\build.ps1 from PowerShell, or equivalently build.bat from the command line prompt

The build outputs (`hello_copy.txt`, `hello_world.txt`) will be located in `Out`. For further configuration options, see the `NinjaResolverSettings` in https://github.com/microsoft/BuildXL/blob/main/Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc