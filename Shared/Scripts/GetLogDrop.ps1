<#
    Downloads all the logs for a BuildXL build session from the log drop.
    
    Users must provide the build id through the -BuildId parameter.

    The script looks for drop.exe in the DROP_EXE_LOCATION env variable and if it is not set
    it just tries to use "drop.exe" assuming it's in the PATH.
    
    The script uses the Batmon API to:
    - Find the location of the logdrop (this can be overriden with the -Uri parameter if necessary)
    
    - Find the names of the machines involved in the build (this can be overriden with the -Machines 
        parameter providing the list of builders for which to download the files)
    
    - Find the relevant targets to get the logs for (e.g. the different build steps for Office builds).
        These can be manually set through the -Targets parameter. 
        
        If unspecified, the script will download logs for all targets. 
        
        Use the -Template parameter (with 'Bxl', 'Office' or 'Cosine') to get the common targets for these builds
        (though this is only different from the default get-all in the Cosine case)

        Information on the targets end up in the all_targets.txt output file after the script runs.

    Files are output to the {buildId}.log folder or an output directory specified by the -Out parameter

    Example invocation: 
        GetLogDrop.ps1 -BuildId 4304a83a-9b8d-4fb0-a03a-1c6e6bc5a117 -Machines DM3AAPE8D393C27,DM3AAPE85F6F0D6 -Targets MetaBuild,ProductBuild
#>

param (
    [Parameter(Mandatory)]
    [string]
    $BuildId,

    [AllowEmptyString()]
    [string]
    $Uri, 

    [string[]]
    $Machines,

    [AllowEmptyString()]
    [string]
    $Out,

    [string[]]$Targets,

    [ValidateSet('Bxl','Office','Cosine')]
    $UseTemplate
)

Import-Module (Join-Path $PSScriptRoot Invoke-CBWebRequest.psm1) -Force -DisableNameChecking;

# Get build info from Batmon
Write-Host "Querying batmon API for build info..." -ForegroundColor Green
$response = Invoke-CBWebRequest -Method Get -Uri "https://cloudbuild.microsoft.com/batmon/build?id=$BuildId"

if ($response.StatusCode -ne 200) {
    Write-Host Call to batmon returned with non-OK exit code. Exiting... -ForegroundColor Red
    exit
}

$data = ($response.Content | ConvertFrom-Json)
$info = $data.Info

# Prep output directory
if (!$Out) 
{
    $Out = "$BuildId.logs"
    Write-Host "Using the default output folder $Out"
}
if (!(Test-Path $Out))
{
New-Item -itemType Directory -Name $Out | Out-Null
}


# Write the relevant batmon info in some txt files, useful for debugging any failure
echo $data > "$Out\build_info.txt"
echo $data.Targets > "$Out\all_targets.txt"


# Parse the build info for the log drop location if it wasn't specified as a parameter
$logdropsettings = $info.LogDropsSettings
if ($Uri) {
    $logDropUri = $Uri
}
elseif ($logdropsettings) {
    $logDropUri = "$($logdropsettings.DropConnectionString)/_apis/drop/drops/$($logdropsettings.ConfiguredDropLabel)"
    echo $logDropUri
    Write-Host "`n Found log drop URI: $logDropUri `n" -ForegroundColor Green
}
else
{
    # Some builds don't have log drop so there will be nothing there. Fail in that case.
    Write-Host "Couldn't find the log drop data in build info." -ForegroundColor Red
    Write-Host "You can specify the log drop URI manually with the -Uri parameter. Exiting..." -ForegroundColor Red
    Write-Host "Build info was output to $Out/build_info.txt" -ForegroundColor Red
    exit
}


if ($Targets) {
    # User specified targets
    $selectedTargets = $Targets
}
elseif ($UseTemplate) {
    Write-Host Selecting target from template $Template -ForegroundColor Green
    switch ($Template)
    {
        # Defaults: build targets for Cosine, Office and BuildXL builds
        # For Cosine we skip all the BuildXLRunner / Razzle stuff and only keep the bxl invocation logs, 
        # do not use -UseTemplate to download everything. For Office and BuildXL builds these
        # targets are all there is so it's equivalent to not using -UseTemplate and grabbing all targets
        'Cosine' { $selectedTargets = @('BxlBuild'); break; }
        'Office' { $selectedTargets = @('EnlistBuild', 'MetaBuild', 'ProductBuild'); break; }
        'Bxl' { $selectedTargets = @('Logs'); break; }
    }
}
else {
    Write-Host "Selecing all available targets from the batmon data. To override this behavior use the -Template or -Targets parameter `n" -ForegroundColor Green
    $selectedTargets = @()
    foreach ($target in $data.Targets)
    {
        $selectedTargets += $target.Name
    }
}

Write-Host "Selected targets: $($selectedTargets -join ', ')"

$logLocations = @()
foreach ($target in $data.Targets)
{
    if ($selectedTargets.Contains($target.Name)) {
        $logLocations += $target.LogLocation.Replace("\", "/")
    }
}

if ($Machines) {
    $builders = $Machines
}
else
{
    Write-Host "`n Will download logs for all the machines in the build. Specify a subset with the -Machines parameter." -ForegroundColor Green
    $builders = @()
    foreach ($builder in $info.Builders)
    {
        $builders += $builder.Split(":")[0] # Split to remove port
    }
}

$roots = ""
ForEach ($machine in $builders)
{
    ForEach($logLoc in $logLocations) {
        $roots = "$roots;$machine/build/$logLoc"
    }
}
$roots = $roots.Substring(1); # Get rid of leading semicolon
$roots = '"' + $roots + '"'   # drop.exe wants the parameter between quotes


$drop_script_location = Join-Path $PSScriptRoot '..\..\drop.cmd'

Write-Host "Calling drop get to download the files... `n" -ForegroundColor Green
& $drop_script_location get -a -r $roots -u $logDropUri -d $Out
Write-Host `n Finished the call to drop. The files were output to the $Out directory -ForegroundColor Green