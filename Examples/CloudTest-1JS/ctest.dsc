import * as CloudTestClient from "Sdk.CloudTestClient";
import * as Drop from "Sdk.Drop";
import {Transformer} from "Sdk.Transformers";
import * as CT1JSSdk from "cloudtest-1js-sdk";

// This is a Map<JavaScriptProjectIdentifier, TransformerExecuteResult> with the execute result produced by each project+verb.
import { ctverbs } from "test-build";

// We create a drop upfront because we want to populate it with some artifacts before creating the session.
// This drop could be created outside of the build, but we'd need to add BuildXL support to make BuildXL ack the existence of an already created (unfinalized) drop.
const createDropResult = Drop.runner.createDrop(
    Object.merge<Drop.DropOperationArguments>(Drop.DropDaemonRunner.getDefaultDropConfig(
        // Just a unique name based on buildid/retry attempt.
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

// Read the scope JSON file produced as a pre-build step.
// This example assumes the scope file is already generated at this location.
const scopeJson : CT1JSSdk.ScopeFile = CT1JSSdk.readScopeFile(f`scopes.json`);

// Create the session. Pass the created drop.
// The session can also be created outside of BuildXL. Doing it inside abstracts some of the details away.
const sessionArgs : CloudTestClient.Helpers.GenerateSessionConfigAndCreateSessionArguments = {
    drop: createDropResult,
    tenant: "cloudtest-sample",
    sku: "test-sku",
    image: "ubuntu22.04",
    // Passed to BuildXL from the outside.
    tokenEnvVar: Environment.getStringValue("CloudTestTokenVariableName"),
    timeoutMinutes: 1,
    maxResources: 1,
    // The jobs are extracted from the pre-computed scopes.json file
    jobs: CT1JSSdk.getJobPlaceHolders(scopeJson),
    displayName: "My Test Session",
    dependencies: dropPrepResult.outputs
};
const sessionCreateResult = CloudTestClient.Helpers.generateConfigAndCreateSession(sessionArgs);

// Submit all the jobs
const jobs = CT1JSSdk.submitCloudTestJobs(scopeJson, ctverbs, sessionCreateResult);
