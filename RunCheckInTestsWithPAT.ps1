Param(
 [Parameter(mandatory=$true)]
 [String]$1esPat,
 [Parameter(mandatory=$true)]
 [String]$cbPat,
 [Parameter(mandatory=$true)]
 [String]$ncPath,
 [Parameter(mandatory=$true)]
 [String]$args
)
[Environment]::SetEnvironmentVariable("1ESSHAREDASSETS_BUILDXL_FEED_PAT", $1esPat, "Process")
[Environment]::SetEnvironmentVariable("CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT", $cbPat, "Process")
[Environment]::SetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH", $ncPath)
.\RunCheckInTests.cmd /lab $args /internal