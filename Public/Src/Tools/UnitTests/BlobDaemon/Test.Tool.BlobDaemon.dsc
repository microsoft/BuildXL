// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd} from "Sdk.Transformers";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Test.Tool.BlobDaemon {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.test({
        assemblyName: "Test.Tool.BlobDaemon",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("Azure.Core").pkg,
            importFrom("Azure.Storage.Blobs").pkg,
            importFrom("Azure.Storage.Common").pkg,
            importFrom("System.Memory.Data").pkg,

            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Tools").ServicePipDaemon.dll,

            importFrom("BuildXL.Tools.BlobDaemon").exe,
        ],
    });
}
