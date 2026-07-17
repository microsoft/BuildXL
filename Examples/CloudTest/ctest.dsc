import * as CloudTestClient from "Sdk.CloudTestClient";
import * as Drop from "Sdk.Drop";
import {Transformer} from "Sdk.Transformers";
// This is a Map<JavaScriptProjectIdentifier, TransformerExecuteResult> with the execute result produced by each project+verb.
import { buildContent } from "test-build";

// We create a drop upfront because we want to populate it with some artifacts before creating the session.
const createDropResult = Drop.runner.createDrop(
    Object.merge<Drop.DropOperationArguments>(Drop.DropDaemonRunner.getDefaultDropConfig(
        `BuildXL/Build/${Environment.getStringValue("BUILD_BUILDID")}/${Environment.getStringValue("SYSTEM_JOBATTEMPT")}/ctest-drop`, 
        "https://mseng.artifacts.visualstudio.com/DefaultCollection"
    ), {retentionDays: 1})
);

// This is just used to illustrate how a drop can be populated with artifacts before creating a session.
const testScript = Transformer.writeFile(p`${Context.getNewOutputDirectory("cloudtest")}/test.sh`, 
`#!/bin/bash
echo "Hello CloudTest!"
`);

// Create the session. Pass the created drop.
const sessionArgs : CloudTestClient.Helpers.GenerateSessionConfigAndCreateSessionArguments = {
    drop: createDropResult,
    tenant: "cloudtest-sample",
    // Passed to BuildXL from the outside.
    tokenEnvVar: Environment.getStringValue("CloudTestTokenVariableName"),
    timeoutMinutes: 1,
    // Send all JSON payloads to the console
    debug: true,
    // Force CT jobs to run. This pipeline is used as a release validation as well, so without this we will
    // hit the cache for the most part, since the inputs are not really churning organically.
    cacheEnabled: false,
    properties: Map.empty<string, string>()
        // We always want to see the logs
        .add("VstsTestResultAttachmentUploadBehavior", "Always"),
    // A session is made up of one or more groups. Here we use a single group; image/sku/maxResources and the
    // group's jobs are now group-level properties.
    groups: [
        {
            sku: "test-sku",
            image: "ubuntu22.04",
            maxResources: 1,
            // We build one job per package + verb.
            jobs: buildContent.keys().map(project => getTestJobName(project)),
        }
    ],
    displayName: "My Test Session",
    // Any artifact that is uploaded to the drop at session creation time (e.g. consumed later by test jobs) must be
    // provided through dropArtifacts so it participates in the CloudTest caching fingerprint.
    dropArtifacts: [ {kind: "file", file: testScript, dropPath: r`setup/test.sh`} ],
    historicRuntimes: { serviceConnectionId: Environment.getStringValue("ServiceConnectionId") },
};

const sessionCreateResult = CloudTestClient.Helpers.generateConfigAndCreateSession(sessionArgs);

// Submit one job per package + verb, with the corresponding build outputs as inputs.
const jobs = buildContent.toArray().map((kvp) => {
    const project : JavaScriptProjectIdentifier = kvp[0];
    const executeResult : TransformerExecuteResult = kvp[1];
    const staticDirectories : OpaqueDirectory[] = executeResult.getOutputDirectories();

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