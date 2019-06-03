# Instructions

1. Setup BuildXL as explained in the documentation
2. Point the environment variable BUILDXL_BIN to the BuildXL binary folder path. For example, if you set up BuildXL in D:\BuildXL, then D:\BuildXL\Out\Bin\debug\net472 should be the value.
3. Run .\run.ps1 from PowerShell, or equivalently run.bat from the command line prompt

The build outputs will be located in Out/Example. For further configuration options, see: https://github.com/microsoft/BuildXL/blob/master/Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc#L285