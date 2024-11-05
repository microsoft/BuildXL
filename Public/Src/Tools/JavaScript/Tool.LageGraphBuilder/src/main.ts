// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import * as BxlConfig from './BuildXLConfigurationReader';

import { serializeGraph } from "./GraphSerializer";
import {JavaScriptGraph, ScriptCommands, JavaScriptProject} from './BuildGraph';
import * as Utilities from './Utilities';

// String value "undefined" can be passed in for the 6th argument. Office implementation will use this to pass the lage location.
// For now 'since' and 'produceErrFile' are optional until Office can update their implementation. See TODO below.
if (process.argv.length < 7 || process.argv.length > 9) {
    console.log("Expected arguments: <repo-folder> <path-to-output-graph> <npm Location> <list-of-targets> <Lage Location> <since> <produce-error-file>");
    process.exit(1);
}

// argv[0] is 'node', argv[1] is 'main.js'
const repoFolder = process.argv[2];
const outputGraphFile = process.argv[3];
const npmLocation = process.argv[4];
const targets : string = process.argv[5];
const lageLocation = process.argv[6] === "undefined" ? undefined : process.argv[6];
let since = "";
let produceErrFile = false;

// TODO: Remove these conditions once the Office implementation is updated to pass 'undefined ' for the 7th argument (since) and 'false' for
// the 8th argument (produce error file). For now we make this parameter optional, change it later to be mandatory.
if (process.argv.length >= 8) {
  since = process.argv[7] === "undefined" ? "" : `--since '${process.argv[7]}' `;
}
if (process.argv.length == 9) {
  produceErrFile = process.argv[8] === "true";
}

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
      
      commands[task.task] = task.command.join(" ");
  
      let projectFolder = path.join(repoFolder, task.workingDirectory);

      let bxlConfig : BxlConfig.BuildXLConfiguration = BxlConfig.getBuildXLConfiguration(repoFolder, projectFolder);

      let project = {
        name: task.id,
        projectFolder: projectFolder,
        dependencies: task === undefined ? [] : task.dependencies,
        availableScriptCommands: commands,
        tempFolder: repoFolder,
        outputDirectories: bxlConfig.outputDirectories,
        sourceFiles: bxlConfig.sourceFiles
      };
  
      return project;
    });
  
    return {
      projects: projects
     };
  }


  let errorFd = 0;
  try {
    let script  = lageLocation === undefined ? `"${npmLocation}" run lage --silent --` : `"${lageLocation}"`;
    script  = `${script} info ${targets} --reporter json ${since}> "${outputGraphFile}"`;
    console.log(`Starting lage export: ${script}`);

    // The graph sometimes is big enough to exceed the default stdio buffer (200kb). In order to workaround this issue, output the raw
    // report to the output graph file and immediately read it back for post-processing. The final graph (in the format bxl expects)
    // will be rewritten into the same file
    let stdio;
    if (produceErrFile) {
      errorFd = Utilities.getErrorFileDescriptor(outputGraphFile);
      stdio = {stdio: ["ignore", "ignore", errorFd]};
    }
    else {
      stdio = {stdio: "ignore"};
    }

    execSync(script, stdio);
 
    const lageJson = fs.readFileSync(outputGraphFile, "utf8");

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
finally {
    fs.closeSync(errorFd);
}