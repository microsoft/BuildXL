// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { JavaScriptGraph } from "./BuildGraph.js";
import * as fs from "fs";

/**
 * Serializes a JavaScriptGraph into a given output path as a JSON object
 */
export function serializeGraph(graph: JavaScriptGraph, outputPath: string) {
    let data = JSON.stringify(graph, null, 2);
    fs.writeFileSync(outputPath, data);
}