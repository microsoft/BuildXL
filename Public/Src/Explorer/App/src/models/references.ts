// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export interface BuildRef {
    sessionId: string,
    kind: "local" | "cloudBuild"
}

export interface PipRef {
    id: number,
    kind: PipKind,
    semiStableHash: string,
    shortDescription?: string
}

export type PipKind = "process" | "copyFile" | "writeFile" | "sealDirectory" | "ipc" | "value" | "specFile" | "module" | "unknownPip";

export interface PipRefWithDetails  extends PipRef {
    module: ModuleRef,
    specFile: SpecFileRef,
    value: ValueRef,
    tool: ToolRef,
    qualifier: QualifierRef,
}

export interface ModuleRef {
    id: number,
    name: string,
}

export interface ValueRef {
    id: number,
    symbol: string,
}

export interface ToolRef {
    id: number,
    name: string,
}

export interface QualifierRef {
    id: number,
    shortName: string,
    longName: string,
}

export interface TagRef {
    name: string,
}

export interface FileRef extends PathRef {
    kind: "file"
}

export interface DirectoryRef extends PathRef{
    kind: "directory"
}

export interface SpecFileRef extends PathRef {
}

export interface PathRef {
    id: number,
    fileName: string,
    filePath: string
}
