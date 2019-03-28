// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

@@public
export const cmdExe: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("ComSpec")}`,
    dependsOnWindowsDirectories: true,
    untrackedDirectoryScopes: [d`${Environment.getPathValue("SystemRoot")}`]
};

@@public
export function range(start: number, count: number, step?: number): number[] {
    step = step || 1;
    let result = [];
    for (let i = start; i < start + count; i += step) {
        result = result.push(i);
    }
    return result;
}
