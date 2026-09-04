[CmdletBinding()]
param(
    [datetimeoffset]$WindowStartUtc,

    [datetimeoffset]$WindowEndUtc,

    [string]$TrainingEndDate,

    [double]$TrainingLookbackDays,

    [ValidateSet('All', 'Prepare', 'Train')]
    [string]$Operation = 'All',

    [Parameter(Mandatory)]
    [string]$DatasetName,

    [Parameter(Mandatory)]
    [string]$DataRoot,

    [string]$ModelDir,

    [string]$ExportArtifactDir,

    [string]$QualityReport,

    [int]$TuningTrials,

    [double]$MaxNormalizedMae,

    [string]$PackageVersion,

    [int]$MaxBuilds = 0,

    [int]$BuildsPerCodebase = 100,

    [switch]$SyntheticFixture,

    [string]$NugetExecutable = 'nuget'
)

$ErrorActionPreference = 'Stop'

$computer = Get-CimInstance Win32_OperatingSystem
Write-Host ("Agent physical memory: {0:N2} GB" -f ($computer.TotalVisibleMemorySize / 1MB))

if ($Operation -in @('All', 'Prepare')) {
    if ($TrainingEndDate -and $TrainingEndDate -ne '2026-MM-DD') {
        $endDate = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($TrainingEndDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$endDate)) {
            throw "Training end date '$TrainingEndDate' must use YYYY-MM-DD, for example 2026-08-14."
        }
        if ($TrainingLookbackDays -le 0) {
            throw 'Training lookback days must be greater than zero.'
        }
        $WindowEndUtc = [datetimeoffset]::new($endDate.Year, $endDate.Month, $endDate.Day, 0, 0, 0, [timespan]::Zero)
        $WindowStartUtc = $WindowEndUtc.AddDays(-$TrainingLookbackDays)
    } elseif ($TrainingLookbackDays -gt 0) {
        $utcToday = [datetime]::UtcNow.Date
        $WindowEndUtc = [datetimeoffset]::new($utcToday.Year, $utcToday.Month, $utcToday.Day, 0, 0, 0, [timespan]::Zero)
        $WindowStartUtc = $WindowEndUtc.AddDays(-$TrainingLookbackDays)
    } elseif (-not $PSBoundParameters.ContainsKey('WindowStartUtc') -or -not $PSBoundParameters.ContainsKey('WindowEndUtc')) {
        throw 'Preparation requires TrainingLookbackDays, or explicit WindowStartUtc and WindowEndUtc.'
    }

    Write-Host "Training window: $($WindowStartUtc.ToUniversalTime().ToString('o')) through $($WindowEndUtc.ToUniversalTime().ToString('o'))"
    $datasetArguments = @(
        '-m', 'pip_usage.dataset',
        '--window-start-utc', $WindowStartUtc.ToUniversalTime().ToString('o'),
        '--window-end-utc', $WindowEndUtc.ToUniversalTime().ToString('o'),
        '--builds-per-codebase', $BuildsPerCodebase,
        '--dataset-name', $DatasetName,
        '--data-root', $DataRoot
    )
    if ($MaxBuilds -gt 0) {
        $datasetArguments += '--max-builds', $MaxBuilds
    }
    if ($SyntheticFixture) {
        $datasetArguments += '--synthetic-fixture'
    }
    python @datasetArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Pip Usage dataset preparation failed. See the Python error above.'
    }
}

$cohortDir = Join-Path $DataRoot "snapshots\$DatasetName"
$preparedPath = Join-Path $cohortDir 'prepared_parts'
$splitPath = Join-Path $cohortDir 'split_manifest.json'
if (-not (Test-Path $preparedPath) -or -not (Get-ChildItem $preparedPath -Filter '*.pkl' -File) -or -not (Test-Path $splitPath)) {
    Write-Host 'Preparation artifacts found:'
    Get-ChildItem -Path $DataRoot -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host $_.FullName }
    throw "Dataset preparation did not create '$preparedPath' and '$splitPath'."
}

if ($Operation -in @('All', 'Train')) {
    $trainingArguments = @(
        '-m', 'pip_usage.training',
        '--tuning-trials', $TuningTrials,
        '--dataset-name', $DatasetName,
        '--dataset-root', $DataRoot,
        '--output-dir', $ModelDir,
        '--quality-report', $QualityReport,
        '--max-normalized-mae', $MaxNormalizedMae
    )
    python @trainingArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Pip Usage model training or export failed. See the Python error above.'
    }

    if (-not (Test-Path $ModelDir)) {
        throw "Training finished without creating the model export directory: $ModelDir"
    }
    New-Item -ItemType Directory -Force -Path $ExportArtifactDir | Out-Null
    Copy-Item -Path (Join-Path $ModelDir '*') -Destination $ExportArtifactDir -Recurse -Force

    $nuspec = Join-Path $PSScriptRoot '..\model-package\BuildXL.ML.Models.nuspec'
    $packageOutput = Join-Path $PSScriptRoot '..\artifacts'
    New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null
    Get-ChildItem $packageOutput -Filter 'BuildXL.ML.Models.*.nupkg' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    & $NugetExecutable pack $nuspec `
        -Version $PackageVersion `
        -BasePath $ModelDir `
        -OutputDirectory $packageOutput `
        -NoPackageAnalysis
    if ($LASTEXITCODE -ne 0) {
        throw 'BuildXL.ML.Models data package creation failed.'
    }
}
