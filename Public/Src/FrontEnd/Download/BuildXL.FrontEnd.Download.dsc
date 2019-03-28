// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Download {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Download",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Net.Http.dll,
                NetFx.System.Web.dll
            ),
            Core.dll,
            Script.dll,
            Sdk.dll,
            TypeScript.Net.dll,

            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("SharpZipLib").pkg,
            ...BuildXLSdk.tplPackages,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.Download",
        ],
    });
}
