// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>

namespace TransformerMigration {
    /**
     * This type is here so that it can form the base between the Sdk.Transformers and the current prelude types
     * when all are moved to
     */
    export interface IpcMoniker {
        __ipcMonikerBrand: any;
    }
}
/**
 * Artifact kind.
 */
const enum ArtifactKind {
    input = 1,
    output,
    rewritten,
    none,
    vsoHash,
    fileId,
    sharedOpaque,
    directoryId
}

/**
 * Represents an artifact in the build graph.
 */
interface Artifact {
    path: Path | File | Directory | StaticDirectory;
    kind: ArtifactKind;
    original?: File;
}

/**
 * Opaque type representing a moniker used for inter-process communication (IPC).
 *
 * A value of this type should not be created directly; instead, always use Transformer.getNewIpcMoniker().
 */
interface IpcMoniker extends TransformerMigration.IpcMoniker {
}

/**
 * Type of command line argument.
 */
const enum ArgumentKind {
    /** Argument represents a raw text. */
    rawText = 1,
    /** Regular command line option for the tool. */
    regular,
    /** Flag argument that could be present or not in the command line. */
    flag,
    /** Special argument type that expresses potential start of the response file. */
    startUsingResponseFile,
}

/** Union type that represents primitive values for a tool. */
type PrimitiveValue = string | String | number | PathAtom | RelativePath | IpcMoniker | Path;

/**
 * Primitive argument value wrapped with additional type information.
 */
interface PrimitiveArgument {
    value: PrimitiveValue;
    kind: ArgumentKind;
}

/** Union type that can be used as an input value in tool command line arguments. */
type ArgumentValue = PrimitiveValue | Artifact | PrimitiveArgument | CompoundArgumentValue;

/** Special compound value representing multiple values separated by a specified string. */
interface CompoundArgumentValue {
    /** A sequence of values that to be joined with the 'separator' string into a single command line argument value. */
    values: ArgumentValue[];
    /** Separator to join the values with. */
    separator: string;
}

/** Represents command line argument for the tool. */
interface Argument {
    /** Optional name of the command line argument. */
    name?: string;
    /** Value of the argument. */
    value: ArgumentValue | ArgumentValue[];
}

/**
 * The result of executing a process
 */
interface TransformerExecuteResult {
    getOutputFile(output: Path): DerivedFile;
    getOutputFiles(): DerivedFile[];
    getRequiredOutputFiles(): DerivedFile[];
    getOutputDirectory(dir: Directory): OpaqueDirectory;
    getOutputDirectories(): OpaqueDirectory[];
}
