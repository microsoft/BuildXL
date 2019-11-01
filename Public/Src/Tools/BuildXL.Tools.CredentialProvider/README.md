This project should be built in our pipeline to generate the CredentialProviderBuildXL.exe before each internal build. 
The NUGET_CREDENTIALPROVIDERS_PATH should be updated to point to the latest CredentialProviderBuildXL.exe.

Notes for future changes:

Please modify the url in the following command and test your newly generated exe file for your changes:
CredentialProviderBuildXL.exe -uri https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json -verbosity detailed

To build the BuildXL.Tools.CredentialProvider project locally for testing simply run "dotnet build -r win-x64". 
The newly generated CredentialProviderBuildXL.exe file can be found inside BuildXL.Tools.CredentialProvider\bin\Debug\netcoreapp2.1\win-x64

Any error will return a non-zero exit code and will print out some error text.
Valid outputs are printed on the console in json format as expected by nuget.exe 
This program pick's up appropriate PATs from ENV variables and print them as the password field in the json output.
Details about the Nuget Credential Provider implementation requirement can be found here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers