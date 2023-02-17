// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Ipc.Providers {
   export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Ipc.Providers",
        sources: globR(d`.`, "*.cs"),
        references: [
            Ipc.dll,
            Ipc.Grpc.dll,
            $.dll,
            $.Storage.dll,
            Utilities.Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            ...BuildXLSdk.systemMemoryDeployment,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcAspNetCorePackages(),
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Ipc",
        ],

        runtimeContentToSkip : [
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg,
        ],
    });
}
