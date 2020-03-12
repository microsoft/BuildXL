import { RushGraph, buildGraph } from "./RushGraphBuilder";
import { SerializeGraph as serializeGraph } from "./RushGraphSerializer";

if (process.argv.length < 3) {
    console.log("Expected arguments: <path-to-rush.json> <path-to-output-graph> [<debug|release>]");
    process.exit(1);
}

let rushJsonFile = process.argv[2];
let outputGraphFile = process.argv[3];

let isDebug: boolean = false;
if (process.argv.length >=5)
{
    isDebug = process.argv[4] === "debug";
}

let graph = buildGraph(rushJsonFile, isDebug);
serializeGraph(graph, outputGraphFile);