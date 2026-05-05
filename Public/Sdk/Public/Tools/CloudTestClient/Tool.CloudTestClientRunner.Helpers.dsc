// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Drop from "Sdk.Drop";

namespace Helpers {
    /** A job definition with an explicit ID and name. */
    @@public
    export interface JobWithId {
        /** Job ID (GUID). */
        id: string;
        /** Job name. */
        name: string;
    }

    /** Arguments for generating a DJE session configuration JSON file. */
    @@public
    export interface GenerateSessionConfigArguments {
        /** CloudTest tenant name. */
        tenant: string;
        /**  Build drop name (e.g. 'my-drop') and service (e.g. 'https://my-drop-service.azure.com') if a new drop should be created, or a DropCreateResult if using an existing drop.  */
        drop: APIs.DropToCreate | Drop.DropCreateResult; 
        /** VM SKU (e.g. Standard_D4s_v3). */
        sku: string;
        /** VM image (e.g. ubuntu22.04). */
        image: string;
        /** Maximum number of VMs to allocate in parallel. */
        maxResources: number;
        /** Job definitions. Each can be a name (string) for auto-generated ID, or {id, name} to use a specific ID. */
        jobs: (string | JobWithId)[];
        /** Session display name. */
        displayName?: string;
        /** Submitting user alias. */
        user?: string;
        /** Enable job result caching. Default: false. */
        cacheEnabled?: boolean;
        /** Tags for the pip. */
        tags?: string[];
        /** Pip description override. */
        description?: string;
    }

    @@public
    export type TestParserType = "TRX" | "JUnit" | "TAEF" | "NUnitXml" | "TAP";

    @@public
    export type TestExecutionType = "MsTest" | "Exe" | "TAEF" | "NUnit" | "XUnit" | "BoostTest";

    /** Reference to a job by name, resolved at runtime from a session config file. */
    @@public
    export interface JobNameReference {
        /** Job name to look up in the session config. */
        jobName: string;
        /** Session config file containing the job name to ID mapping. */
        sessionConfigFile: File;
    }

    /** A job can be referenced by direct ID (string) or by name + config file. */
    @@public
    export type JobReference = string | JobNameReference;

    /** Arguments for generating an UpdateDynamicJob configuration JSON file. */
    @@public
    export interface DynamicJobConfigArguments {
        /** VM image (e.g. ubuntu22.04). Used to compute groupId. */
        image: string;
        /** VM SKU (e.g. Standard_D4s_v3). Used to compute groupId. */
        sku: string;
        /** Session ID: a File (produced by createSession) or a string GUID (from a pre-build step). */
        sessionId: APIs.SessionId;
        /** Job reference: either a direct job ID string, or a {jobName, sessionConfigFile} pair for name-based lookup. */
        jobReference: JobReference;
        /** Relative path within the drop containing this job's test files. */
        testFolder: RelativePath;
        /** Path to executable on the worker VM. */
        jobExecutable: Path | RelativePath;
        /** Test framework: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest. */
        testExecutionType: TestExecutionType;
        /** Arguments passed to the executable. */
        jobArguments?: string;
        /** Result parser: TRX, JUnit, TAEF, NUnitXml, TAP. Default: TRX. */
        testParserType?: TestParserType;
        /** Max job duration. Format: HH:MM:SS. */
        jobTimeout?: string;
        /** Per-test-case timeout. Format: HH:MM:SS. */
        testCaseTimeout?: string;
        /** VsoHash artifacts for job inputs, used for caching. The tool aggregates them into a single hash. */
        testDependencyHashes?: Artifact[];
        /** Job priority (lower = higher priority). Default: 0. */
        priority?: number;
        /** Tags for the pip. */
        tags?: string[];
        /** Pip description override. */
        description?: string;
    }

    /**
     * Arguments for generating a DJE session configuration JSON file and creating a session.
     */
    @@public
    export interface GenerateSessionConfigAndCreateSessionArguments extends GenerateSessionConfigArguments, APIs.CloudTestBaseArguments {
    }

    /**
     * The result of generating a session config, containing the generated config file and console output.
     */
    @@public
    export interface GenerateSessionConfigResult {
        /** Console output of the tool. */
        console: DerivedFile;
        /** The generated session configuration JSON file. */
        configFile: DerivedFile;
        /** The underlying execute result. */
        executeResult: Transformer.ExecuteResult;
    }

    /**
     * Packs the result of generating a session config and creating a session into a single object.
     */
    @@public
    export interface ConfigAndSessionResult {
        configArguments: GenerateSessionConfigAndCreateSessionArguments;
        configResult: GenerateSessionConfigResult;
        sessionResult: APIs.CreateSessionResult;
    }

    /**
     * Generates a DJE session configuration JSON file.
     * Auto-generates testSessionId, groupId (SHA256 of image+sku), and per-job GUIDs.
     * The generated file can be passed as bodyFile to createSession.
     */
    @@public
    export function generateSessionConfig(args: GenerateSessionConfigArguments): GenerateSessionConfigResult {
        const outDir = Context.getNewOutputDirectory("cloudtest-config");
        const configFile = p`${outDir}/session-config.json`;
        const consolePath = p`${outDir}/console.out`;

        const isAdo = isRunningOnAzureDevOps();

        const user = args.user || Environment.getStringValue("BUILD_REQUESTEDFOR") || Environment.getStringValue("USER") || "unknown";

        const dropService = getDropService(args.drop);
        const dropName = getDropName(args.drop);

        // Cache is enabled by default
        const cacheEnabled = args.cacheEnabled !== undefined ? args.cacheEnabled : true;

        let commandLineArgs: Argument[] = [
            Cmd.option("/mode:", "generateSessionConfig"),
            Cmd.option("/tenant:", args.tenant),
            Cmd.option("/buildDropLocation:", `${dropService}/_apis/drop/drops/${dropName}`),
            Cmd.option("/sku:", args.sku),
            Cmd.option("/image:", args.image),
            Cmd.option("/maxResources:", args.maxResources),
            Cmd.option("/configOutputFile:", Artifact.output(configFile)),
            ...args.jobs.map(job => jobToArgument(job)),
            Cmd.option("/displayName:", args.displayName),
            Cmd.option("/user:", user),
            Cmd.flag("/cacheEnabled", cacheEnabled),
            ...(isAdo ? [
                Cmd.option("/adoProjectId:", Environment.getStringValue("SYSTEM_TEAMPROJECTID")),
                Cmd.option("/adoCollectionUri:", Environment.getStringValue("SYSTEM_COLLECTIONURI")),
                Cmd.option("/adoBuildId:", Environment.getStringValue("BUILD_BUILDID")),
                Cmd.option("/adoAccessTokenEnvVar:", "SYSTEM_ACCESSTOKEN"),
            ] : []),
        ];

        let result = Transformer.execute({
            tool: APIs.tool,
            arguments: commandLineArgs,
            consoleOutput: consolePath,
            workingDirectory: outDir,
            tags: [...(args.tags || []), "cloudtest"],
            description: args.description || "CloudTest: Generate Session Config",
            // We always want to generate a new config file, so a new CT session ID is generated. We are leaving the
            // caching aspect to CloudTest.
            disableCacheLookup: true,
            unsafe: isAdo
                ? { passThroughEnvironmentVariables: ["SYSTEM_ACCESSTOKEN"] }
                : undefined,
        });

        return {
            console: result.getOutputFile(consolePath),
            configFile: result.getOutputFile(configFile),
            executeResult: result,
        };
    }

    /**
     * Generates a DJE session configuration JSON file and creates a session.
     */
    @@public
    export function generateConfigAndCreateSession(args: GenerateSessionConfigAndCreateSessionArguments): ConfigAndSessionResult {
        const configResult = generateSessionConfig(args);

        const createSessionArgs: APIs.CreateSessionArguments = {
            tenant: args.tenant,
            tokenEnvVar: args.tokenEnvVar,
            timeoutMinutes: args.timeoutMinutes,
            dependencies: [...(args.dependencies || []), configResult.configFile],
            environmentVariables: args.environmentVariables,
            tags: args.tags,
            description: args.description,
            bodyFile: configResult.configFile,
            drop: args.drop,
        };

        return {
            configArguments: args,
            configResult: configResult,
            sessionResult: APIs.createSession(createSessionArgs),
        };
    }

    /**
     * Generates an UpdateDynamicJob configuration JSON file.
     * Computes groupId from image+sku. The generated file can be passed as bodyFile to updateDynamicJob.
     */
    @@public
    export function generateUpdateDynamicJobConfig(args: DynamicJobConfigArguments): GenerateSessionConfigResult {
        const outDir = Context.getNewOutputDirectory("cloudtest-update-config");
        const configFile = p`${outDir}/update-dynamic-job-config.json`;
        const consolePath = p`${outDir}/console.out`;

        let commandLineArgs: Argument[] = [
            Cmd.option("/mode:", "generateUpdateDynamicJobConfig"),
            Cmd.option("/image:", args.image),
            Cmd.option("/sku:", args.sku),
            ...APIs.sessionIdArgs(args.sessionId),
            ...(isJobNameReference(args.jobReference)
                ? [
                    Cmd.option("/jobName:", (<JobNameReference>args.jobReference).jobName),
                    Cmd.option("/sessionConfigPath:", Artifact.input((<JobNameReference>args.jobReference).sessionConfigFile)),
                  ]
                : [Cmd.option("/jobId:", <string>args.jobReference)]),
            Cmd.option("/testFolder:", args.testFolder),
            Cmd.option("/jobExecutable:", args.jobExecutable),
            Cmd.option("/testExecutionType:", args.testExecutionType),
            Cmd.option("/configOutputFile:", Artifact.output(configFile)),
            Cmd.option("/jobArguments:", args.jobArguments),
            Cmd.option("/testParserType:", args.testParserType || "TRX"),
            Cmd.option("/jobTimeout:", args.jobTimeout),
            Cmd.option("/testCaseTimeout:", args.testCaseTimeout),
            Cmd.options("/testDependencyHash:", args.testDependencyHashes || []),
            Cmd.option("/priority:", args.priority),
        ];

        let dependencies: Transformer.InputArtifact[] = [];
        if (APIs.isSessionIdFile(args.sessionId)) {
            dependencies = [...dependencies, <File>args.sessionId];
        }
        if (isJobNameReference(args.jobReference)) {
            dependencies = [...dependencies, (<JobNameReference>args.jobReference).sessionConfigFile];
        }

        let result = Transformer.execute({
            tool: APIs.tool,
            arguments: commandLineArgs,
            consoleOutput: consolePath,
            workingDirectory: outDir,
            dependencies: dependencies,
            // A job submission will naturally be uncacheable since it will have a unique session ID. But we let's make it explicit.
            disableCacheLookup: true,
            tags: [...(args.tags || []), "cloudtest"],
            description: args.description || "CloudTest: Generate UpdateDynamicJob Config",
        });

        return {
            console: result.getOutputFile(consolePath),
            configFile: result.getOutputFile(configFile),
            executeResult: result,
        };
    }

    /**
     * Arguments for submitting a job to an existing session.
     */
    @@public
    export interface SubmitJobArguments {
        /** Configuration and session result from generating the session config. */
        configAndSessionResult: Helpers.ConfigAndSessionResult;
        /** Name of the job. */
        jobName: string;
        /** Input artifacts for the job. Only files and opaque directories are supported. */
        jobInputs: (File | OpaqueDirectory)[];
        /** Path to executable on the worker VM. */
        jobExecutable: Path | RelativePath;
        /** Test framework: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest. */
        testExecutionType: TestExecutionType;
        /** Arguments passed to the executable. */
        jobArguments?: string;
        /** Result parser: TRX, JUnit, TAEF, NUnitXml, TAP. Default: TRX. */
        testParserType?: TestParserType;
        /** Max job duration. Format: HH:MM:SS. */
        jobTimeout?: string;
        /** Per-test-case timeout. Format: HH:MM:SS. */
        testCaseTimeout?: string;
        /** Job priority (lower = higher priority). Default: 0. */
        priority?: number;
        /** Tags for the pip. */
        tags?: string[];
        /** Pip description override. */
        description?: string;
        /** Timeout for the job in minutes. */
        timeoutMinutes?: number;
        /** Additional environment variables to set for the job. */
        environmentVariables?: Transformer.EnvironmentVariable[];
    }

    /**
     * Submits a job to an existing session. This is a helper that combines the steps of adding job artifacts to the drop, generating an UpdateDynamicJob config file, and calling the UpdateDynamicJob API.
     */
    @@public 
    export function submitJob(args: SubmitJobArguments): APIs.CloudTestResult {
        
        let artifactInfos = args.jobInputs.map(input => {
            if (Transformer.isStaticDirectory(input)) {
                const info : Drop.DropDirectoryInfo = {
                    kind: "directory",
                    directory: input,
                    dropPath: r`${args.jobName}`
                };
                return info;
            } else {
                const info : Drop.DropFileInfo = {
                    kind: "file",
                    file: input,
                    dropPath: r`${args.jobName}`
                };
                return info;
            }
        });

        let addArtifactsResult = Drop.runner.addArtifactsToDrop(
            args.configAndSessionResult.sessionResult.createDropResult,
            // Just use the defaults
            {},
            artifactInfos
        );

        const dynamicJobConfigArguments : DynamicJobConfigArguments = {
            image: args.configAndSessionResult.configArguments.image,
            sku: args.configAndSessionResult.configArguments.sku,
            sessionId: args.configAndSessionResult.sessionResult.sessionIdFile,
            jobReference: {jobName: args.jobName, sessionConfigFile: args.configAndSessionResult.configResult.configFile}, 
            testFolder: r`${args.jobName}`,
            jobExecutable: args.jobExecutable,
            testExecutionType: args.testExecutionType,
            jobArguments: args.jobArguments,
            testParserType: args.testParserType,
            jobTimeout: args.jobTimeout,
            testCaseTimeout: args.testCaseTimeout,
            // We always provide the hashes for the job inputs since CloudTest will try to store the results in the cache regardless of whether
            // cacheEnabled was set to true or false when creating the session (the flag is just honored for cache lookups)
            testDependencyHashes:  Artifact.vsoHashes(args.jobInputs),
            priority: args.priority,
            tags: args.tags,
            description: args.description,
        };

        const configResult = generateUpdateDynamicJobConfig(dynamicJobConfigArguments);

        let updateDynamicJob : APIs.UpdateDynamicJobArguments = {
            sessionId: args.configAndSessionResult.sessionResult.sessionIdFile,
            bodyFile: configResult.configFile,
            dependencies: [...args.jobInputs, configResult.configFile, ...addArtifactsResult.outputs],
            tokenEnvVar: args.configAndSessionResult.configArguments.tokenEnvVar,
            timeoutMinutes: args.timeoutMinutes,
            environmentVariables: args.environmentVariables,
            tags: args.tags,
            description: args.description,
            tenant: args.configAndSessionResult.configArguments.tenant,
        };

        const updateResult = APIs.updateDynamicJob(updateDynamicJob);

        return updateResult;
    }

    /**
     * Arguments for waiting for a session to complete.
     */
    @@public
    export interface WaitForCompletionArguments {
        /** Configuration and session result from generating the session config. */
        configAndSessionResult: Helpers.ConfigAndSessionResult;
        /** 
         * The jobs that have been submitted and are being waited on. 
         * These are optional. By passing them, the pip that polls for the session completion will only start after all the provided jobs are submitted. 
         * */
        submittedJobs?: APIs.CloudTestResult[];
        /** The maximum amount of time, in minutes, to wait for the session to complete. */
        timeoutMinutes?: number;
        /** Tags to associate with the wait operation. */
        tags?: string[];
        /** A description for the wait operation. */
        description?: string;
    }

    /**
     * Waits for a session to complete. This is a helper that calls the WaitForSessionCompletion API and passes the session ID and token from the configAndSessionResult, 
     * along with any provided tags and description for the pip, and returns the CloudTestResult.
     */
    @@public
    export function waitForCompletion(args: WaitForCompletionArguments) : APIs.CloudTestResult {
        const waitArgs : APIs.WaitForSessionCompletionArguments = {
            sessionId: args.configAndSessionResult.sessionResult.sessionIdFile,
            tokenEnvVar: args.configAndSessionResult.configArguments.tokenEnvVar,
            tenant: args.configAndSessionResult.configArguments.tenant,
            environmentVariables: args.configAndSessionResult.configArguments.environmentVariables,
            timeoutMinutes: args.timeoutMinutes,
            tags: args.tags,
            description: args.description,
            dependencies: args.submittedJobs && args.submittedJobs.mapMany(job => job.executeResult.getOutputFiles()) || [],
        };

        return APIs.waitForSessionCompletion(waitArgs);
    }

    // ============================================================================
    // Internal helpers
    // ============================================================================

    function isJobNameReference(ref: JobReference): ref is JobNameReference {
        return typeof(ref) !== "string";
    }

    function isJobWithId(job: string | JobWithId): job is JobWithId {
        return typeof(job) !== "string";
    }

    function jobToArgument(job: string | JobWithId): Argument {
        return isJobWithId(job)
            ? Cmd.option("/jobIdAndName:", `${job.id}#${job.name}`)
            : Cmd.option("/jobName:", job);
    }

    /**
     * Detect if the current environment is Azure DevOps by checking for the existence of specific environment variables.
     */
    function isRunningOnAzureDevOps(): boolean {
        return Environment.hasVariable("BUILD_BUILDID")
            && Environment.hasVariable("SYSTEM_COLLECTIONURI")
            && Environment.hasVariable("SYSTEM_TEAMPROJECTID");
    }

    function getDropName(drop: APIs.DropToCreate | Drop.DropCreateResult) : string {
        if (APIs.isDropResult(drop)) {
            return drop.dropConfig.name;
        } else {
            return drop.name;
        }
    }

    function getDropService(drop: APIs.DropToCreate | Drop.DropCreateResult) : string {
        if (APIs.isDropResult(drop)) {
            return drop.dropConfig.service;
        } else {
            return drop.service;
        }
    }
}


