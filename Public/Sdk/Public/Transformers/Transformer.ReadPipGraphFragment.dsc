// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /**
     * Read in a fragment of the graph as a binary file and add all pips.
     * name: Name of this pip graph fragment, used to return a handle to it.
     * file: Binary file to read.
     * dependencyFragments: All other pip graph fragments this one depends on.
     * description: String to print to console while fragments are loading.
     * Returns a handle to the fragment just created, which can be used to specify dependencies on other graphs.
     */
    @@public
    export function readPipGraphFragment(file: SourceFile, dependencyFragments: FragmentHandle[], description?: string): FragmentHandle {
        return _PreludeAmbientHack_Transformer.readPipGraphFragment(file, dependencyFragments, description);
    }

    @@public
    export interface FragmentHandle {}
}
