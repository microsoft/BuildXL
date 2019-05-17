// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {

    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet451;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...(BuildXLSdk.isDotNetCoreBuild ? [
                // TODO: This is to get a .Net Core build, but it may not pass tests
                importFrom("System.Data.SQLite.Core").withQualifier({targetFramework: "net461"}).pkg,
                importFrom("System.IO.Pipes.AccessControl").pkg,
                importFrom("System.Threading.AccessControl").pkg,
                importFrom("System.Security.AccessControl").pkg,
            ] :
            [
                importFrom("System.Data.SQLite.Core").pkg,
                NetFx.System.Data.dll,
                NetFx.System.Runtime.Serialization.dll,
            ]),

            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Grpc.dll,
            // TODO: This needs to be renamed to just utilities... but it is in a package in public/src
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
            importFrom("System.Interactive.Async").pkg,

            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
        ],
        runtimeContent: [
            importFrom("Sdk.SelfHost.Sqlite").runtimeLibs,
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ],
        allowUnsafeBlocks: true,
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test.LongRunning",
        ],
    });
}
