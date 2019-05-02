// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";

namespace Deployment {
    @@public
    export const deployment: Deployment.Definition = {
        contents: [
            InMemory.dll,
            Interfaces.dll,
            BasicFilesystem.dll,
            BuildCacheAdapter.dll,
            ImplementationSupport.dll,
            MemoizationStoreAdapter.dll,
            VerticalAggregator.dll,
        ...addIf(!BuildXLSdk.isDotNetCoreBuild,
                Analyzer.exe,
                Compositing.dll,
                InputListFilter.dll
            )
        ]
    };
}
