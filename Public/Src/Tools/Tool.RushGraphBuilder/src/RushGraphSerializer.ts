import { RushGraph } from "./RushGraphBuilder";
import fs = require('fs');

/**
 * Serializes a RushGraph into a given output path as a JSON object
 */
export function SerializeGraph(graph: RushGraph, outputPath: string) {
    let data = JSON.stringify(graph, null, 2);
    fs.writeFileSync(outputPath, data);
}