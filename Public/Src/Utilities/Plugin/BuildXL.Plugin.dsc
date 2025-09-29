// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Plugin {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Plugin",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(false),
            $.dll,
            $.Ipc.dll,
            $.Ipc.Providers.dll,
            $.PluginGrpc.dll,
            Utilities.Core.dll,
            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Text.Json").pkg,
            ]),
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Plugin",
        ],
    });
}
