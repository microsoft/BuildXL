// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace App {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "MemoizationStoreApp",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.Config`,
        references: [
            ...(BuildXLSdk.isDotNetCoreBuild
                // TODO: This is to get a .Net Core build, but it may not pass tests
                ? [importFrom("System.Data.SQLite.Core").withQualifier({targetFramework:"net461"}).pkg]
                : [importFrom("System.Data.SQLite.Core").pkg]
            ),
            ...(BuildXLSdk.isDotNetCoreBuild
                // TODO: This is to get a .Net Core build, but it may not pass tests
                ? [importFrom("CLAP").withQualifier({targetFramework:"net451"}).pkg]
                : [importFrom("CLAP").pkg]
            ),
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            Interfaces.dll,
            Library.dll,

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
