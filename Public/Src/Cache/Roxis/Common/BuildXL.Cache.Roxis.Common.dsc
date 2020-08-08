// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace Common {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Roxis.Common",
        sources: [
            ...globR(d`.`,"*.cs"),
        ],
        references: [
            importFrom("RuntimeContracts").pkg,

            importFrom("BuildXL.Utilities").dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            // Needed to implement gRPC service
            Grpc.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
            importFrom("Google.Protobuf").pkg,

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
