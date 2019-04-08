// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as References from "./References";

export interface PipData {
    /** Separator */
    s: string,

    e: PipDataEncoding,

    i: PipDataEntry[],
}

export type PipDataEncoding = "n" | "c";

export type PipDataEntry = StringPipDataEntry | PathPipDataEntry | NestedPipDataEntry;

export interface StringPipDataEntry {
    s: String
}

export interface PathPipDataEntry {
    p: References.PathRef
}

export interface NestedPipDataEntry {
    n: PipData
}

export function isStringEntry(item: PipDataEntry): item is StringPipDataEntry {
    return item && (<StringPipDataEntry>item).s !== undefined;
}
export function isPathEntry(item: PipDataEntry): item is PathPipDataEntry {
    return item && (<PathPipDataEntry>item).p !== undefined;
}
export function isNestedEntry(item: PipDataEntry): item is NestedPipDataEntry {
    return item && (<NestedPipDataEntry>item).n !== undefined;
}
