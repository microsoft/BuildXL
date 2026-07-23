// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace Test.Tool.ServicePipDaemonTestUtilities {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    // Shared test utilities for ServicePipDaemon-based daemon test projects (e.g. BlobDaemon, DropDaemon).
    @@public
    export const dll = !BuildXLSdk.isDaemonToolingEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "Test.Tool.ServicePipDaemonTestUtilities",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
