// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release", targetRuntime: "win-x64"};

    const Core64 = Core.withQualifier({platform: "x64", configuration: qualifier.configuration});
    const Core86 = Core.withQualifier({platform: "x86", configuration: qualifier.configuration});

    @@public
    export const detours: SdkDeployment.Definition = {
        contents: [
            {
                subfolder: "x64",
                contents: [{
                    contents: [
                        Core64.detoursDll.binaryFile,
                        Core64.detoursDll.debugFile,
                    ]
                }]
            },
            {
                subfolder: "x86",
                contents: [{
                    contents: [
                        Core86.detoursDll.binaryFile,
                        Core86.detoursDll.debugFile,
                        // BuildXL is only x64 process. No x86.
                    ]
                }],
            },
        ]
    };

    @@public
    export const natives: SdkDeployment.Definition = {
        contents: [
            {
                subfolder: "x64",
                contents: [{
                    contents: [
                        Core64.nativesDll.binaryFile,
                        Core64.nativesDll.debugFile,
                    ]
                }]
            },
        ]
    };
}
