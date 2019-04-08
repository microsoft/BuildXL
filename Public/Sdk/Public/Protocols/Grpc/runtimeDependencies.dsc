// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";

const pkgContents = importFrom("Grpc.Core").Contents.all;


@@public
export const runtimeContent: Deployment.Definition = {
    contents: [
        {
            subfolder: r`runtimes/win/native`,
            contents: [
                Managed.Factory.createBinary(pkgContents, r`runtimes/win/native/grpc_csharp_ext.x64.dll`),
                Managed.Factory.createBinary(pkgContents, r`runtimes/win/native/grpc_csharp_ext.x86.dll`),
            ],
        },
        {
            subfolder: r`runtimes/osx/native`,
            contents: [
                Managed.Factory.createBinary(pkgContents, r`runtimes/osx/native/libgrpc_csharp_ext.x64.dylib`),
                Managed.Factory.createBinary(pkgContents, r`runtimes/osx/native/libgrpc_csharp_ext.x86.dylib`),
                    ],
        },
        {
            subfolder: r`runtimes/linux/native`,
            contents: [
                Managed.Factory.createBinary(pkgContents, r`runtimes/linux/native/libgrpc_csharp_ext.x64.so`),
                Managed.Factory.createBinary(pkgContents, r`runtimes/linux/native/libgrpc_csharp_ext.x86.so`),
            ],
        },
    ],
};
