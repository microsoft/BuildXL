import * as CloudTestClient from "Sdk.CloudTestClient";
import * as Drop from "Sdk.Drop";
import {Transformer} from "Sdk.Transformers";
import * as CT1JSSdk from "cloudtest-1js-sdk";

// This is a Map<JavaScriptProjectIdentifier, TransformerExecuteResult> with the execute result produced by each project+verb.
import { ctverbs } from "test-build";

// The drop service and a unique drop name (based on buildid/retry attempt).
const dropService = "https://mseng.artifacts.visualstudio.com/DefaultCollection";
const dropName = `BuildXL/Build/${Environment.getStringValue("BUILD_BUILDID")}/${Environment.getStringValue("SYSTEM_JOBATTEMPT")}/ctest-drop`;

// We create a drop upfront because we want to populate it with some artifacts before creating the session.
// This drop could be created outside of the build, but we'd need to add BuildXL support to make BuildXL ack the existence of an already created (unfinalized) drop.
const createDropResult = Drop.runner.createDrop(
    Object.merge<Drop.DropOperationArguments>(
        Drop.DropDaemonRunner.getDefaultDropConfig(dropName, dropService),
        {retentionDays: 1})
);

// This is just used to illustrate how a drop can be populated with artifacts before creating a session.
const testScript = Transformer.writeFile(p`${Context.getNewOutputDirectory("cloudtest")}/test.sh`, 
`#!/bin/bash
echo "Hello CloudTest!"
`);

const dropUrl = `${dropService}/_apis/drop/drops/${dropName}`;

// Read the scope JSON file produced as a pre-build step.
// This example assumes the scope file is already generated at this location.
const scopeJson : CT1JSSdk.ScopeFile = CT1JSSdk.readScopeFile(f`scopes.json`);

// Create the session. Pass the created drop.
// The session can also be created outside of BuildXL. Doing it inside abstracts some of the details away.
const sessionArgs : CloudTestClient.Helpers.GenerateSessionConfigAndCreateSessionArguments = {
    drop: createDropResult,
    tenant: "cloudtest",
    // Passed to BuildXL from the outside.
    tokenEnvVar: Environment.getStringValue("CloudTestTokenVariableName"),
    timeoutMinutes: 1,
    environment: "ppe",
    // Send all JSON payloads to the console
    debug: true,
    // Turn on the shared download cache on CT side. This will eventually become the default
    properties: Map.empty<string, string>()
        .add("UseSharedDownloadContentCache", "true"),
        //.add("SharedDownloadContentCacheRealization", "Copy") // Hardlink mode is the default, but it can be changed to Copy if content cannot be left as read-only.
        //.add("SharedDownloadContentCacheSizeMegabytes", "10000") // 50GB is the default max quota, but that can be adjusted as needed.
    fileProviders: [
        {
            type: "VsoDrop",
            properties: [
                { name: "CloudTest.ProviderCustomName", value: "[BuildDrop]" },
                { name: "DropUrl", value: dropUrl },
                { name: "BuildRoot", value: "" },
            ],
        },
    ],
    // A session is made up of one or more groups. Here we use two groups to illustrate partitioning jobs across
    // groups (e.g. by package): each group has its own image/sku, optional name, jobs, and setup/cleanup.
    // Job names are unique across groups, so submission resolves each job's group by name automatically.
    groups: [
        {
            name: "b-package-group",
            sku: "DefaultCloudTestSku",
            image: "CTLinuxUbuntu2204",
            maxResources: 1,
            // The jobs for @test/b-package, extracted from the pre-computed scopes.json file.
            jobs: CT1JSSdk.getJobPlaceHolders(scopeJson, pkg => pkg === "@test/b-package"),
            // Group setup runs once on the worker before any of the group's jobs. We want to run test.sh,
            // which lives in the drop. 
            dynamicGroupSetup: {
                dataFiles: [
                    { source: { prefix: "BuildRoot", path: r`test.sh` }, destination: r`setup` },
                ],
                scripts: [
                    { path: p`/bin/bash`, args: "[WorkingDirectory]/setup/test.sh", scriptName: "b-package-setup" },
                ],
            }
        },
        {
            name: "c-package-group",
            sku: "DefaultCloudTestSku",
            image: "CTLinuxUbuntu2404",
            maxResources: 1,
            // The jobs for @test/c-package. This group has no setup, showing groups can be configured independently.
            jobs: CT1JSSdk.getJobPlaceHolders(scopeJson, pkg => pkg === "@test/c-package"),
            dynamicGroupSetup: {
                // We need to include data files as well, even though they are not being consumed
                // Bug https://dev.azure.com/mseng/1ES/_workitems/edit/2438631 tracking it
                dataFiles: [
                    { source: { prefix: "BuildRoot", path: r`test.sh` }, destination: r`setup` },
                ],
            }
        }
    ],
    displayName: "My Test Session",
    // The test.sh script is consumed by the b-package-group's dynamicGroupSetup. Uploading it through dropArtifacts
    // (instead of a manual addArtifactsToDrop) ensures it participates in the CloudTest caching fingerprint.
    dropArtifacts: [ {kind: "file", file: testScript, dropPath: r`test.sh`} ],
};
const sessionCreateResult = CloudTestClient.Helpers.generateConfigAndCreateSession(sessionArgs);

// Submit all the jobs
const jobs = CT1JSSdk.submitCloudTestJobs(scopeJson, ctverbs, sessionCreateResult);
