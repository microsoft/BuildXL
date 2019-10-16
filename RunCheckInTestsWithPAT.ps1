Param(
 [Parameter(mandatory=$true)]
 [String]$name,
 [Parameter(mandatory=$true)]
 [String]$value,
 [Parameter(mandatory=$true)]
 [String]$arg
)
[Environment]::SetEnvironmentVariable($name, $value, "Process")
.\RunCheckInTests.cmd /lab $arg /internal