# Automatically adds AAD tokens to BatmonWeb requests. In case of a request pointing to the old batmon Url,
# this script will automatically translate the Url to the new ones.
function Invoke-CBWebRequest
{
    param 
    (
        [Parameter(Position=0, Mandatory=$True)]
        [Uri]$Uri,
        
        [ValidateNotNullOrEmpty()]
        [ValidateSet("Get","Post")]
        $Method = "Post",
        
        [String]
        $Body = $null,
        
        [String]
        $ContentType = "application/json"
    )
    
    # Pin MSAL.PS to version 4.5.1.1. Newer version gives warning:
    #     WARNING: The specified module 'MSAL.PS' with PowerShellGetFormatVersion '2.0' is not supported by the current version
    #     of PowerShellGet. Get the latest version of the PowerShellGet module to install this module, 'MSAL.PS'.
    $msalModule = Get-Module -ListAvailable -Name 'MSAL.PS' | Where-Object {$_.Version.Major -eq 4}
    if (($null -eq $msalModule))
    {
        Install-Module -Name 'MSAL.PS' -Scope CurrentUser -RequiredVersion  4.5.1.1 -Force
    }
    Import-Module -Name 'MSAL.PS' -RequiredVersion  4.5.1.1 -Force
    
    if($Uri -imatch "cbtest" -and $Uri -inotmatch "microsoft.com")
    {
        $Uri = $Uri -replace "cbtest", "cbtest.microsoft.com"
    }
    elseif($Uri -imatch "qci" -and $Uri -inotmatch "microsoft.com")
    {   
        $regex = [regex]"qci[0-9][0-9]"
        $match = $regex.Match($Uri)
        $environment = $match.Captures[0].Value
        
        $Uri = $Uri -replace $environment, ($environment + ".cbci.microsoft.com")
    }
    
    elseif($Uri -inotmatch "microsoft.com")
    {
        $Uri = $Uri -replace "https://b", "https://cloudbuild.microsoft.com"
    }
        
    # CloudBuildPublicClientDRIScopes
    $clientId = "a5a5dbed-8a88-40d9-94f3-f62bad35ad07"
    $redirectUri = "msala5a5dbed-8a88-40d9-94f3-f62bad35ad07://auth"
    
    $scope = ""
    
    if ($Uri -imatch 'https://cloudbuild' -or $Uri -imatch "https://b") 
    {
        $scope = "https://cloudbuild.microsoft.com/.default"
    }
    elseif ($Uri -imatch 'cbtest') 
    {
        $scope = "https://cbtest.microsoft.com/.default"
    }
    elseif ($Uri -imatch 'qci') 
    { 
        # CI Environments
        $scope = "https://cbci.microsoft.com/.default"
    }
    else 
    {
        # fall back
        $scope = "https://cloudbuild.microsoft.com/.default"
    }
    
    $authResult = (Get-MsalToken -TenantId microsoft.com -ClientId $clientId -RedirectUri $redirectUri -Scopes $scope)
    $token = $authResult.AccessToken
    
    $params = 
    @{
        "Uri" = $URI
        "UseBasicParsing" = $true
        "Method" = $Method
        "Headers" = @{"Authorization" = "Bearer $token"}
    }
    
    if($Method -ieq "POST")
    {
        $params["Body"] = $Body
        $params["ContentType"] = $ContentType
    }
    
    Write-Host "Sending $Method request to $Uri..."
    
    return Invoke-WebRequest @params
}