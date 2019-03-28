// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";

namespace Deployment{


export declare const qualifier: BuildXLSdk.DefaultQualifier;

@@public
export const deployment: Deployment.Definition = {
    contents: BuildXLSdk.isDotNetCoreBuild ?
    [
        BasicFilesystem.dll,
        BuildCacheAdapter.dll,
        InMemory.dll,
        Interfaces.dll,
        ImplementationSupport.dll,
        MemoizationStoreAdapter.dll,
        VerticalAggregator.dll,
    ]:[
        Analyzer.exe,
        BasicFilesystem.dll,
        BuildCacheAdapter.dll,
        Compositing.dll,
        InMemory.dll,
        InputListFilter.dll,
        Interfaces.dll,
        ImplementationSupport.dll,
        MemoizationStoreAdapter.dll,
        VerticalAggregator.dll,
    ]
};

}
