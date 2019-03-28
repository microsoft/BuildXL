param (
    [Parameter(Mandatory=$true)][string]$dominoRepoRoot,
    [Parameter(Mandatory=$true)][string]$typeScriptRepoRoot
)

$typeScriptTestCases = "$typeScriptRepoRoot\tests\cases\compiler"
$typeScriptReferences = "$typeScriptRepoRoot\tests\baselines\reference\"
$typeScriptDotNetTests = "$dominoRepoRoot\Public\Src\FrontEnd\TypeScript.Net\TypeScript.Net.UnitTests"

$testCasesWithNoClasses = dir $typeScriptTestCases | where{ ! $_.PSIsContainer} | where {(($_ | get-content) -match "class").Length -eq 0}
Write-Host ("There are " + $testCasesWithNoClasses.Count + " total tests with no classes")

$existingTestCases = dir "$typeScriptDotNetTests\Cases" | % { $_.Name }
$newTestCases = $testCasesWithNoClasses | ? { $existingTestCases -notcontains $_.Name }
Write-Host ("There are " + $newTestCases.Count + " new tests to copy")

$errorFiles = $newTestCases | % { $typeScriptReferences + $_.Name.Replace(".ts", ".errors.txt") }

$failingTestCases = "$typeScriptDotNetTests\FailingCases\"
Write-Host ("Copying new tests to $failingTestCases...")
mkdir $failingTestCases -Force > null
copy-item ($newTestCases | % { $_.FullName }) $failingTestCases
copy-item $errorFiles $failingTestCases -ErrorAction SilentlyContinue
Write-Host ("Copy complete.")