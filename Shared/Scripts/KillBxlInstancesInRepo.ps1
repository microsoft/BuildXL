# Kills any instance of bxl.exe that is running in the current repo

# We assume the location of this script in the repo and traverse up to the root of the repo
$normalPattern = (get-item $PSScriptRoot).Parent.Parent.FullName.ToString() + "\%\bxl.exe"
$normalPattern = $normalPattern.Replace("\", "\\")

# The MSBuild server build builds to <root>\..\bin\_MSBuild_bootstrap
$serverPattern = (get-item $PSScriptRoot).Parent.Parent.Parent.FullName.ToString() + "\bin\_MSBuild_bootstrap\%\bxl.exe"
$serverPattern = $serverPattern.Replace("\", "\\")

$command = "wmic process where ""ExecutablePath like '" + $normalPattern + "' or ExecutablePath like '" + $serverPattern + "'"" delete"
Invoke-Expression $command | Out-Null
