# Converts a resx file to a markdown table
param (
    [Parameter(Mandatory=$true)]
    [string]$ResxFile,
    [Parameter(Mandatory=$true)]
    [string]$OutputFile
)

$FilterStrings = @("Banner", "Example", "Explanation", "Filter_");
function Should-Output($Name)
{
    if ($Name.StartsWith("HelpText_DisplayHelp_"))
    {
        foreach ($FilterString in $FilterStrings)
        {
            if ($Name.Contains($FilterString))
            {
                return $false;
            }
        }
        
        return $true;
    }

    return $false
}

$Resx = [xml](Get-Content $ResxFile);
$HelpText = foreach ($Data in $Resx.root.data)
{
    [Tuple]::Create("$($Data.name)", "$($Data.value)")
};

$HelpText = $HelpText | Sort-Object -Property "Item1";

$Output = @();

# Header
$Output += "# BuildXL Flags`n";
$Output += "This page lists flags that can be used to configure BuildXL.`n";

$Output += "| Name | Value |";
$Output += "| ---- | ----- |";

foreach ($Data in $HelpText)
{
    if (Should-Output($Data.Item1))
    {
        $NameStr = $Data.Item1 -replace "HelpText_DisplayHelp_", "";
        $Output += "| $($NameStr) | $($Data.Item2) |";
    }
}

Out-File -FilePath $OutputFile -InputObject $Output -Encoding utf8;