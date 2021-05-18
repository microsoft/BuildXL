// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd} from "Sdk.Transformers";

namespace Test.Tool.SymbolDaemon {
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.test({
        assemblyName: "Test.Tool.SymbolDaemon",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Tools.SymbolDaemon").exe,
        ]
    });
}
