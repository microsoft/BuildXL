import * as CloudTestClient from "Sdk.CloudTestClient";
import * as Drop from "Sdk.Drop";
import {Transformer} from "Sdk.Transformers";
// This is a Map<JavaScriptProjectIdentifier, OpaqueDirectory[]> with the outputs produced by each project+verb.
import { buildContent } from "test-build";

// We create a drop upfront because we want to populate it with some artifacts before creating the session.
const createDropResult = Drop.runner.createDrop(
    // TODO: once the Drop SDK is in place, replace this with a call to Drop.getDefaultDropConfig
    Object.merge<Drop.DropOperationArguments>(CloudTestClient.APIs.getDefaultDropConfig(
        `BuildXL/Build/${Environment.getStringValue("BUILD_BUILDID")}/${Environment.getStringValue("SYSTEM_JOBATTEMPT")}/ctest-drop`, 
        "https://mseng.artifacts.visualstudio.com/DefaultCollection"
    ), {retentionDays: 1})
);

// This is just used to illustrate how a drop can be created upfront and populated with artifacts before creating a session.
const testScript = Transformer.writeFile(p`${Context.getNewOutputDirectory("cloudtest")}/test.sh`, 
`#!/bin/bash
echo "Hello CloudTest!"
`);

const dropPrepResult = Drop.runner.addArtifactsToDrop(
    createDropResult,
    // Just use the defaults
    {},
    [ {kind: "file", file: testScript, dropPath: r`setup/test.sh`}]
);

// Create the session. Pass the created drop.
const sessionArgs : CloudTestClient.Helpers.GenerateSessionConfigAndCreateSessionArguments = {
    drop: createDropResult,
    tenant: "cloudtest-sample",
    sku: "test-sku",
    image: "ubuntu22.04",
    // Passed to BuildXL from the outside.
    tokenEnvVar: Environment.getStringValue("CloudTestTokenVariableName"),
    timeoutMinutes: 1,
    maxResources: 1,
    // We build one job per package + verb.
    jobs: buildContent.keys().map(project => getTestJobName(project)),
    displayName: "My Test Session",
    dependencies: dropPrepResult.outputs
};

const sessionCreateResult = CloudTestClient.Helpers.generateConfigAndCreateSession(sessionArgs);

// Submit one job per package + verb, with the corresponding build outputs as inputs.
const jobs = buildContent.toArray().map((kvp) => {
    const project : JavaScriptProjectIdentifier = kvp[0];
    const staticDirectories : OpaqueDirectory[] = kvp[1];

    const submitJobArgs : CloudTestClient.Helpers.SubmitJobArguments = {
        jobName: getTestJobName(project),
        // This is just a mock job that produces a TAP file with one successful test.
        jobExecutable: p`/bin/bash`,
        jobArguments: `-c "{ echo 'TAP version 13'; echo '1..1'; echo 'ok 1 - test'; } > $LoggingDirectory/result.tap"`,
        testExecutionType: "Exe",
        testParserType: "TAP",
        jobInputs: staticDirectories,
        configAndSessionResult: sessionCreateResult,
    };

    return CloudTestClient.Helpers.submitJob(submitJobArgs);
});

// Wait for the cloudtest session to complete. For now we wait for this as part of the build.
// In the future we may have an agent-less task that just waits for the session completion and reports the result, so we don't have to use an agent for this if the CT session is the last
// thing that happens on a pipeline
const sessionResult = CloudTestClient.Helpers.waitForCompletion({
    configAndSessionResult: sessionCreateResult,
    // This is optional, but by passing them, the pip that polls for the session completion will only start after all the provided jobs are submitted.
    submittedJobs: jobs,
    timeoutMinutes: 10
});

// We need to give a name to each test job. Use the package name + verb for that
function getTestJobName(project: JavaScriptProjectIdentifier): string {
    return `${project.packageName}_${project.command}`;
}