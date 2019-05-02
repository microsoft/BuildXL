// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Analyzer {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.Analyzer.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").Analyzer.exe,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            Interfaces.dll,
        ],
    });
}
