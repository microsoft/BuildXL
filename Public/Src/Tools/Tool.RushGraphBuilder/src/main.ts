import { RushGraph, buildGraph } from "./RushGraphBuilder";
import { SerializeGraph as serializeGraph } from "./RushGraphSerializer";

if (process.argv.length < 3) {
    console.log("Expected arguments: <path-to-rush.json> <path-to-output-graph>");
    process.exit(1);
}

let rushJsonFile = process.argv[2];
let outputGraphFile = process.argv[3];

try {
    let graph = buildGraph(rushJsonFile);
    serializeGraph(graph, outputGraphFile);
}
catch(Error)
{
    // Standard error from this tool is exposed directly to the user.
    // Catch any exceptions and just print out the message.
    console.error(Error.message);
    process.exit(1);
}