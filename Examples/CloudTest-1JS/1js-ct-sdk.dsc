// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This file contains utility functions for working with the CloudTest scopes file and scheduling CloudTest
// jobs based on it. The main purpose of this file is to provide an example of how to correlate the test commands
// defined in the scopes file with the pips in the build graph (e.g., by matching the package and verb with the exported 
// verb patterns from the Lage resolver) and then schedule CloudTest jobs with the appropriate inputs from the build graph for each command in the scopes file.

import * as Json from "Sdk.Json";
import * as CloudTestClient from "Sdk.CloudTestClient";
import * as Graph from "Sdk.Graph";

// Structures modeling scopes.json structure. See Examples\CloudTest-1JS\scopes.json

/**
 * Represents a single test command entry within a package scope.
 * When npm-run-all expansion occurs, multiple commands may originate from a single verb.
 */
@@public
export interface TestCommand {
    /** The concrete command name (e.g., "test:e2e" or "component:shard-1"). */
    command: string;

    /** The original lage verb that produced this command (tracks npm-run-all expansion provenance). */
    provenanceVerb: string;

    /** The shell command to execute for this test job. */
    innerCommand: string;

    /** Content hash used for caching/backfill. */
    hash: string;

    /** Owner alias for this test command. */
    owner?: string;

    /** CloudTest profile file name determining platform, SKU, and image. */
    testProfile?: string;

    /** Shell command to discover test cases without executing them. */
    discoveryCommand?: string;
}

/**
 * Represents a package and its associated test commands in the scope file.
 */
@@public
export interface PackageScope {
    /** The package name as declared in package.json (e.g., "@test/b-package"). */
    packageName: string;

    /** The list of test commands to run for this package. */
    commandList: TestCommand[];
}

/**
 * Root structure of the scopes.json file produced by the determine-test-scopes command.
 * Lists packages in scope and the CloudTest jobs to schedule for each.
 */
@@public
export interface ScopeFile {
    /** The list of packages with their test commands. */
    packageList: PackageScope[];
}

// Utility functions

/**
 * Utility functions for working with the CloudTest scopes file, which define the test commands to be scheduled as CloudTest jobs and their associated metadata.
 */
@@public
export function readScopeFile(scopeFile: SourceFile) : ScopeFile {
    let content = File.readAllText(scopeFile);
    return Json.read<ScopeFile>(content);
}

/**
 * Given a ScopeFile, returns a list of placeholders in the format "packageName_command" that represent the test commands to be scheduled as CloudTest jobs. These placeholders can be used to correlate the commands in the scopes file with the corresponding pips in the build graph (e.g., by matching them with exported verb patterns from the Lage resolver).
 */
@@public
export function getJobPlaceHolders(scopeFile: ScopeFile) : string[] {
    let placeholders: MutableSet<string> = MutableSet.empty<string>();
    
    for (const packageScope of scopeFile.packageList) {
        for (const testCommand of packageScope.commandList) {
            placeholders.add(getTestJobName(packageScope.packageName, testCommand.command));
        }
    }

    return placeholders.toArray();
}

/**
 * Given a package name, verb, and the scopes file, returns the list of TestCommand entries from the scopes file that match the given package and verb. 
 * This can be used to retrieve the metadata for the test commands associated with a particular pip in the build graph (e.g., by matching the package and verb with the exported verb patterns from the Lage resolver).
 * NOTE: the current implementation fails whenever the given package and verb combination does not exist in the scopes file, but it can be easily modified to return an empty list instead. Relaxing this behavior would allow
 * for a more liberal filter passed to BuildXL (that may result in more ct-job pips being scheduled) but the ultimate set of CT jobs submitted will be driven by scopes.json.
 */
@@public
export function getCommandList(package: string, verb: string, scopeFile: ScopeFile) : TestCommand[] {
    let packageScope = scopeFile.packageList.find(p => p.packageName === package);
    if (!packageScope) {
        Contract.fail(`Package ${package} not found in scope file ${scopeFile}`);
    }

    let commands = packageScope.commandList.filter(c => c.provenanceVerb === verb);
    if (commands.length === 0) {
        Contract.fail(`Command ${verb} not found for package ${package} in scope file ${scopeFile}`);
    }

    return commands;
}

/**
 * Given the scopes file and the map of CT verbs to their corresponding pips in the build graph, submits a CloudTest job for each command in the scopes file with the corresponding pip outputs 
 * as inputs to the job. Returns a list of submitted job results.
 */
@@public
export function submitCloudTestJobs(scopeFile: ScopeFile, ctVerbs: Map<JavaScriptProjectIdentifier, TransformerExecuteResult>, sessionCreateResult : CloudTestClient.Helpers.ConfigAndSessionResult) {
    
    const jobs = ctVerbs.toArray().map((kvp) => {
        const project : JavaScriptProjectIdentifier = kvp[0];
        const executeResult : TransformerExecuteResult = kvp[1];

        // The inputs for all the CT jobs scheduled from this pip will have the transitive closure of the pip's inputs as their inputs.
        // (we exclude sources from the inputs because usually we just need the build outputs)
        // Having the full closure may be an overkill in some cases. The current approach is just an easy generalization. Other approaches could involve
        // more fine-grained selection of inputs based on the specific needs of each test command.
        // The cast below is needed because getDependencyClosure can also include static directories that are not outputs (e.g. source seal directories), but by excluding sources we ensure we only 
        // get opaque directories and files. On the other hand, the job submission only accepts files and opaque directories as inputs, since the hash generation logic (for now) only works for those.
        const inputs : (File | OpaqueDirectory)[] = <(File | OpaqueDirectory)[]> Graph.getDependencyClosure(executeResult, {excludeSources: true});

        // Get all the test commands that belong to this package + verb
        const commands = getCommandList(project.packageName, project.command, scopeFile);

        // Schedule a CT job for each test command.
        const jobs = commands.map(command => {
            const submitJobArgs : CloudTestClient.Helpers.SubmitJobArguments = {
                jobName: getTestJobName(project.packageName, command.command),
                // This is just a mock job that produces a TAP file with one successful test.
                jobExecutable: p`/bin/bash`,
                // Just flush the inner command to stdout, so it is clear in the job logs which command is being executed for each job, and then produce a TAP file with one successful test.
                jobArguments: `-c "echo '${command.innerCommand}'; { echo 'TAP version 13'; echo '1..1'; echo 'ok 1 - test'; } > $LoggingDirectory/result.tap"`,
                testExecutionType: "Exe",
                testParserType: "TAP",
                jobInputs: inputs,
                configAndSessionResult: sessionCreateResult,
            };

            return CloudTestClient.Helpers.submitJob(submitJobArgs);
        });

        // Wait for the cloudtest session to complete. For now we wait for this as part of the build.
        // In the future we will have an agent-less task that just waits for the session completion and reports the result, so we don't have to use an agent for this if the CT session is the last
        // thing that happens on a pipeline
        const sessionResult = CloudTestClient.Helpers.waitForCompletion({
            configAndSessionResult: sessionCreateResult,
            // This is optional, but by passing them, the pip that polls for the session completion will only start after all the provided jobs are submitted.
            submittedJobs: jobs,
            timeoutMinutes: 10
        });

        return jobs;
    });
}

// We need to give a name to each test job. Use the package name + verb for that
function getTestJobName(project: string, command: string): string {
    return `${project}_${command}`;
}