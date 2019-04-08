// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    
    @@public
    export interface RunnerArguments {
        tool?: ToolDefinition;

        /** Arbitrary pip tags. */
        tags?: string[];

        /** 
         * Arbitrary pip description.
         * Pip description does not affect pip cacheability.
         */
        description?: string;
    }
}
