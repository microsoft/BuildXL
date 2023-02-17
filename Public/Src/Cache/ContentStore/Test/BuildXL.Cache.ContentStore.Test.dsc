// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    @@public
    export const categoriesToRunInParallel = [ "Integration1", "Integration2", "Integration", "Performance" ];

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.ContentStore.Test",
        sources: globR(d`.`,"*.cs"),
        runTestArgs: {
            parallelBucketCount: 8,
        },
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.Netstandard.dll
            ),

            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg
            ),
            // TODO: This needs to be renamed to just utilities... but it is in a package in public/src
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("Google.Protobuf").pkg,
            importFrom("System.Threading.Tasks.Extensions").pkg,
            
            UtilitiesCore.dll,
            Hashing.dll,
            Distributed.dll,
            InterfacesTest.dll,
            Interfaces.dll,
            Library.dll,
            Grpc.dll,
            App.exe, // Tests launch the server, so this needs to be deployed.

            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            ...BuildXLSdk.fluentAssertionsWorkaround,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...BuildXLSdk.bclAsyncPackages,
        ],
        runtimeContent: [
            Library.dll,
            ...getGrpcPackages(true),
        ],
    });
}
