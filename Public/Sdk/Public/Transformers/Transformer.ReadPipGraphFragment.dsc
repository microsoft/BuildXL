// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /**
     * Read in a fragment of the graph as a binary file and all all pip from it to the pip graph.
     * name: Name of this pip graph fragment, used to return a handle to it.
     * file: Binary file to read.
     * dependencyNames: All other pip graph fragments this one depends on.
     * Returns a handle to the fragment just created, which can be used to specify dependencies on other graphs.
     */
    @@public
    export function readPipGraphFragment(name: string, file: SourceFile, dependencyNames: string[]): string {
        return _PreludeAmbientHack_Transformer.readPipGraphFragment(name, file, dependencyNames);
    }
}
