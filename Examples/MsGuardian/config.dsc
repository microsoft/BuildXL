// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    modules: [
        d`src`,
        d`sdk`
    ].mapMany(dir => [...globR(dir, "module.config.dsc")]),
    mounts: [
        {
            name: a`Deployment`,
            isReadable: true,
            isWritable: true,
            isScrubbable: true,
            path: p`Out/deployment`,
            trackSourceFileChanges: true
        },
    ],
    resolvers: [
        {
            kind: "Nuget",
            configuration: {
                toolUrl: "https://dist.nuget.org/win-x86-commandline/v4.9.4/NuGet.exe",
                hash: "VSO0:17E8C8C0CDCCA3A6D1EE49836847148C4623ACEA5E6E36E10B691DA7FDC4C39200"
            },
            
            // Microsoft internal only
            repositories: { "Guardian": "https://securitytools.pkgs.visualstudio.com/_packaging/Guardian/nuget/v3/index.json" },
            packages: [
                { id: "Microsoft.Guardian.Cli", version: "0.74.1" },
            ],
        }
    ],
});