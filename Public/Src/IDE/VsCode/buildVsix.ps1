[CmdletBinding(PositionalBinding=$false)]
param(
    [ValidateSet("Release", "Debug")]
    [string]$configuration = "Release"
)

$scriptRoot = Split-Path -Path $MyInvocation.MyCommand.Path
$repoRoot = Join-Path $scriptRoot "..\..\..\..\";
$bxl = Join-Path $repoRoot "bxl.cmd";
$pluginSource = Join-Path $repoRoot "Out\Bin" | Join-Path -ChildPath ($configuration)
$pluginDest = Join-Path $scriptRoot "client" | Join-Path -ChildPath "bin"
$vscodeClient = Join-Path $scriptRoot "client"

echo "Building latest language service"

cmd /c "$bxl -deployConfig $configuration /f:spec='Public\Src\IDE\LanguageServer\BuildXL.Ide.Script.LanguageServer.dsc' /scrub" 

if ($LastExitCode -ne 0) {
        throw "Failed to build the latest plugin";
    }

echo "Copying artifacts to plugin location ($pluginSource -> $pluginDest)"

robocopy $pluginSource $pluginDest /E /V
if (($LastExitCode -ne 0) -and ($LastExitCode -ne 1)) {
        throw "Failed to copy artifacts to the plugin folder (robocopy error: $LastExitCode)";
    }

echo "Generating VSix..."

cd $vscodeClient

npm install
vsce package --baseContentUrl http://aka.ms/BuildXL

if ($LastExitCode -ne 0) {
        throw "Failed to generate Vsix package";
    }