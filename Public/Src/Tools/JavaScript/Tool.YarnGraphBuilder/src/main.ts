import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";

import { serializeGraph } from "./GraphSerializer";
import * as BxlConfig from "./BuildXLConfigurationReader";
import {JavaScriptGraph, JavaScriptProject} from './BuildGraph';

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

if (process.argv.length < 5) {
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <path-to-yarn>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
let repoFolder = process.argv[2];
let outputGraphFile = process.argv[3];
let pathToYarn = process.argv[4];
let testJson : string = undefined;

// Unit tests may write a path to a JSON file that can be read here to parse a custom json payload to test older yarn formats.
if (process.argv.length === 6) {
    testJson = fs.readFileSync(process.argv[5], "utf8");
}

function readPackageJson(location: string): PackageJson {
    return JSON.parse(
        fs.readFileSync(path.join(location, "package.json"), "utf8")
    );
}

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
        // This yarn execution sometimes non-deterministically makes node non-terminating. Debugging this call shows a dangling pipe
        // that seems to be related to stdout/stderr piping to the main process. In order to workaround this issue, output the raw
        // report to the output graph file and immediately read it back for post-processing. The final graph (in the format bxl expects)
        // will be rewritten into the same file
        execSync(`"${pathToYarn}" --silent workspaces info --json > "${outputGraphFile}"`, {stdio: "ignore"});

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