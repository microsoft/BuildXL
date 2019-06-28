// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Script {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Script",
        rootNamespace: "BuildXL.FrontEnd.Script",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        generateLogsLite: false,
        // After switching to C# 7 features, the style cop fails on the legit cases.
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Xml.dll,
                importFrom("System.Collections.Immutable").pkg,
            ]),

            Sdk.dll,
            TypeScript.Net.dll,

            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            
            // When we can split apmbients in different assemblies we can move the Json ambients into seperate assembly and remove this reference.
            importFrom("Newtonsoft.Json").pkg,

            ...BuildXLSdk.tplPackages,
        ],
        runtimeContent: [
            {
                subfolder: a`Sdk.Prelude`,
                contents: glob(d`${Context.getMount("SdkRoot").path}/Prelude`, "*.dsc")
            }
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.Script.ErrorHandling",
            "Test.BuildXL.FrontEnd.Script",
            "Test.BuildXL.FrontEnd.Core",
        ],
    });
}
