// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import {Node, Npm} from "Sdk.NodeJs";

namespace JavaScript.Common {
    export declare const qualifier: {};

    /**
     * A static directory containing common code for JavaScript-based graph builder tools
     */
    @@public 
    export const commonSources : StaticDirectory = Transformer.sealDirectory(d`src`, glob(d`src`, "*.ts"));
}
