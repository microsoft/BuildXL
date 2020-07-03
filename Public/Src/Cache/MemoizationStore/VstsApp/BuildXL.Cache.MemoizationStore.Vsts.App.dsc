// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsApp {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled || BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.executable({
        assemblyName: "BuildXL.MemoizationStoreVstsApp",
        sources: globR(d`.`,"*.cs"),
        references: [
            Vsts.dll,
            Interfaces.dll,
            ContentStore.Hashing.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            ContentStore.Vsts.dll,
            
            BuildXLSdk.isFullFramework 
                ? importFrom("CLAP").pkg
                : importFrom("CLAP-DotNetCore").pkg,

            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
        ],
        appConfig: f`App.config`,
        tools: {
            csc: {
                codeAnalysisRuleset: f`MemoizationStoreVstsApp.ruleset`,
                additionalFiles: [ BuildXLSdk.cacheRuleSet ],
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            }
        },
    });
}
