# Instructions

1. Setup BuildXL as explained in the documentation
2. Point the environment variable BUILDXL_BIN to the BuildXL binary folder path. For example, if you set up BuildXL in D:\BuildXL, then D:\BuildXL\Out\Bin\debug\win-x64 should be the value.
3. Optionally specify search locations for the CMake binary in `config.bc` through the `cMakeSearchLocations` parameter. If this is not specified, the `PATH` environment variable will be used to find `CMake.exe`.
4. Be aware that BuildXL uses the Ninja CMake generator and CMake requires the Ninja executable to carry out the build. This means that `ninja.exe` should be in your `PATH` or its location specified through the `CMAKE_MAKE_PROGRAM` variable through the `cacheEntries` property in `config.bc`.   
5. Run .\build.ps1 from PowerShell, or equivalently build.bat from the command line prompt

The build outputs will be located in Out/Example. For further configuration options, see: https://github.com/microsoft/BuildXL/blob/main/Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc#L285