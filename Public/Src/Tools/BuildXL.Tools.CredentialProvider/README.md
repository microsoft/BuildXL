Please use modify the url in the following command and test your newly generated exe file for your changes:
CredentialProviderBuildXL.exe -uri https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json -verbosity detailed

To build the project simply open the .sln file in Visual Studio and Build the Project. 
The newly generated CredentialProviderBuildXL.exe file can be found inside bin/Debug folder.
Any error will return a non-zero exit code and will print out some error text.
Valid outputs are printed on the console in json format. 
This program pick's up appropriate PATs from ENV variables and print them as the password field in the json output.
Details about the Nuget Credential Provider implementation requirement can be found here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers