// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({    
    modules: globR(d`src`, "package.config.dsc"),

    resolvers: [
        <SourceResolver>{
            kind: "DScript",
            root: d`../Sdk`,
        },
        <SourceResolver>{
            kind: "DScript",
            root: d`${Environment.getPathValue("BUILDXL_BIN_DIRECTORY")}/Sdk`,
        },
        {
            kind: "Nuget",
            
            // This configuration pins people to a specific version of nuget
            // The credential provider should be set by defining the env variable NUGET_CREDENTIALPROVIDERS_PATH.  TODO: It can be alternatively pinned here,
            // but when it fails to download (the cloudbuild case, since it actually comes from a share) the build is aborted. Consider making the failure non-blocking. 
            configuration: {
                toolUrl: "https://dist.nuget.org/win-x86-commandline/v3.4.4/NuGet.exe",
                hash: "EFA4B6713CD09EB8A7DB91322F4DB213C58E8F036E2FA517819998F49C465BF100",
            },
                        
            repositories: {
                "selfhost": "https://dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json",
            },

            packages: [
                { id: "Microsoft.Net.Compilers", version: "2.3.2" },
                { id: "VsTest.Console", version: "11.0.60315.1", alias: "VsTest.Console.Nuget" },
            ]
        }
    ],
});
