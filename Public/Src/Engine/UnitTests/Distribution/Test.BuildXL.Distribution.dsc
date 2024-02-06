// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Distribution {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;
    
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Distribution",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").Distribution.Grpc.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            Managed.Factory.filterRuntimeSpecificBinaries(BuildXLSdk.WebFramework.getFrameworkPackage(), [
                importFrom("System.IO.Pipelines").pkg
            ]),
        ],
    });
}
