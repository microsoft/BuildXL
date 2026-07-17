// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Drop from "Sdk.Drop";

/**
 * This namespace has low-level APIs for interacting with CloudTest Dynamic Job Execution (DJE), that just take strongly-typed arguments and return strongly-typed results. 
 * These APIs will be used by the Helpers namespace, which will contain higher-level helper functions that implement common patterns (e.g. create session + submit jobs + wait for completion) and can be
 * used directly from build scripts.
 */
namespace APIs {
    const root = d`.`;
    const isWindows: boolean = Context.isWindowsOS();

    /**
     * Definition of the CloudTest DJE client tool.
     */
    @@public
    export const tool: Transformer.ToolDefinition = {
        exe: isWindows ? f`${root}/bin/CloudTestClient.exe` : f`${root}/bin/CloudTestClient`,
        description: "CloudTest DJE Client",
        runtimeDependencies: globR(d`${root}/bin`, "*"),
        dependsOnCurrentHostOSDirectories: true,
        prepareTempDirectory: true,
        untrackedFiles: [
            f`${Context.getBuildEngineDirectory().path}`
        ],
        untrackedDirectoryScopes: [
            d`${Context.getUserHomeDirectory().path}/.dotnet/corefx/cryptography`
        ]
    };

    /**
     * Warning regex applied to the CloudTestClient tool output.
     */
    @@public
    export const warningRegex: string = "(?i).*WARNING:.*";

    /** A session ID can be either a file containing the ID (e.g. from createSession) or a string GUID (e.g. from a pre-build step). */
    @@public
    export type SessionId = File | string;

    /** Base arguments common to all CloudTest operations. */
    @@public
    export interface CloudTestBaseArguments {
        /** CloudTest tenant name. */
        tenant: string;
        /** Name of the environment variable holding the bearer token. */
        tokenEnvVar: string;
        /** Overall timeout in minutes. Default: 5. */
        timeoutMinutes?: number;
        /** CloudTest API environment: "prod" (default), "dev", or "ppe". */
        environment?: "prod" | "dev" | "ppe";
        /** Additional pip dependencies. */
        dependencies?: Transformer.InputArtifact[];
        /** Additional environment variables to pass. */
        environmentVariables?: Transformer.EnvironmentVariable[];
        /** Tags for the pip. */
        tags?: string[];
        /** Pip description override. */
        description?: string;
        /** When true, the CloudTest client logs the JSON payloads of all REST submissions it performs against the CloudTest endpoint to the console. Default: false. */
        debug?: boolean;
    }

    /**
     * Arguments for creating a drop before creating a CloudTest DJE session.
     */
    @@public
    export interface DropToCreate {
        /** Name of the drop to create. */
        name: string;
        /** Service associated with the drop. */
        service: string;
    }

    /**
     * Identifies an entity authorized to read the CloudTest database, used to retrieve historic job runtimes.
     * Exactly one of `serviceConnectionId` (ADO) or `entraTokenEnvVarName` (non-ADO) must be provided.
     * The mere presence of this object is what enables historic runtime retrieval.
     */
    @@public
    export interface HistoricRuntimesArguments {
        /** Azure DevOps service connection ID for workload identity federation (ADO case). Mutually exclusive with entraTokenEnvVarName. */
        serviceConnectionId?: string;
        /** Name of an environment variable holding an Entra token authorized to read the CloudTest database (non-ADO case). Mutually exclusive with serviceConnectionId. */
        entraTokenEnvVarName?: string;
    }

    /** Arguments for creating a CloudTest DJE session. */
    @@public
    export interface CreateSessionArguments extends CloudTestBaseArguments {
        /** Optional drop creation arguments. If provided, a drop will be created before the session is created. */
        drop: DropToCreate | Drop.DropCreateResult; 
        /** Path to the JSON file containing the session request body. */
        bodyFile: File;
        /**
         * Optional artifacts to upload to the drop before the session is created.
         *
         * IMPORTANT: Any artifact that must be uploaded to the drop for group-setup-related activity (e.g. setup/cleanup scripts,
         * service payloads, or any other content placed in the drop) and is later consumed by test jobs MUST be uploaded through
         * this parameter. Artifacts uploaded to the drop by other means are not accounted for in the CloudTest caching fingerprint,
         * which can result in under-builds (stale cached results being reused when these inputs change).
         */
        dropArtifacts?: Drop.DropArtifactInfo[];
        /** When provided, enables retrieval of historic job runtimes from the CloudTest database using the given identity. When omitted, historic runtime retrieval is disabled. */
        historicRuntimes?: HistoricRuntimesArguments;
    }

    /** Arguments for updating a dynamic job in an existing session. */
    @@public
    export interface UpdateDynamicJobArguments extends CloudTestBaseArguments {
        /** Path to the JSON file containing the job update request body. */
        bodyFile: File;
        /** Session ID: a File (produced by createSession) or a string GUID (from a pre-build step). */
        sessionId: SessionId;
        /** Optional file containing historic runtime data for this job. When provided, the tool reads the avg duration and emits it via ##buildxl[runtime]. */
        historicRuntimeFile?: File;
    }

    /** Arguments for cancelling an existing session. */
    @@public
    export interface CancelSessionArguments extends CloudTestBaseArguments {
        /** Session ID: a File (produced by createSession) or a string GUID (from a pre-build step). */
        sessionId: SessionId;
    }


    /** Arguments for querying the status of an existing session (polls until completion). */
    @@public
    export interface WaitForSessionCompletionArguments extends CloudTestBaseArguments {
        /** Session ID: a File (produced by createSession) or a string GUID (from a pre-build step). */
        sessionId: SessionId;
    }
    /**
     * The result of creating a CloudTest DJE session, containing the session ID file, log output, and console output.
     */
    @@public
    export interface CreateSessionResult {
        /** Console output of the tool. */
        console: DerivedFile;
        /** File containing the session ID (plain text). */
        sessionIdFile: DerivedFile;
        /** The underlying execute result. */
        executeResult: Transformer.ExecuteResult;
        /** The result of creating a drop, if a drop was created by BuildXL. Undefined otherwise. */
        createDropResult?: Drop.DropCreateResult;
        /** Directory containing per-job historic runtime JSON files. Undefined if historic runtime retrieval was not enabled. */
        historicRuntimesDir?: OpaqueDirectory;
    }

    /**
     * The result of submitting a job, updating a dynamic job, or cancelling a session, containing console output, log output, and the execute result.
     */
    @@public
    export interface CloudTestResult {
        /** Console output of the tool. */
        console: DerivedFile;
        /** The underlying execute result. */
        executeResult: Transformer.ExecuteResult;
    }

    /**
     * Creates a CloudTest DJE session. Blocks until the session completes.
     * Returns the session ID file and log output.
     */
    @@public
    export function createSession(args: CreateSessionArguments): CreateSessionResult {

        // Historic runtime retrieval is enabled by providing an identity authorized to read the CloudTest database.
        // When provided, exactly one of serviceConnectionId (ADO) or entraTokenEnvVarName (non-ADO) must be set.
        const historicRuntimesEnabled = args.historicRuntimes !== undefined;
        if (historicRuntimesEnabled) {
            const hasServiceConnectionId = args.historicRuntimes.serviceConnectionId !== undefined;
            const hasEntraTokenEnvVar = args.historicRuntimes.entraTokenEnvVarName !== undefined;
            Contract.requires(
                (hasServiceConnectionId || hasEntraTokenEnvVar) && !(hasServiceConnectionId && hasEntraTokenEnvVar),
                "historicRuntimes requires exactly one of 'serviceConnectionId' or 'entraTokenEnvVarName' to be provided."
            );
        }

        let createDropResult: Drop.DropCreateResult = undefined;
        // If a Drop.DropCreateResult is provided, use it directly. Otherwise, create a new drop.
        if (isDropResult(args.drop)) {
            createDropResult = <Drop.DropCreateResult>args.drop;
        }
        else {
            const dropToCreate = <DropToCreate>args.drop;
            // Let's use just one day of retention for the drop, since this is used just to communicate artifacts to the CT session.
            const dropConfig = Object.merge<Drop.DropOperationArguments>(Drop.DropDaemonRunner.getDefaultDropConfig(dropToCreate.name, dropToCreate.service), {retentionDays: 1});
            createDropResult = Drop.runner.createDrop(dropConfig);
        }

        // Optionally upload artifacts to the drop before creating the session. The session-creation pip takes a
        // dependency on the upload outputs so the artifacts are guaranteed to be in the drop before submission.
        // (These artifacts are also expected to participate in the session hash computation in the future.)
        const addArtifactsResultOutputs = (args.dropArtifacts !== undefined && args.dropArtifacts.length > 0)
            ? Drop.runner.addArtifactsToDrop(createDropResult, {}, args.dropArtifacts).outputs
            : [];

        const outDir = Context.getNewOutputDirectory("cloudtest");
        const sessionIdFile = p`${outDir}/session-id.txt`;
        const consolePath = p`${outDir}/console.out`;
        const historicRuntimesDir = historicRuntimesEnabled ? d`${outDir}/historic-runtimes` : undefined;

        // Environment variables the tool needs to authenticate against the CloudTest database, depending on the chosen identity.
        const historicRuntimesEnvVars = historicRuntimesEnabled
            ? (args.historicRuntimes.entraTokenEnvVarName !== undefined
                ? [args.historicRuntimes.entraTokenEnvVarName]
                : ["SYSTEM_OIDCREQUESTURI"])
            : [];

        let commandLineArgs: Argument[] = [
            Cmd.option("/mode:", "createSession"),
            Cmd.option("/tenant:", args.tenant),
            Cmd.option("/tokenEnvVar:", args.tokenEnvVar),
            Cmd.option("/bodyFile:", Artifact.input(args.bodyFile)),
            Cmd.option("/sessionIdFile:", Artifact.output(sessionIdFile)),
            Cmd.option("/timeout:", args.timeoutMinutes),
            ...(historicRuntimesEnabled ? [
                Cmd.option("/historicRuntimesOutputDir:", Artifact.output(historicRuntimesDir)),
                Cmd.option("/historicRuntimeServiceConnectionId:", args.historicRuntimes.serviceConnectionId),
                Cmd.option("/historicRuntimeEntraTokenEnvVar:", args.historicRuntimes.entraTokenEnvVarName),
            ] : []),
            Cmd.option("/environment:", args.environment),
            Cmd.flag("/debug", args.debug),
        ];

        // The pip timeout is a property of the executing tool. Let's make sure it is not below the timeout the user is willing to wait for
        // the corresponding underlying REST operation.
        let executeTool = tool;
        if (args.timeoutMinutes !== undefined) {
            executeTool = Object.merge<Transformer.ToolDefinition>(
                executeTool, 
                { timeoutInMilliseconds: args.timeoutMinutes * 60 * 1000 });
        }

        let result = Transformer.execute({
            tool: executeTool,
            arguments: commandLineArgs,
            consoleOutput: consolePath,
            workingDirectory: outDir,
            warningRegex: warningRegex,
            dependencies: [
                ...(args.dependencies || []), 
                ...(createDropResult ? createDropResult.outputs : []), 
                ...addArtifactsResultOutputs],
            environmentVariables: args.environmentVariables || [],
            tags: [...(args.tags || []), "cloudtest"],
            description: args.description || "CloudTest: Create Session",
            // We expect the create session operation to be uncacheable since it will produce a unique session ID file. But let's make it explicit.
            disableCacheLookup: true,
            unsafe: {
                passThroughEnvironmentVariables: [args.tokenEnvVar, "USER", "SYSTEM_ACCESSTOKEN", ...historicRuntimesEnvVars]
            }
        });

        return {
            console: result.getOutputFile(consolePath),
            sessionIdFile: result.getOutputFile(sessionIdFile),
            executeResult: result,
            createDropResult: createDropResult,
            historicRuntimesDir: historicRuntimesDir !== undefined ? result.getOutputDirectory(historicRuntimesDir) : undefined,
        };
    }

    /**
     * Updates a dynamic job in an existing CloudTest DJE session.
     */
    @@public
    export function updateDynamicJob(args: UpdateDynamicJobArguments): CloudTestResult {
        return executeWithSessionId(args, "updateDynamicJob", args.sessionId, args.bodyFile, /* mustRunOnOrchestrator */ false, args.historicRuntimeFile);
    }

    /**
     * Cancels an existing CloudTest DJE session.
     */
    @@public
    export function cancelSession(args: CancelSessionArguments): CloudTestResult {
        return executeWithSessionId(args, "cancelSession", args.sessionId, undefined, /* mustRunOnOrchestrator */ false);
    }


    /**
     * Polls a CloudTest DJE session until completion and reports its result.
     * Exit code 0 if the session result is Passed; non-zero otherwise.
     * On failure, the full REST response is written to stderr.
     */
    @@public
    export function waitForSessionCompletion(args: WaitForSessionCompletionArguments): CloudTestResult {
        // Force this pip to run on the orchestrator so we don't hold any workers just to poll for the session completion.
        return executeWithSessionId(args, "waitForSessionCompletion", args.sessionId, undefined, /* mustRunOnOrchestrator */ true);
    }

    // ============================================================================
    // Internal helpers (exported for use by Helpers namespace)
    // ============================================================================

    export function isSessionIdFile(sessionId: SessionId): sessionId is File {
      return (sessionId as DerivedFile).path !== undefined;
    }

    export function sessionIdArgs(sessionId: SessionId): Argument[] {
        return isSessionIdFile(sessionId)
            ? [Cmd.option("/sessionIdFile:", Artifact.input(sessionId))]
            : [Cmd.option("/sessionId:", sessionId)];
    }

    function executeWithSessionId(args: CloudTestBaseArguments, mode: string, sessionId: SessionId, bodyFile: File, mustRunOnOrchestrator: boolean, historicRuntimeFile?: File): CloudTestResult {
        const outDir = Context.getNewOutputDirectory("cloudtest");
        const consolePath = p`${outDir}/console.out`;

        let commandLineArgs: Argument[] = [
            Cmd.option("/mode:", mode),
            Cmd.option("/tenant:", args.tenant),
            Cmd.option("/tokenEnvVar:", args.tokenEnvVar),
            Cmd.option("/bodyFile:", Artifact.input(bodyFile)),
            Cmd.option("/timeout:", args.timeoutMinutes),
            Cmd.option("/environment:", args.environment),
            Cmd.flag("/debug", args.debug),
            ...sessionIdArgs(sessionId),
            Cmd.option("/historicRuntimeFile:", Artifact.input(historicRuntimeFile)),
        ];

        // The pip timeout is a property of the executing tool. Let's make sure it is not below the timeout the user is willing to wait for
        // the corresponding underlying REST operation.
        let executeTool = tool;
        if (args.timeoutMinutes !== undefined) {
            executeTool = Object.merge<Transformer.ToolDefinition>(
                executeTool, 
                { timeoutInMilliseconds: args.timeoutMinutes * 60 * 1000 });
        }

        let result = Transformer.execute({
            tool: executeTool,
            arguments: commandLineArgs,
            consoleOutput: consolePath,
            workingDirectory: outDir,
            warningRegex: warningRegex,
            dependencies: args.dependencies || [],
            environmentVariables: args.environmentVariables || [],
            tags: [...(args.tags || []), "cloudtest"],
            description: args.description || `CloudTest: ${mode}`,
            // These operations are all fire-and-forget REST calls. Even if the involve polling for completion, they are not expecting to be CPU comsuming operations, 
            // so we can mark the pip as light and avoid unnecessary resource contention with other CPU-intensive pips.
            isLight: true,
            // We expect these operations to be uncacheable since they will be operating on a specific session ID. But let's make it explicit.
            disableCacheLookup: true,
            mustRunOnOrchestrator: mustRunOnOrchestrator,
            unsafe: {
                passThroughEnvironmentVariables: [args.tokenEnvVar]
            }
        });

        return {
            console: result.getOutputFile(consolePath),
            executeResult: result,
        };
    }

    export function isDropResult(drop: DropToCreate | Drop.DropCreateResult): drop is Drop.DropCreateResult {
        return (<Drop.DropCreateResult>drop).dropConfig !== undefined;
    }
}
