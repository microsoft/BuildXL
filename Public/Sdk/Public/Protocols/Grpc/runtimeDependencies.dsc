// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";

const pkgContents = importFrom("Grpc.Core").Contents.all;

namespace Deployment {
    export declare const qualifier : {targetRuntime: "win-x64" | "osx-x64" | "linux-x64"};
    @@public
    export const runtimeContent: Deployment.Definition = qualifier.targetRuntime === "win-x64"  
        ? {
            contents: [
                {
                    subfolder: r`runtimes/win-x64/native`,
                    contents: [
                        Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/grpc_csharp_ext.x64.dll`),
                    ],
                },
            ]
        }
        : qualifier.targetRuntime === "osx-x64" 
        ? {
            contents: [
                {
                    subfolder: r`runtimes/osx-x64/native`,
                    contents: [
                        Managed.Factory.createBinary(pkgContents, r`runtimes/osx-x64/native/libgrpc_csharp_ext.x64.dylib`),
                    ],
                },
            ]
        }
        : { 
            contents: [
                {
                    subfolder: r`runtimes/linux-x64/native`,
                    contents: [
                        Managed.Factory.createBinary(pkgContents, r`runtimes/linux-x64/native/libgrpc_csharp_ext.x64.so`),
                    ],
                },
            ],
        };
}