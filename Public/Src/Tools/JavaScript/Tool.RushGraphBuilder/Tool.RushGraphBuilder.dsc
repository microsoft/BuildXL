// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import {Node, Npm} from "Sdk.NodeJs";

namespace JavaScript.RushGraphBuilder {
    export declare const qualifier: {};

    const rushToolSrc = Transformer.sealDirectory(d`src`, globR(d`src`));
    const output = Node.tscBuild({sources: [rushToolSrc, Common.commonSources]});

    @@public export const deployment : Deployment.Definition = {
        contents: [{
            subfolder: r`tools/RushGraphBuilder`,
            contents: [output]
        }]
    };
}
