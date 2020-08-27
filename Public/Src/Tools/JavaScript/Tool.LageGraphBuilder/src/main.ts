import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";

import { serializeGraph } from "./GraphSerializer";
import {JavaScriptGraph, ScriptCommands, JavaScriptProject} from './BuildGraph';

if (process.argv.length < 5) {
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <list-of-targets>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
const repoFolder = process.argv[2];
const outputGraphFile = process.argv[3];
const targets : string = process.argv[4];

/**
 * Result output of `lage info`
 */

export interface Report {
    timestamp: number;
    level: "error" | "warn" | "info" | "verbose" | "silly";
    msg: string;
    data?: InfoData;
  }
  
/**
 * LogStructuredData for the `info` command
 */
export interface InfoData {
    command?: string[];
    scope?: string[];
    packageTasks?: PackageTaskInfo[];
}

/**
 * Only useful for logging purposes for the `info` command
 * Use task-scheduler types for interacting with the pipelines
 */
export interface PackageTaskInfo {
    id: string;
    package: string;
    task: string;
    command: string[];
    workingDirectory: string;
    dependencies: string[];
}

function lageToBuildXL(lage: Report): JavaScriptGraph {
    const projects = lage.data.packageTasks.map(task => {
      let commands : ScriptCommands = {}
      
      // all commands are called build...
      commands["build"] = task.command.join(" ");
  
      let project = {
        name: task.id,
        projectFolder: path.join(repoFolder, task.workingDirectory),
        dependencies: task.dependencies,
        availableScriptCommands: commands,
        tempFolder: repoFolder,
        // $TODO: Allow bxl config values to be threaded through.
        outputDirectories: [],
        sourceFiles: []
      };
  
      return project;
    });
  
    return {
      projects: projects
     };
  }


try {
    // $TODO: WE assume for now that NPM is on the path. We'll likely need to resolve npm in the WorkspaceResolver and pass it as an argument
    // when we start to run on cloudbuild.
    const script  = `npm run lage --silent -- info ${targets} --reporter json`;
    console.log(`Starting lage export: ${script}`);
    const lageOutput = execSync(script).toString();
  
    //Temp workaround since the install-run.js script  in the config prints more stuff than just json
    const lageLines = lageOutput.split(/\r?\n/);
    const lageJson = lageLines[4];
  
    const lageReport = JSON.parse(lageJson) as Report;
    console.log('Finished lage export');

    const graph = lageToBuildXL(lageReport);

    serializeGraph(graph, outputGraphFile);
} catch (Error) {
    // Standard error from this tool is exposed directly to the user.
    // Catch any exceptions and just print out the message.
    console.error(Error.message);
    process.exit(1);
}
