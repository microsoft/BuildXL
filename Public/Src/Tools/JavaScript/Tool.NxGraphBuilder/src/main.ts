// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as fs from "fs";
import * as path from "path";
import * as BxlConfig from './BuildXLConfigurationReader.js';
import { serializeGraph } from "./GraphSerializer.js";
import { JavaScriptGraph, ScriptCommands, JavaScriptProject } from './BuildGraph.js';

if (process.argv.length < 7) {
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <path-to-nx-lib> <path-to-node> <list-of-targets>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
const repoFolder = process.argv[2];
const outputGraphFile = process.argv[3];
const nxLocation = process.argv[4];
const nodeLocation = process.argv[5];
const targetArgs : string[] = process.argv[6].split(',');

async function loadNxModules() {
    // This is just temporary as we are using private APIS of Nx
    // TODO: Find a better way to do this
    try {
        const createTaskGraphModule = await import(path.join(nxLocation, "src/tasks-runner/create-task-graph.js"));
        const commandLineUtilsModule = await import(path.join(nxLocation, "src/utils/command-line-utils.js"));
        const runManyModule = await import(path.join(nxLocation, "src/command-line/run-many/run-many.js"));
        const projectGraphModule = await import(path.join(nxLocation, "src/project-graph/project-graph.js"));

        return {
            createTaskGraph: createTaskGraphModule.createTaskGraph,
            splitArgsIntoNxArgsAndOverrides: commandLineUtilsModule.splitArgsIntoNxArgsAndOverrides,
            projectsToRun: runManyModule.projectsToRun,
            createProjectGraphAsync: projectGraphModule.createProjectGraphAsync
        };
    } catch (error) {
        throw new Error(`Cannot find nx module under '${nxLocation}'. This module is required to compute the Nx project graph. Details: ${error}`);
    }
}

function createBxlProjectGraph(nxTaskGraph): JavaScriptGraph {
    let projects: JavaScriptProject[] = [];

    let nxExe = path.join(nxLocation, "bin/nx.js");
    if (!fs.existsSync(nxExe)) {
        throw new Error(`Cannot find nx executable under '${nxExe}'. This module is required to compute the Nx project graph.`);
    }

    for (const [currentTask, dependencies] of Object.entries(nxTaskGraph.dependencies)) {
        const task = nxTaskGraph.tasks[currentTask];
        if (!task) {
            throw new Error(`Project not found for task: ${currentTask}`);
        }

        const projectRoot = task.projectRoot;
        if (!projectRoot) {
            throw new Error(`Project root not found for task: ${currentTask}`);
        }

        const target = task.target;
        if (!target) {
            throw new Error(`Target not found for task: ${currentTask}`);
        }

        let commands : ScriptCommands = {}
        commands[target.target] = `${nodeLocation} ${nxExe} run ${currentTask} --skipNxCache --skipRemoteCache --excludeTaskDependencies`;

        let bxlConfig : BxlConfig.BuildXLConfiguration = BxlConfig.getBuildXLConfiguration(repoFolder, projectRoot);

        let p: JavaScriptProject = {
            name: currentTask,
            projectFolder: path.join(repoFolder, projectRoot),
            dependencies: <string[]>dependencies,
            availableScriptCommands: commands,
            // Nx does not designate a temporary folder for each project/verb
            tempFolder: repoFolder,
            outputDirectories: bxlConfig.outputDirectories,
            sourceFiles: bxlConfig.sourceFiles,
            // Not supported by Nx?
            sourceDirectories: [],
            // All Nx projects are cacheable? For now assume yes.
            cacheable: true,
            };

        projects.push(p);
    }

    return {
        projects: projects, 
    };
}

async function createNxTaskGraph(nxModules) {
    const projectGraph = await nxModules.createProjectGraphAsync();
    const extraTargetDependencies = {}; 
    const nxJson = readNxJson();
    const excludeTaskDependencies = false;
    const { nxArgs, overrides } = nxModules.splitArgsIntoNxArgsAndOverrides(
        {
            targets: targetArgs,
            skipRemoteCache: true,
            skipNxCache: true,
            all: true
        },
        'run-many',
        { printWarnings: true },
        nxJson
    );

    const targets = Array.isArray(nxArgs.targets) ? nxArgs.targets
        : nxArgs.targets
            ? [nxArgs.targets]
            : [];

    const configuration = nxArgs.configuration;

    const projects = nxModules.projectsToRun(nxArgs, projectGraph);
    const projectNames = projects.map(p => p.name);

    const taskGraph = nxModules.createTaskGraph(
        projectGraph,
        extraTargetDependencies,
        projectNames,
        targets,
        configuration,
        overrides,
        excludeTaskDependencies
    );

    return taskGraph;
}

async function createGraph() {
    const nxModules = await loadNxModules();
    let nxGraph = await createNxTaskGraph(nxModules);
    return createBxlProjectGraph(nxGraph);
}

function readNxJson() {
    const nxJson = fs.readFileSync(path.join(repoFolder, 'nx.json'), 'utf-8');
    return JSON.parse(nxJson);
} 

// Main execution wrapped in async IIFE
(async () => {
    try {
        let jsGraph = await createGraph();
        serializeGraph(jsGraph, outputGraphFile);
    } catch (error) {
        // Standard error from this tool is exposed directly to the user.
        // Catch any exceptions and just print out the message.
        console.error(error.message);
        process.exit(1);
    }
})();