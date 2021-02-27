// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import {Node} from "Sdk.NodeJs";

namespace JavaScript.YarnGraphBuilder {
    export declare const qualifier: {};

    const sources = Transformer.sealDirectory(d`src`, globR(d`src`));
    const output = Node.tscBuild({sources: [sources, Common.commonSources]});

    @@public export const deployment : Deployment.Definition = {
        contents: [{
            subfolder: r`tools/YarnGraphBuilder`,
            contents: [output]
        }]
    };
}
