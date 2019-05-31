// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsApp {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled || BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.executable({
        assemblyName: "BuildXL.MemoizationStoreVstsApp",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...(BuildXLSdk.isDotNetCoreBuild
                // TODO: This is to get a .Net Core build, but it may not pass tests
                ? [importFrom("CLAP").withQualifier({targetFramework:"net451"}).pkg]
                : [importFrom("CLAP").pkg]
            ),
            Vsts.dll,
            Interfaces.dll,
            ContentStore.Hashing.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            ContentStore.Vsts.dll,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
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
