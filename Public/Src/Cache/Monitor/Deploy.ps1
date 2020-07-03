param (
    [Parameter(Mandatory=$true, Position=0)] $monitorApplicationKey,
    [Parameter(Mandatory=$false, Position=1)] $setupPath = "C:/src/monitor",
    [Parameter(Mandatory=$false, Position=2)] $buildPath = "C:/work/BXL/Out/Bin",
    [Parameter(Mandatory=$false, Position=3)] $deploy = $false,
    [Parameter(Mandatory=$false, Position=4)] $remove = $false
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# nssm is used to wrap the executable and make it into a Windows service
$nssmUrl = "https://nssm.cc/ci/nssm-2.24-101-g897c7ad.zip"
$nssmFileName = Join-Path $setupPath -ChildPath "nssm.zip"
$nssmFolder = Join-Path $setupPath -ChildPath "nssm-2.24-101-g897c7ad/"
$nssmTemporaryExecutable = Join-Path $nssmFolder -ChildPath "win64/nssm.exe"
$nssmExecutable = Join-Path $setupPath -ChildPath "nssm.exe"

$monitorRelativePath = "debug/cache/netcoreapp3.1/win-x64/Monitor/App"
$monitorTemporaryPath = Join-Path -Path $buildPath -ChildPath $monitorRelativePath
$monitorPath = Join-Path $setupPath -ChildPath "bin"
$monitorExecutable = Join-Path $monitorPath -ChildPath "BuildXL.Cache.Monitor.App.exe"

$monitorServiceName = "CacheMonitor"
$monitorStdOutLog = Join-Path $setupPath "stdout.log"
$monitorStdErrLog = Join-Path $setupPath "stderr.log"

# Ensure we are running as administrator, otherwise we can't setup the service
# See: https://stackoverflow.com/questions/7690994/running-a-command-as-administrator-using-powershell
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{   
    $arguments = "& '" + $myinvocation.mycommand.definition + "'"
    Start-Process powershell -Verb runAs -ArgumentList $arguments
    Break
}

if ($remove) 
{
    # Kill the currently running service
    try 
    {
        Stop-Service -Name $monitorServiceName -Force

        # Need to use this for compatibility with old PowerShell versions
        $service = Get-WmiObject -Class Win32_Service -Filter "Name='$monitorServiceName'"
        $service.delete()

        Write-Output "Killed and removed monitor service"
    }
    catch 
    {
        Write-Output "No monitor service currently running"
    }

    # Clean up folders
    if (Test-Path $setupPath)
    {
        Remove-Item $setupPath -Recurse -Force
        Write-Output "Monitor folder at $setupPath removed"
    }

    [System.Environment]::SetEnvironmentVariable("CACHE_MONITOR_APPLICATION_KEY", $null, [System.EnvironmentVariableTarget]::Machine)

    Write-Output "All resources have been freed"
}

if ($deploy)
{
    if (Test-Path $setupPath -PathType Container)
    {
        Write-Error "Setup folder $setupPath already exists"
    }

    # Validation
    if (!(Test-Path $monitorTemporaryPath -PathType Container)) 
    {
        Write-Error "Monitor path $monitorTemporaryPath does not exist or is not a folder"
    }

    # Create workspace
    New-Item -Path $setupPath -ItemType Directory

    # Setup monitor binaries
    Copy-Item -Path $monitorTemporaryPath -Destination $monitorPath -Recurse
    if (!(Test-Path $monitorExecutable -PathType Leaf))
    {
        Write-Error "Could not find monitor executable at $monitorExecutable"
    }
    Remove-Variable monitorTemporaryPath

    # Download nssm
    Invoke-WebRequest -Uri $nssmUrl -OutFile $nssmFileName
    Expand-Archive -Path $nssmFileName -DestinationPath $setupPath
    if (!(Test-Path $nssmTemporaryExecutable -PathType Leaf)) 
    {
        Write-Error "Could not find nssm executable in downloaded package at $nssmTemporaryExecutable"
    }
    Copy-Item $nssmTemporaryExecutable $nssmExecutable
    Remove-Item $nssmFileName -Force
    Remove-Item $nssmFolder -Recurse -Force
    Remove-Variable nssmUrl
    Remove-Variable nssmFileName
    Remove-Variable nssmFolder
    Remove-Variable nssmTemporaryExecutable

    # Setup service and start it
    [System.Environment]::SetEnvironmentVariable("CACHE_MONITOR_APPLICATION_KEY", "$monitorApplicationKey", [System.EnvironmentVariableTarget]::Machine)
    Start-Process -NoNewWindow -Wait -FilePath $nssmExecutable -ArgumentList "install","$monitorServiceName","$monitorExecutable"
    Start-Process -NoNewWindow -Wait -FilePath $nssmExecutable -ArgumentList "set","$monitorServiceName","AppStdout","$monitorStdOutLog"
    Start-Process -NoNewWindow -Wait -FilePath $nssmExecutable -ArgumentList "set","$monitorServiceName","AppStderr","$monitorStdErrLog"
    Start-Process -NoNewWindow -Wait -FilePath $nssmExecutable -ArgumentList "set","$monitorServiceName","AppDirectory","$setupPath"
    Set-Service -Name $monitorServiceName -StartupType Automatic
    Start-Service -Name $monitorServiceName

    Write-Output "Deployment succeeded"
}
