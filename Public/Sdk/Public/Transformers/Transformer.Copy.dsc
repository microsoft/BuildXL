// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /** Copies a file to a new destination; the created copy-pip is tagged with 'tags'. */
    @@public
    export function copyFile(sourceFile: File, destinationFile: Path, tags?: string[], description?: string, keepOutputsWritable?: boolean): DerivedFile {
        return _PreludeAmbientHack_Transformer.copyFile(sourceFile, destinationFile, tags, description, keepOutputsWritable);
    }

}
