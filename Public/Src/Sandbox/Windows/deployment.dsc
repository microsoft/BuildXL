// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release"};

    const Core64 = Core.withQualifier({platform: "x64", configuration: qualifier.configuration});
    const Core86 = Core.withQualifier({platform: "x86", configuration: qualifier.configuration});

    @@public
    export const definition: SdkDeployment.Definition = {
        contents: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [{
                subfolder: "x64",
                contents: [{
                    contents: [
                        Core64.detoursDll.binaryFile,
                        Core64.detoursDll.debugFile,
                        Core64.nativesDll.binaryFile,
                        Core64.nativesDll.debugFile,
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
            }]),
        ]
    };
}
