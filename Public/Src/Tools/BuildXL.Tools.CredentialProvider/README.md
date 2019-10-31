## General Information
CredentialProviderBuildXL.exe is used by nuget.exe for authentication. nuget.exe passes a uri to our credential provider and expects a valid json output on the console.
This program pick's up appropriate PATs from ENV variables and print them as the password field in the json output.
Any error will return a non-zero exit code and will print out some error text.

Details about the Nuget Credential Provider implementation requirement can be found here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers

## Usage
This project should be built in our pipeline to generate the CredentialProviderBuildXL.exe before each internal build. 
The NUGET_CREDENTIALPROVIDERS_PATH should be updated to point to the latest CredentialProviderBuildXL.exe.

## Notes for future changes
After making code changes, please test your newly generated CredentialProviderBuildXL.exe
nuget.exe calls the nuget credential provider by passing uri via command line. Sample command:
CredentialProviderBuildXL.exe -uri https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json -verbosity detailed

Sample valid output on the console:
{ "Username":"DoesNotReallyMatterForPATs","Password":"<PAT_HERE>","Message":""}

Sample error output on the console:
Error:  The value of the env var 'CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT' is not set, so the credentials for 'https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json' cannot be retrieved.

Please modify the url in the example above to check if your newly added nuget uri is returning expected json output on the console.

## Building Locally
To build the BuildXL.Tools.CredentialProvider project locally for testing simply run "dotnet build -r win-x64". 
The newly generated CredentialProviderBuildXL.exe file can be found inside BuildXL.Tools.CredentialProvider\bin\Debug\netcoreapp2.1\win-x64