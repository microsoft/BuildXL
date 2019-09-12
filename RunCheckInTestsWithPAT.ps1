Param(
 [Parameter(mandatory=$true)]
 [String]$name,
 [Parameter(mandatory=$true)]
 [String]$value,
 [Parameter(mandatory=$true)]
 [String]$name2,
 [Parameter(mandatory=$true)]
 [String]$value2,
 [Parameter(mandatory=$true)]
 [String]$arg
)
[Environment]::SetEnvironmentVariable($name, $value,"Process")
[Environment]::SetEnvironmentVariable($name2, $value2,"Process")
# .\RunCheckInTests.cmd /lab $arg /internal
.\bxl.cmd /q:ReleaseNet472 /q:ReleaseDotNetCore /f:output='out/bin/release/public/pkgs/*' /server- /logOutput:FullOutputOnWarningOrError /enableGrpc+ /traceInfo:prvalidation=TestingPart4 /enableIncrementalFrontEnd-
q