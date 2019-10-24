nuget.exe uses any CredentialProvider*.exe files placed in the same directory in alphabetically asscending order for authentication.
More information about Nuget Credential Providers can be found here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers

We have 2 Credentail Provider exe's stored in the Shared/Tools directory. These need to be stored within the same directory as the nuget.exe being used.
1.  CredentialProviderDevLocal.VSS.exe
    CredentialProviderDevLocal.VSS.exe is used for nuget authentication when building BuildXL locally or on any agent with a valid window log-in (alias@microsoft.com)
    It is important to make sure nuget.exe picks up this file after trying CredentialProviderBuildXL.exe so we don't get log-in pop-ups in our pipeline agents. (These could result in the builds waiting for timeout before failing)

2.  CredentialProviderBuildXL.exe
    CredentialProviderBuildXL.exe is a credential provider generated and maintained by the BuildXL team to provide credentials required for building bxl on Azure and other pipelines.
    It picks up appropriate Personal Access Tokens (PAT) and output's the appropriate json value to the console. This json is picked up by nuget.exe for authentication.
    
    This file can be generated using the tool in BuildXL\Public\Src\Tools\BuildXL.Tools.CredentialProvider
    Please refer to the README file inside the BuildXL.Tools.CredentialProvider project for its internal workings and file generation steps.