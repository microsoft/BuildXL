// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** 
     * Writes lines to a new file; the created write-pip is tagged with 'tags'.
     * If FileContent is an array, an optional separator can be passed that will be used to join the lines. New line is the default separator. 
     **/
    @@public
    export function writeFile(destinationFile: Path, content: FileContent, tags?: string[], separator?: string, description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeFile(destinationFile, content, tags, separator, description);
    }

    /** Writes data to file. */
    @@public
    export function writeData(destinationFile: Path, content: Data, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeData(destinationFile, content, tags, description);
    }

    /** Write all lines. */
    @@public
    export function writeAllLines(destinationFile: Path, contents: Data[], tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllLines(destinationFile, contents, tags, description);
    }

    /** Write all text. */
    @@public
    export function writeAllText(destinationFile: Path, content: string, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllText(destinationFile, content, tags, description);
    }
 
    /** Interface for data. */
    @@public
    export type Data = string | number | Path | PathFragment | CompoundData | Directory;
    
    /** Interface for compound data. */
    @@public
    export interface CompoundData {
        separator?: string;
        contents: Data[];
    }
    
    /** The content of a file that can be written using writeFile. */
    @@public
    export type FileContent = PathFragment | Path | (PathFragment | Path)[];
}
