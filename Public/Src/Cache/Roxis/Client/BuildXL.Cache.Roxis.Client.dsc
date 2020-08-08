// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";
import * as Net472 from "Sdk.Managed.Frameworks.Net472";

namespace Client {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Roxis.Client",
        sources: [
            ...globR(d`.`,"*.cs"),
        ],
        references: [
            importFrom("RuntimeContracts").pkg,

            importFrom("BuildXL.Utilities").dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            // Needed to implement gRPC service
            Common.dll,
            Grpc.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
            importFrom("Google.Protobuf").pkg,
            ...addIf(BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0",
                importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg
            ),
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                    NetFx.System.Xml.dll,
                    NetFx.System.Xml.Linq.dll,
                    NetFx.System.Runtime.Serialization.dll,
                    // HACK: Net462 doesn't ship with netstandard dlls, so we fetch them from Net472 instead. This
                    // may not work.
                    Net472.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll
                ]
            ),

            // Needed for serialization/deserialization.
            // TODO: figure out a way to remove this?
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Roxis.Test",
        ],
        skipDocumentationGeneration: true,
        nullable: true,
    });
}
