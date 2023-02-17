// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

@@public
export const dll = BuildXLSdk.library({
    assemblyName: "BuildXL.Pips",
    generateLogs: true,
    sources: globR(d`.`, "*.cs"),
    addCallerArgumentExpressionAttribute: false,
    embeddedResources: [
        {
            resX: f`Filter/ErrorMessages.resx`
        }
    ],
    references: [
        importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
        importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Interop.dll,
        importFrom("BuildXL.Utilities").Ipc.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Utilities").Collections.dll,
        importFrom("BuildXL.Utilities").Configuration.dll,
        importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ...addIf(BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isDotNetCore,
            importFrom("Microsoft.Automata.SRM").withQualifier({ targetFramework: "netstandard2.1" }).pkg,
            importFrom("Microsoft.ApplicationInsights").pkg,
            importFrom("Microsoft.Security.RegularExpressions").pkg,
            importFrom("Microsoft.Security.CredScan.KnowledgeBase").pkg,
            importFrom("Microsoft.Security.CredScan.KnowledgeBase.Client").pkg,
            importFrom("Microsoft.Security.CredScan.KnowledgeBase.Ruleset").pkg
        ),
    ],
    internalsVisibleTo: [
        "BuildXL.Scheduler",
        "Test.BuildXL.EngineTestUtilities",
        "Test.BuildXL.Pips",
        "Test.BuildXL.Scheduler",
        "bxlanalyzer"
    ],
});
