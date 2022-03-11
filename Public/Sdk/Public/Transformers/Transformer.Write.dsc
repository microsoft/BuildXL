// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** 
     * Writes lines to a new file; the created write-pip is tagged with 'tags'.
     * If FileContent is an array, an optional separator can be passed that will be used to join the lines. New line is the default separator. 
     * This overload will not receive an object overload as this is a legacy option and users are encouraged to mvoe to the other overloads
     **/
    @@public
    export function writeFile(destinationFile: Path, content: FileContent, tags?: string[], separator?: string, description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeFile(destinationFile, content, tags, separator, description);
    }

    /** Writes data to file. */
    @@public
    export function writeData(destinationPathOrArgs: (Path | WriteDataArguments), content?: Data, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeData(destinationPathOrArgs, content, tags, description);
    }

    /** Write all lines. */
    @@public
    export function writeAllLines(destinationPathOrArgs: (Path | WriteAllLinesArguments), contents?: Data[], tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllLines(destinationPathOrArgs, contents, tags, description);
    }

    /** Write all text. */
    @@public
    export function writeAllText(destinationPathOrArgs: (Path | WriteAllTextArguments), content?: string, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllText(destinationPathOrArgs, content, tags, description);
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

    @@public
    export interface WriteDataArguments extends CommonWriteArguments {
        /** The contents to write */
        contents: Data;

        /** Option for writing Paths */
        pathRenderingOption?: PathRenderingOption;
    }

    @@public
    export interface WriteAllLinesArguments extends CommonWriteArguments {
        /** The lines to write */
        lines: Data[];

        /** Option for writing Paths */
        pathRenderingOption?: PathRenderingOption;
    }

    @@public
    export interface WriteAllTextArguments extends CommonWriteArguments {
        /** The text to write */
        text: string;
    }

    @@public
    export interface CommonWriteArguments {
        /** The location to write the new file to */
        outputPath: Path,

        /** Optional set of tags to set on this write file pip. */
        tags?: string[],

        /** Optional custom description for this write file  pip. */
        description?: string,
    }

    /**
     * Indicate how Paths should be rendered when written to Json.
     * - none: No additional transformations are performed, path separator will be based on OS (default).
     * - backSlashes: Always use backs lashes as path separator (not escaped).
     * - escapedBackSlashes: Always use back slashes as path separator with escape characters.
     * - forwardSlashes: Always use forward slashes as path separator.
     * 
     * CODESYNC: Public/Sdk/Public/Json/jsonSdk.dsc
     */
    @@public
    export type PathRenderingOption = "none" | "backSlashes" | "escapedBackSlashes" | "forwardSlashes";
}
