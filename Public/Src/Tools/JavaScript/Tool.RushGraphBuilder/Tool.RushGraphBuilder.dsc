// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import {Node} from "Sdk.NodeJs";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace JavaScript.RushGraphBuilder {
    export declare const qualifier: BuildXLSdk.AllSupportedQualifiersWithoutMacOS;

    const rushToolSrc = Transformer.sealDirectory(d`src`, globR(d`src`));
    const output = Node.tscBuild({
        sources: [rushToolSrc, Common.commonSources],
        productionPackageJson: f`src/package.json`,
    });

    @@public export const deployment : Deployment.Definition = {
        contents: [{
            subfolder: r`tools/RushGraphBuilder`,
            contents: [output]
        }]
    };
}
