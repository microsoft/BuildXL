<# The script file is used to validate SBOM in rolling build pipelines.
The script file checks if all the sections in SBOM are present as expected, throws an error when any of the sections are missing or empty.
#>
# In PR validation(Primary Validation Pipeline) the path to the manifest file contains an ADO predefined variable which is passed as an argument in the pipeline.
param (
 $SBOMManifestFile
)
# This condition checks if the file exists or not, throws an error when the manifest file does not exist.
if(-Not(Get-Item -Path $SBOMManifestFile -ErrorAction Ignore))
{
    Write-Error "SBOM package file does not exist"
}
# This block of code checks if all the sections are present as expected.
$ManifestObject = Get-Content -Raw $SBOMManifestFile  | ConvertFrom-Json
$SBOMSections = 'files', 'packages', 'relationships', 'creationInfo', 'spdxVersion', 'dataLicense', 'SPDXID', 'name', 'documentNamespace', 'documentDescribes'
forEach ($section in $SBOMSections)
{  
  # This condition checks if the section is null
  if ($ManifestObject.$section -eq $null)
  {
    Write-Error $section" is not present in SBOM"
    continue
  }
  # The next subsequent conditions are used to check if the sections are empty based on their type
  # This condition is used to check if the sections which are of type Collections are empty or not 
  if ($ManifestObject.$section.GetType().Name -eq "Object[]")
  {
    if (-Not($ManifestObject.$section.count -gt 0))
    {
      Write-Error $section" of type Collection is empty"
    }
    # Adding this condition to ensure that there is atleast one-package depedency present.
     if ($section -eq 'relationships')
     {
       $relationshipTypeArray = $manifestObject.$section | Where-Object {$_.relationshipType -eq 'DEPENDS_ON'}
       if ($relationshipTypeArray -eq $null)
       {
        Write-Error "There should be atleast one package dependency present. SBOM validation failed"
       }
     } 
  } 
  # This condition is used to check if the sections which are of type String are empty or not
  if ($ManifestObject.$section.GetType().Name -eq "String")
  {
    if ($ManifestObject.$section -eq "")
    {
      Write-Error $section" of type String is empty"
    }
  }
  # This condition is used to check if the sections of type PSCustomObject are empty or not
  if ($ManifestObject.$section.GetType().Name -eq "PSCustomObject")
  {
    if ("" -eq $ManifestObject.$section)
    {
      Write-Error $section" of type PSCustomObject is empty"
    }
  }
  
}