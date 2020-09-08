// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Plugin {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Plugin",
        appConfig: f`App.Config`,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").PluginGrpc.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("System.Memory").pkg,
            importFrom("System.Buffers").pkg,
        ],
        runtimeContent: [
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ]
    });
}
