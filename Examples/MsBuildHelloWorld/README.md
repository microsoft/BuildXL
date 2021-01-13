# Instructions

1. Setup BuildXL as explained in the documentation
2. Point the environment variable `BUILDXL_BIN` to the BuildXL binary folder path. For example, if you set up BuildXL in `D:\BuildXL`, then `D:\BuildXL\Out\Bin\debug\win-x64` should be the value.
3. Optionally specify searchs locations for the MSBuild directory in `config.bc` through the `msBuildSearchLocations` parameter. This also helps to consistently load the assemblies needed by MSBuild from those same locations. If this is not specified, the `PATH` environment variable will be used to find `MSBuild.exe` and the required assemblies.
4. Run `.\run.ps1` from PowerShell, or equivalently `run.bat` from the command line prompt

The build outputs will be located in the `Debug` folder. For further configuration options, see the [MsBuildResolver interface definition](https://github.com/microsoft/BuildXL/blob/master/Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc#L152) 