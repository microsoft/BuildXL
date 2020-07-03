import { JavaScriptGraph } from "./BuildGraph";
import fs = require('fs');

/**
 * Serializes a JavaScriptGraph into a given output path as a JSON object
 */
export function serializeGraph(graph: JavaScriptGraph, outputPath: string) {
    let data = JSON.stringify(graph, null, 2);
    fs.writeFileSync(outputPath, data);
}