import { buildGraph } from "./RushGraphBuilder";
import { serializeGraph } from "./RushGraphSerializer";

if (process.argv.length < 5) {
    console.log("Expected arguments: <path-to-rush.json> <path-to-output-graph> <path-to-rush-lib>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
let rushJsonFile = process.argv[2];
let outputGraphFile = process.argv[3];
let pathToRushLibBase = process.argv[4];

try {
    let graph = buildGraph(rushJsonFile, pathToRushLibBase);
    serializeGraph(graph, outputGraphFile);
}
catch(Error)
{
    // Standard error from this tool is exposed directly to the user.
    // Catch any exceptions and just print out the message.
    console.error(Error.message);
    process.exit(1);
}