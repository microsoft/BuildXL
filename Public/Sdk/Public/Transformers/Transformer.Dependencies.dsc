// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** The different kind of supported output artifacts. 
     * If a Path or File is passed directly, they are interpreted as required outputs. 
     * If a Directory is passed directly, it is interpreted as an (exclusive) opaque directory 
     * Otherwise, kinds associated to each of these entities are passed explicitly via DirectoryOutput or FileOrPathOutput 
     * */
    @@public
    export type Output = Path | File | Directory | DirectoryOutput | FileOrPathOutput;
    
    @@public
    @@obsolete("Please use 'Output' instead")
    export type OutputArtifact = Path | File | Directory;

    /** Kinds of input artifact that can be argument types for the inputs functions. */
    @@public
    export type Input = File | StaticDirectory;
    
    /** Kinds of input artifact that can be argument types for the inputs functions. */
    @@public
    export type InputArtifact = File | StaticDirectory;

    /** Represents a shared or regular (exclusive) opaque directory */
    @@public
    export interface DirectoryOutput {
        kind: OutputDirectoryKind;
        directory: Directory;
    }
    
     /** An output directory can be shared or exclusive 
     */
    @@public
    export type OutputDirectoryKind = "shared" | "exclusive";
    
    /** Represents a path where the output is to be created, or a file for the case of a rewritten file */
    @@public
    export interface FileOrPathOutput {
        existence: FileExistenceKind;
        artifact: Path | File;
    }

    /** An output file can be required, optional or temporary. */
    @@public
    export type FileExistenceKind = "required" | "optional" | "temporary";
}
