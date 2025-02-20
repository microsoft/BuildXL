// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";

import { serializeGraph } from "./GraphSerializer";
import * as BxlConfig from "./BuildXLConfigurationReader";
import {JavaScriptGraph, JavaScriptProject} from './BuildGraph';
import * as Utilities from './Utilities';

/**
 * A representation of the output of yarn workspaces info --json
 */
interface YarnWorkspaces {
    [key: string]: { location: string; workspaceDependencies: string[] };
}

/**
 * A strip down version of a package.json (scripts)
 */
interface PackageJson {
    scripts?: { [key: string]: string };
}

// For now 'produceErrFile' are optional until Office can update their implementation. See TODO below.
if (process.argv.length < 5 || process.argv.length > 7) {
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <path-to-yarn> <produce-error-file>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
let repoFolder = process.argv[2];
let outputGraphFile = process.argv[3];
let pathToYarn = process.argv[4];
let testJson : string = undefined;
let produceErrFile = false;

// TODO: Remove these conditions once the Office implementation is updated to pass 'false' for
// the 6th argument (produce error file). For now we make this parameter optional, change it later to be mandatory.
if (process.argv.length >= 6) {
    produceErrFile = process.argv[5] === "true";
}
// Unit tests may write a path to a JSON file that can be read here to parse a custom json payload to test older yarn formats.
if (process.argv.length === 7) {
    testJson = fs.readFileSync(process.argv[6], "utf8");
}

function readPackageJson(location: string): PackageJson {
    return JSON.parse(
        fs.readFileSync(path.join(location, "package.json"), "utf8")
    );
}

let errorFd = 0;
try {
    /**
     * New versions of yarn return a workspace dependency tree in the following format:
     * { 
     *     'workspaceName' : { 
     *         location: 'some/location',
     *         workspaceDependencies: [],
     *         mismatchedWorkspaceDependencies: []
     *     } 
     * }
     * 
     * Older versions of yarn return the following format instead where the data key contains json as seen above:
     * {
     *     type: 'log',
     *     data: '{ 'workspaceName' : { location: 'some/location', workspaceDependencies: [], mismatchedWorkspaceDependencies: [] } }' 
     * }
     */
    let workspaceJson;
    if (testJson !== undefined) {
        workspaceJson = JSON.parse(testJson);
    }
    else {
        let stdio;
        if (produceErrFile) {
            errorFd = Utilities.getErrorFileDescriptor(outputGraphFile);
            stdio = {stdio: ["ignore", "ignore", errorFd]};
        }
        else {
            stdio = {stdio: "ignore"};
        }

        // This yarn execution sometimes non-deterministically makes node non-terminating. Debugging this call shows a dangling pipe
        // that seems to be related to stdout/stderr piping to the main process. In order to workaround this issue, output the raw
        // report to the output graph file and immediately read it back for post-processing. The final graph (in the format bxl expects)
        // will be rewritten into the same file
        execSync(`"${pathToYarn}" --silent workspaces info --json > "${outputGraphFile}"`, stdio);

        workspaceJson = JSON.parse(fs.readFileSync(outputGraphFile, "utf8"));
    }

    // Parse the data key if the old format is found.
    if ("type" in workspaceJson && workspaceJson["type"] === "log") {
        workspaceJson = JSON.parse(workspaceJson["data"]);
    }

    const workspaces = workspaceJson as YarnWorkspaces;

    const projects = Object.keys(workspaces).map(
        (workspaceKey): JavaScriptProject => {
            const { location, workspaceDependencies } = workspaces[
                workspaceKey
            ];

            const projectFolder = path.join(repoFolder, location);
            const packageJson = readPackageJson(location);

            let bxlConfig: BxlConfig.BuildXLConfiguration = BxlConfig.getBuildXLConfiguration(
                repoFolder,
                projectFolder
            );

            return {
                name: workspaceKey,
                projectFolder,
                dependencies: workspaceDependencies,
                availableScriptCommands: packageJson.scripts,
                tempFolder: repoFolder,
                outputDirectories: bxlConfig.outputDirectories,
                sourceFiles: bxlConfig.sourceFiles,
                // Yarn nodes are always cacheable
                cacheable: true,
            };
        }
    );

    const graph : JavaScriptGraph = { projects };

    serializeGraph(graph, outputGraphFile);
} catch (Error) {
    // Standard error from this tool is exposed directly to the user.
    // Catch any exceptions and just print out the message.
    console.error(Error.message || Error);
    process.exit(1);
}
finally {
    fs.closeSync(errorFd);
}