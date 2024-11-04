param (
    [string]$packageVersion,
    [string]$outputDirectory,
    [string]$basePackageName
)

if (!(Test-Path -Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force
}

# Define the path for the .nuspec file
$nuspecPath = Join-Path -Path $outputDirectory -ChildPath "$basePackageName.nuspec"

# Write the .nuspec content
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd">
  <metadata minClientVersion="2.12">
    <id>$basePackageName</id>
    <version>$packageVersion</version>
    <authors>Microsoft</authors>
    <owners>microsoft,buildxl,bxl</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>The BuildXL AppHostPatcher is used to for .NET Core self-contained deployment generation.</description>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <serviceable>true</serviceable>
  </metadata>
</package>
"@

# Output the content to the .nuspec file
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

Write-Host "Created .nuspec file at $nuspecPath with version $packageVersion"
