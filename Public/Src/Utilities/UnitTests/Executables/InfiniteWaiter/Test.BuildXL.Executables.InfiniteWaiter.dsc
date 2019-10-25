// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InfiniteWaiter {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Test.BuildXL.Executables.InfiniteWaiter",
        sources: globR(d`.`, "*.cs")
    });
}
