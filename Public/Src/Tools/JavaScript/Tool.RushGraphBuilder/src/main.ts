// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { buildGraph } from "./RushGraphBuilder";
import { serializeGraph } from "./GraphSerializer";

if (process.argv.length < 5) {
    console.log("Expected arguments: <path-to-rush.json> <path-to-output-graph> <path-to-rush-or-rush-lib> <use-build-graph-plugin> [<rushCommand>] [<additionalArguments]");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
let rushJsonFile = process.argv[2];
let outputGraphFile = process.argv[3];
// If useBuildGraphPlugin is true, this parameter is the path to rush. Otherwise, the path to rush-lib
let pathToRushOrRushLibBase = process.argv[4];
let useBuildGraphPlugin = process.argv[5] == "True";
// If the graph plugin is used, command and arguments are passed as well
let rushCommand = useBuildGraphPlugin? process.argv[6] : "";
let rushArguments = useBuildGraphPlugin? process.argv[7] : "";

try {
    let graph = buildGraph(rushJsonFile, pathToRushOrRushLibBase, useBuildGraphPlugin, outputGraphFile, rushCommand, rushArguments);
    serializeGraph(graph, outputGraphFile);
}
catch(Error)
{
    // Standard error from this tool is exposed directly to the user.
    // Catch any exceptions and just print out the message.
    console.error(Error.message);
    process.exit(1);
}