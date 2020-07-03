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
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <path-to-yarn");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
let repoFolder = process.argv[2];
let outputGraphFile = process.argv[3];
let pathToYarn = process.argv[4];

function readPackageJson(location: string): PackageJson {
    return JSON.parse(
        fs.readFileSync(path.join(location, "package.json"), "utf8")
    );
}

try {
    const workspaces = JSON.parse(
        execSync(`"${pathToYarn}" --silent workspaces info --json`).toString()
    ) as YarnWorkspaces;

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
    console.error(Error.message);
    process.exit(1);
}
