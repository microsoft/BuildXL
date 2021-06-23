// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Artifact {
    /** Creates an input artifact from file or directory. */
    @@public
    export function input(value: Transformer.InputArtifact): Artifact {
        return createArtifact(value, ArtifactKind.input);
    }

    /** Creates a list of input artifacts from a list of files and directories. */
    @@public
    export function inputs(values: Transformer.InputArtifact[]): Artifact[] {
        return (values || []).mapDefined(input);
    }

    /** Creates an output artifact from a file. */
    @@public
    export function output(value: Transformer.OutputArtifact): Artifact {
        return createArtifact(value, ArtifactKind.output);
    }

    /** 
     * Creates a shared opaque directory from a directory. 
     * */
    @@public
    export function sharedOpaqueOutput(value: Directory): Artifact {
        return createArtifact(value, ArtifactKind.sharedOpaque);
    }

    /** Creates a list of output artifacts from a list of files.  */
    @@public
    export function outputs(values: Transformer.OutputArtifact[]): Artifact[] {
        return (values || []).mapDefined(output);
    }

    /**
     * Creates a rewritten artifact from a file.
     * If an output path is specified (not undefined), then the (original) to-be-rewritten file will be copied first to the specified output path.
     * The result of copying is then used as a dependency of the transformer that consumes this artifact.
     */
    @@public
    export function rewritten(originalInput: File, outputPath?: Path): Artifact {
        return outputPath !== undefined 
            ? createArtifact(outputPath, ArtifactKind.rewritten, originalInput)
            : createArtifact(originalInput, ArtifactKind.rewritten);
    }

    /** Creates an artifact from a file or a directory, but marks it as neither an input nor an output. */
    @@public
    export function none(value: Transformer.InputArtifact | Transformer.OutputArtifact | Directory): Artifact {
        if (value === undefined) return undefined;

        return createArtifact(value.path, ArtifactKind.none);
    }

    /** Creates an input artifact from file or directory. */
    @@public
    export function vsoHash(value: File): Artifact {
        return createArtifact(value, ArtifactKind.vsoHash);
    }

    /** Creates an input artifact from file or directory. */
    @@public
    export function fileId(value: File): Artifact {
        return createArtifact(value, ArtifactKind.fileId);
    }

    /** Creates an input artifact from file or directory. */
    @@public
    export function directoryId(value: StaticDirectory): Artifact {
        return createArtifact(value, ArtifactKind.directoryId);
    }

    function createArtifact(value: Transformer.InputArtifact | Transformer.OutputArtifact, kind: ArtifactKind, original?: File): Artifact {
        if (value === undefined) return undefined;

        return <Artifact>{
            path: value,
            kind: kind,
            original: original
        };
    }
}
