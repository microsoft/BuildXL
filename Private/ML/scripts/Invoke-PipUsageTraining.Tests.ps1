BeforeAll {
    $scriptPath = Join-Path $PSScriptRoot 'Invoke-PipUsageTraining.ps1'
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
}
Describe 'Invoke-PipUsageTraining' {
    It 'runs dataset preparation and training with the supplied bounded inputs' {
        $dataRoot = Join-Path $TestDrive 'data'
        $modelDir = Join-Path $TestDrive 'model'
        $exportDir = Join-Path $TestDrive 'export'
        $qualityReport = Join-Path $TestDrive 'quality.json'
        $nugetScript = Join-Path $TestDrive 'nuget.ps1'
        $datasetName = 'smoke-dataset'
        $global:PipUsagePythonCalls = @()
        $global:PipUsageDotnetCalls = @()
        Set-Content $nugetScript 'param([Parameter(ValueFromRemainingArguments=$true)]$Arguments); $global:PipUsageNugetCalls += ,@($Arguments); $global:LASTEXITCODE = 0'

        function global:python {
            $global:PipUsagePythonCalls += ,@($args)
            if ($args -contains 'pip_usage.dataset') {
                $cohort = Join-Path $dataRoot "snapshots\$datasetName"
                New-Item -ItemType Directory -Force -Path (Join-Path $cohort 'prepared_parts') | Out-Null
                Set-Content -Path (Join-Path $cohort 'prepared_parts\00.pkl') -Value 'prepared'
                Set-Content -Path (Join-Path $cohort 'split_manifest.json') -Value '{}'
            } elseif ($args -contains 'pip_usage.training') {
                New-Item -ItemType Directory -Force -Path $modelDir | Out-Null
                Set-Content -Path (Join-Path $modelDir 'model_spec.json') -Value '{}'
                Set-Content -Path $qualityReport -Value '{}'
            }
            $global:LASTEXITCODE = 0
        }

        $global:PipUsageNugetCalls = @()
        $expectedWindowEnd = [datetime]::UtcNow.Date
        $expectedWindowStart = $expectedWindowEnd.AddDays(-7)

        try {
            & $scriptPath `
                -Operation Prepare `
                -TrainingEndDate '2026-MM-DD' `
                -TrainingLookbackDays 7 `
                -DatasetName $datasetName `
                -DataRoot $dataRoot `
                -MaxBuilds 100 `
                -BuildsPerCodebase 25
            & $scriptPath `
                -Operation Train `
                -DatasetName $datasetName `
                -DataRoot $dataRoot `
                -ModelDir $modelDir `
                -ExportArtifactDir $exportDir `
                -QualityReport $qualityReport `
                -TuningTrials 1 `
                -MaxNormalizedMae 1.0 `
                -PackageVersion '0.0.1-smoke' `
                -NugetExecutable $nugetScript
        } finally {
            Remove-Item Function:\python -Force
            Remove-Item (Join-Path $repoRoot 'Private\ML\artifacts') -Recurse -Force -ErrorAction SilentlyContinue
        }

        $global:PipUsagePythonCalls.Count | Should -Be 2
        $datasetCall = $global:PipUsagePythonCalls[0] -join ' '
        $datasetCall | Should -Match 'pip_usage.dataset'
        $datasetCall | Should -Match ([regex]::Escape("--window-start-utc $($expectedWindowStart.ToString('yyyy-MM-dd'))T00:00:00.0000000+00:00"))
        $datasetCall | Should -Match ([regex]::Escape("--window-end-utc $($expectedWindowEnd.ToString('yyyy-MM-dd'))T00:00:00.0000000+00:00"))
        $datasetCall | Should -Match '--max-builds 100'
        $datasetCall | Should -Match '--builds-per-codebase 25'
        $trainingCall = $global:PipUsagePythonCalls[1] -join ' '
        $trainingCall | Should -Match 'pip_usage.training'
        $trainingCall | Should -Match '--tuning-trials 1'
        $trainingCall | Should -Match '--max-normalized-mae 1'
        (Test-Path (Join-Path $exportDir 'model_spec.json')) | Should -BeTrue
        $global:PipUsageNugetCalls.Count | Should -Be 1
        ($global:PipUsageNugetCalls[0] -join ' ') | Should -Match 'BuildXL.ML.Models.nuspec'
        Remove-Variable PipUsagePythonCalls -Scope Global
        Remove-Variable PipUsageNugetCalls -Scope Global
    }
}

Describe 'Pip Usage pipeline Python execution' {
    It 'runs real preparation, multi-target training, quality evaluation, and export on one machine' {
        $dataRoot = Join-Path $TestDrive 'real-data'
        $modelDir = Join-Path $TestDrive 'real-model'
        $exportDir = Join-Path $TestDrive 'real-export'
        $qualityReport = Join-Path $TestDrive 'real-quality.json'
        $nugetScript = Join-Path $TestDrive 'real-nuget.ps1'
        Set-Content $nugetScript 'param([Parameter(ValueFromRemainingArguments=$true)]$Arguments); $global:LASTEXITCODE = 0'

        Push-Location (Join-Path $repoRoot 'Private\ML')
        try {
            $tuningRanges = python -c 'import json; from pip_usage.training import TUNING_RANGES; print(json.dumps(TUNING_RANGES))' | ConvertFrom-Json
            $LASTEXITCODE | Should -Be 0
            & $scriptPath `
                -Operation Prepare `
                -WindowStartUtc '2026-01-01T00:00:00Z' `
                -WindowEndUtc '2026-01-02T00:00:00Z' `
                -DatasetName 'real-pipeline-fixture' `
                -DataRoot $dataRoot `
                -SyntheticFixture
            & $scriptPath `
                -Operation Train `
                -DatasetName 'real-pipeline-fixture' `
                -DataRoot $dataRoot `
                -ModelDir $modelDir `
                -ExportArtifactDir $exportDir `
                -QualityReport $qualityReport `
                -TuningTrials 2 `
                -MaxNormalizedMae 2.0 `
                -PackageVersion '0.0.1-test' `
                -NugetExecutable $nugetScript

            foreach ($name in @('model_spec.json', 'pip_usage_cpu.json', 'pip_usage_memory.json', 'pip_usage_average_memory.json', 'pip_usage_duration.json', 'test_cases.json')) {
                Test-Path (Join-Path $modelDir $name) | Should -BeTrue
            }
            Test-Path $qualityReport | Should -BeTrue
            $quality = Get-Content $qualityReport -Raw | ConvertFrom-Json
            $quality.modelKind | Should -Be 'pipUsage'
            $quality.datasetStatistics.discoveredRows | Should -Be 360
            $quality.datasetStatistics.downloadedRows | Should -Be 360
            $quality.datasetStatistics.preparedRows | Should -Be 360
            $quality.datasetStatistics.parserYield | Should -Be 1
            $quality.datasetStatistics.preparationYield | Should -Be 1
            $quality.vocabularyCardinalities.Tool | Should -BeGreaterThan 0
            $quality.vocabularyCardinalities.Codebase | Should -BeGreaterThan 0
            @($quality.artifacts.PSObject.Properties).Count | Should -Be 6
            @($quality.training.PSObject.Properties).Count | Should -Be 4
            foreach ($target in $quality.training.PSObject.Properties) {
                $target.Value.parameters.learning_rate | Should -BeGreaterOrEqual $tuningRanges.learning_rate[0]
                $target.Value.parameters.learning_rate | Should -BeLessOrEqual $tuningRanges.learning_rate[1]
                $target.Value.parameters.num_leaves | Should -BeGreaterOrEqual $tuningRanges.num_leaves[0]
                $target.Value.parameters.num_leaves | Should -BeLessOrEqual $tuningRanges.num_leaves[1]
                $target.Value.parameters.min_child_samples | Should -BeGreaterOrEqual $tuningRanges.min_child_samples[0]
                $target.Value.parameters.min_child_samples | Should -BeLessOrEqual $tuningRanges.min_child_samples[1]
            }
            foreach ($target in @('cpu', 'memory', 'average_memory', 'duration')) {
                $quality.testMae.$target.expected | Should -BeGreaterOrEqual 0
                $quality.testNormalizedMae.$target.expected | Should -BeGreaterOrEqual 0
            }
        } finally {
            Pop-Location
            Get-ChildItem (Join-Path $repoRoot 'Private\ML') -Recurse -Directory -Filter '__pycache__' |
                Remove-Item -Recurse -Force
            Remove-Item (Join-Path $repoRoot 'Private\ML\artifacts') -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

