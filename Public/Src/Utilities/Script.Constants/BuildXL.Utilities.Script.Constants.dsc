// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Script.Constants {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Script.Constants",
        sources: globR(d`.`, "*.cs"),

    });
}
