// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Native from "Sdk.Linux.Native";

namespace VFork {

    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };
    
    const obj = Native.Linux.Compilers.compile({sourceFile: f`vforkSpawn.c`});

    @@public
    export const exe = Native.Linux.Compilers.link({
        objectFiles: [obj],
        tool: Native.Linux.Compilers.gccTool,
        outputName: a`vforkSpawn`
    });
}
