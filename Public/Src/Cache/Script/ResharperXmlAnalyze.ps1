param([string]$xmlPath)
$fail = $false

[xml]$report = Get-Content $xmlPath

$issueTypes = @{}
$issueTypeNodes = Select-Xml "//IssueType" $report
$issueTypeNodes | ForEach-Object {
    $issueTypes.Add($_.Node.Id, $_.Node)
}

$issueNodes = Select-Xml "//Issues/Project/Issue" $report
$issueNodes | ForEach-Object {
    
    if ($_.Node.File.Contains("AssemblyAttributes.cs")) {
        continue
    }
    
    $typeId = $_.Node.TypeId
    if ($issueTypes.ContainsKey($typeId)) {
        $issueSeverity = $issueTypes[$typeId].Severity
        $isError = $isWarning = $false
        if ($issueSeverity -like "error") {
            $isError = $true
            $color = "red"
        }
        elseif ($issueSeverity -like "warning") {
            $isWarning = $true
            $color = "yellow"
        }
        
        if ($isError -or $isWarning) {
            $fail = $true
            $msg = "{0} in {1}({2}) [{3}]" -f $typeId, $_.Node.File, $_.Node.Line, $_.Node.Message
            Write-Host $msg -foregroundcolor $color
        }
    }
}

if ($fail) {
    exit 1
}
exit 0