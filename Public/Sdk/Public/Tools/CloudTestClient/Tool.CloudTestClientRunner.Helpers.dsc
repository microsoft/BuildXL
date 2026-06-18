// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Drop from "Sdk.Drop";
import * as Json from "Sdk.Json";

namespace Helpers {
    /** A job definition with an explicit ID and name. */
    @@public
    export interface JobWithId {
        /** Job ID (GUID). */
        id: string;
        /** Job name. */
        name: string;
    }

    // ============================================================================
    // CloudTest DJE schema types for group setup, cleanup, and file providers
    // ============================================================================

    /**
     * Well-known CloudTest macro prefixes for path resolution on worker VMs.
     * These are mapped as [prefix] when resolving paths on the worker VM.
     * The PathAtom fallback allows custom file provider names (e.g. a`VsoGitRoot`).
     */
    @@public
    export type KnownPrefix = "WorkingDirectory" | "SharedWorkingDirectory" | "LoggingDirectory" | "BuildRoot" | "DataRoot" | "VSODrop" | PathAtom;

    /**
     * A path on the CloudTest worker VM with an explicit macro prefix.
     * Resolves to [prefix]/path in the CloudTest configuration.
     */
    @@public
    export interface PrefixedPath {
        /** CloudTest macro prefix — e.g. a`WorkingDirectory`, a`BuildRoot`, or a custom a`VsoGitRoot`. */
        prefix: KnownPrefix;
        /** Relative path within the prefixed directory. */
        path: RelativePath;
    }

    /**
     * A path reference for CloudTest worker VMs.
     * - Path: an absolute path, passed as-is (e.g. for UNC paths to drops or build outputs).
     * - RelativePath: relative to [WorkingDirectory] on the worker VM.
     * - PrefixedPath: relative to the specified CloudTest macro prefix.
     */
    @@public
    export type CloudTestPath = Path | RelativePath | PrefixedPath;

    /**
     * A copy entry for build/data files. Specifies source and destination for file
     * copying operations on the worker VM.
     */
    @@public
    export interface CopyEntry {
        /** Source path on the worker VM. */
        source: CloudTestPath;
        /** Destination path on the worker VM. */
        destination: CloudTestPath;
        /** Whether to copy recursively. Default: true. */
        isRecursive?: boolean;
        /** Whether zero matched files is allowed. Default: true. */
        isZeroCopiedFilesAllowed?: boolean;
    }

    /**
     * A script entry for setup or cleanup phases on the worker VM.
     */
    /**
     * A CloudTest argument: a primitive value, or a compound value (from Cmd.join) with a separator.
     * Compatible with Transformer's ArgumentValue, so it can be used with Cmd.option directly.
     */
    @@public
    export type CloudTestArgument = PrimitiveValue | CompoundPrimitiveValue;

    /**
     * A compound value: an array of primitive values joined by a separator (default: space).
     */
    @@public
    export type CompoundPrimitiveValue = {
        values: CloudTestArgument[];
        separator?: string;
    };

    /**
     * A script to run on the worker VM during group setup or cleanup.
     */
    @@public
    export interface ScriptEntry {
        /** Path to the script on the worker VM. */
        path: CloudTestPath;
        /** Arguments to pass to the script. Primitives are converted to strings; compound values are joined by their separator (default: space). */
        args?: CloudTestArgument;
        /** Display name for the script. */
        scriptName?: string;
        /** Per-script timeout in minutes. 0 means no per-script timeout. */
        timeoutMins?: number;
    }

    /**
     * A service to deploy on worker VMs during group setup.
     */
    @@public
    export interface ServiceEntry {
        /** Path to the service on the worker VM. */
        path: CloudTestPath;
        /** Whether to skip hashing the input for caching purposes. Default: false. */
        skipHashInput?: boolean;
    }

    /**
     * Configuration for group-level setup operations. Runs before any jobs in the group execute.
     * Can copy files, deploy services, and run setup scripts on the worker VM.
     */
    @@public
    export interface GroupSetup {
        /** Files to copy for build setup. */
        buildFiles?: CopyEntry[];
        /** Files to copy for data setup. */
        dataFiles?: CopyEntry[];
        /** Services to deploy. */
        services?: ServiceEntry[];
        /** Scripts to run during setup. */
        scripts?: ScriptEntry[];
        /** Timeout for the entire setup phase in minutes. */
        timeoutMins?: number;
    }

    /**
     * Configuration for group-level cleanup operations. Runs after all jobs in the group complete.
     */
    @@public
    export interface GroupCleanup {
        /** Scripts to run during cleanup. */
        scripts?: ScriptEntry[];
        /** Timeout for the entire cleanup phase in minutes. */
        timeoutMins?: number;
    }

    /**
     * A key-value property for a file provider definition.
     */
    @@public
    export interface ProviderProperty {
        /** Property name (e.g. "CloudTest.ProviderCustomName", "BaseUrl"). */
        name: string;
        /** Property value. */
        value: string;
    }

    /**
     * A file provider definition that tells CloudTest how to access external file stores
     * (e.g., VsoGit, VsoDrop, PipelineArtifacts).
     */
    /**
     * Known CloudTest file provider types.
     * The string fallback allows custom/future provider types.
     */
    @@public
    export type FileProviderType = "VsoDrop" | "VsoChunkedDrop" | "CloudBuild" | "CloudVault" | "PipelineArtifacts" | "LocalDrop" | "ShareDrop" | "BuildDataDrop" | "DataDrop";

    /**
     * A file provider definition for a CloudTest DJE session.
     */
    @@public
    export interface FileProvider {
        /** Provider type. */
        type: FileProviderType;
        /** Key-value properties for the provider. */
        properties: ProviderProperty[];
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
        /** Max concurrent jobs per VM. Default: 1. */
        maxParallelismForJobs?: number;
        /** Job definitions. Each can be a name (string) for auto-generated ID, or {id, name} to use a specific ID. */
        jobs: (string | JobWithId)[];
        /** Session display name. */
        displayName?: string;
        /** Submitting user alias. */
        user?: string;
        /** CloudTest stamp (e.g. "wus2-default"). */
        stamp?: string;
        /** Session properties as key-value pairs. */
        properties?: Map<string, string>;
        /** Feature flags (e.g. ["EnableTCDForDynamicJobs"]). */
        featureExceptions?: string[];
        /** Enable job result caching. Default: false. */
        cacheEnabled?: boolean;
        /** Group setup configuration. Runs before jobs execute on worker VMs. */
        dynamicGroupSetup?: GroupSetup;
        /** Group cleanup configuration. Runs after jobs complete on worker VMs. */
        dynamicGroupCleanup?: GroupCleanup;
        /** File provider definitions for external file stores (e.g., VsoGit, VsoDrop). */
        fileProviders?: FileProvider[];
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
        jobArguments?: CloudTestArgument;
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

        // Generate JSON files for optional complex structures (passed directly, resolved on C# side)
        // Encoding this as arguments to the tool is too complicated/awkward.
        // Json.write defaults to single quotes, but the C# System.Text.Json parser requires double quotes.
        const setupFile = args.dynamicGroupSetup !== undefined
            ? Json.write(p`${outDir}/dynamic-group-setup.json`, args.dynamicGroupSetup, "\"")
            : undefined;
        const cleanupFile = args.dynamicGroupCleanup !== undefined
            ? Json.write(p`${outDir}/dynamic-group-cleanup.json`, args.dynamicGroupCleanup, "\"")
            : undefined;
        // Json.write does not handle top-level arrays properly, so we wrap them in an object.
        const fileProvidersFile = args.fileProviders !== undefined
            ? Json.write(p`${outDir}/file-providers.json`, { fileProviders: args.fileProviders }, "\"")
            : undefined;

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
            Cmd.option("/maxParallelismForJobs:", args.maxParallelismForJobs),
            Cmd.option("/stamp:", args.stamp),
            Cmd.option("/properties:", args.properties !== undefined && args.properties.count() > 0
                ? args.properties.forEach(kvp => `${kvp[0]}=${kvp[1]}`).join(";")
                : undefined),
            Cmd.option("/featureExceptions:", args.featureExceptions !== undefined && args.featureExceptions.length > 0
                ? args.featureExceptions.join(",")
                : undefined),
            Cmd.option("/dynamicGroupSetupFile:", Artifact.input(setupFile)),
            Cmd.option("/dynamicGroupCleanupFile:", Artifact.input(cleanupFile)),
            Cmd.option("/fileProvidersFile:", Artifact.input(fileProvidersFile)),
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
            environment: args.environment
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
            Cmd.option("/jobArguments:", toArgumentValue(args.jobArguments)),
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
     * Filter to apply to job inputs when submitting a job. Only files matching the contentFilter will be included in the job inputs passed to CloudTest. 
     */
    @@public
    export interface InputFilter {
        /** File path regex pattern that specifies which files should be processed.  */
        contentFilter: string;
        /** Whether to apply the content filter to the relative path of the files (relative to the root of the directory). Default: false. */
        applyContentFilterToRelativePath?: boolean;
    }

    /**
     * An input artifact with a relative path, so the location of the file - or the root of the directory- can be specified relative to the job-specific folder.
     */
    @@public
    export interface InputWithRelativePath {
        /** The input artifact. */
        input: File | OpaqueDirectory;
        /** The relative path of the input artifact (relative to the job-specific folder). */
        relativePath: RelativePath;
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
        /** 
         * Input artifacts for the job. Only files and opaque directories are supported. 
         * Each test job has a unique folder where all it inputs should be placed. The relative paths of the inputs (relative to the job folder) can be preserved by using the InputWithRelativePath interface.
         * When plain files or directories are used, files and directories are placed directly under the job folder.
         * */
        jobInputs: (File | OpaqueDirectory | InputWithRelativePath)[];
        /** Filter to apply to the job inputs. */
        jobInputsFilter?: InputFilter;
        /** Overrides the per-test unique location where the job inputs will be placed in the drop to use the provided value. */
        jobInputsDropLocation?: PathAtom;
        /** Path to executable on the worker VM. */
        jobExecutable: Path | RelativePath;
        /** Test framework: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest. */
        testExecutionType: TestExecutionType;
        /** Arguments passed to the executable. */
        jobArguments?: CloudTestArgument;
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
        
        const jobInputsLocation = args.jobInputsDropLocation !== undefined 
            ? args.jobInputsDropLocation 
            : PathAtom.createSanitized(args.jobName);
        
        let artifactInfos = args.jobInputs.map(input => {
            
            // All inputs should go into a test job specific folder. We use the job name as the folder name.
            let artifact : File | OpaqueDirectory =  undefined;
            let relativePath : RelativePath = undefined;
            // If the input is a static directory or a file, we place the artifact directly under the job folder.
            if (Transformer.isStaticDirectory(input) || Transformer.isFile(input)) {
                artifact = input;
                relativePath = r`${jobInputsLocation}`;
            }
            // If the input is an InputWithRelativePath, we honor the specified relative path.
            else {
                artifact = input.input;
                relativePath = r`${jobInputsLocation}/${input.relativePath}`;
            }

            if (Transformer.isStaticDirectory(artifact)) {
                const info : Drop.DropDirectoryInfo = {
                    kind: "directory",
                    directory: artifact,
                    dropPath: relativePath,
                    contentFilter: args.jobInputsFilter && args.jobInputsFilter.contentFilter,
                    applyContentFilterToRelativePath: args.jobInputsFilter && args.jobInputsFilter.applyContentFilterToRelativePath
                };
                return info;
            } else {
                const info : Drop.DropFileInfo = {
                    kind: "file",
                    file: artifact,
                    dropPath: relativePath
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

        let inputArtifacts : (File | OpaqueDirectory)[] = artifactInfos.map(info => 
            info.kind === "file" 
                ? (<Drop.DropFileInfo>info).file 
                : <OpaqueDirectory>(<Drop.DropDirectoryInfo>info).directory);

        const dynamicJobConfigArguments : DynamicJobConfigArguments = {
            image: args.configAndSessionResult.configArguments.image,
            sku: args.configAndSessionResult.configArguments.sku,
            sessionId: args.configAndSessionResult.sessionResult.sessionIdFile,
            jobReference: {jobName: args.jobName, sessionConfigFile: args.configAndSessionResult.configResult.configFile}, 
            testFolder: r`${jobInputsLocation}`,
            jobExecutable: args.jobExecutable,
            testExecutionType: args.testExecutionType,
            jobArguments: args.jobArguments,
            testParserType: args.testParserType,
            jobTimeout: args.jobTimeout,
            testCaseTimeout: args.testCaseTimeout,
            // We always provide the hashes for the job inputs since CloudTest will try to store the results in the cache regardless of whether
            // cacheEnabled was set to true or false when creating the session (the flag is just honored for cache lookups)
            testDependencyHashes:  Artifact.vsoHashes(inputArtifacts),
            priority: args.priority,
            tags: args.tags,
            description: args.description,
        };

        const configResult = generateUpdateDynamicJobConfig(dynamicJobConfigArguments);

        let updateDynamicJob : APIs.UpdateDynamicJobArguments = {
            sessionId: args.configAndSessionResult.sessionResult.sessionIdFile,
            bodyFile: configResult.configFile,
            dependencies: [...inputArtifacts, configResult.configFile, ...addArtifactsResult.outputs],
            tokenEnvVar: args.configAndSessionResult.configArguments.tokenEnvVar,
            timeoutMinutes: args.timeoutMinutes,
            environmentVariables: args.environmentVariables,
            tags: args.tags,
            description: args.description,
            tenant: args.configAndSessionResult.configArguments.tenant,
            environment: args.configAndSessionResult.configArguments.environment
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
            environment: args.configAndSessionResult.configArguments.environment
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

    function isCompoundPrimitiveValue(arg: CloudTestArgument): arg is CompoundPrimitiveValue {
        return typeof(arg) !== "string" && typeof(arg) !== "number" && (<CompoundPrimitiveValue>arg).values !== undefined;
    }

    /** Converts a CloudTestArgument to an ArgumentValue compatible with Cmd.option. */
    function toArgumentValue(arg: CloudTestArgument): ArgumentValue {
        if (arg === undefined) {
            return undefined;
        }

        if (!isCompoundPrimitiveValue(arg)) {
            return arg;
        }

        const compound = <CompoundPrimitiveValue>arg;
        return <CompoundArgumentValue>{
            values: compound.values.map(v => toArgumentValue(v)),
            separator: compound.separator || " ",
        };
    }

    function getDropService(drop: APIs.DropToCreate | Drop.DropCreateResult) : string {
        if (APIs.isDropResult(drop)) {
            return drop.dropConfig.service;
        } else {
            return drop.service;
        }
    }
}


