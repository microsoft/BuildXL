// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace App {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "MemoizationStoreApp",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.Config`,
        references: [
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            Interfaces.dll,
            Library.dll,
            BuildXLSdk.isFullFramework 
                ? importFrom("CLAP").pkg
                : importFrom("CLAP-DotNetCore").pkg,
            importFrom("System.Interactive.Async").pkg,
        ],
        tools: {
            csc: {
                codeAnalysisRuleset: f`MemoizationStoreApp.ruleset`,
                additionalFiles: [ BuildXLSdk.cacheRuleSet ],
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            }
        },
    });
}
