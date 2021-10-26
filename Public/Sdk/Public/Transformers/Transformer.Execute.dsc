// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** Schedules a new pip, according to given arguments. */
    @@public
    export function execute(args: ExecuteArguments): ExecuteResult {
        return <ExecuteResult>_PreludeAmbientHack_Transformer.execute(args);
    }

    @@public
    export interface ExecuteArguments extends ExecuteArgumentsCommon {
        /** Regular process pips that make calls to one or more service 
          * pips should use this field to declare those dependencies
          * (so that they don't get scheduled for execution before all
          * the services have started). */
        servicePipDependencies?: ServiceId[];

        /** Whether to grant the read/write permissions of this pip to 
          * the declared service pips (permissions are granted only 
          * throughout the lifetime of this pip). */
        delegatePermissionsToServicePips?: PermissionDelegationMode;
    }
    
    /** Different options for delegating permissions of a process to a service pip. */
    @@public
    export const enum PermissionDelegationMode { 
        /** Don't grant any permissions at all. */
        none,

        /** Grant permissions only throughout the lifetime of the caller pip. */
        temporary,

        /** Grant permissions permanently, i.e., until the service pip terminates. */
        permanent
    }

    @@public
    export interface ExecuteArgumentsCommon extends ExecuteArgumentsComposible {
        /** Command-line arguments. */
        arguments: Argument[];

        /** Working directory. */
        workingDirectory: Directory;
    }

    // Use this interface when 'composing' tools to not have callers require passing in arguments and working directory
    @@public
    export interface ExecuteArgumentsComposible extends RunnerArguments {
        /** Command-line arguments. */
        arguments?: Argument[];

        /** Working directory. */
        workingDirectory?: Directory;

        /** Tools dependencies. */
        dependencies?: InputArtifact[];

        /** Implicit outputs. */
        // TODO: Uncomment this out once we move the selfhost to not use this field
        // @@obsolete("Please use 'Outputs' instead")
        implicitOutputs?: OutputArtifact[];

        /** Optional (or temporary) implicit outputs. */
        // TODO: Uncomment this out once we move the selfhost to not use this field
        // @@obsolete("Please use 'Outputs' instead")
        optionalImplicitOutputs?: OutputArtifact[];

        /** Tool outputs */
        outputs?: Output[];

        /** Console input. */
        consoleInput?: File | Data;

        /** Redirect console output to file. */
        consoleOutput?: Path;

        /** Specifies the standard error file to use for the process. */
        consoleError?: Path;

        /** Environment variables. */
        environmentVariables?: EnvironmentVariable[];

        /** Regex that would be used to extract warnings from the output. */
        warningRegex?: string;

        /** Regex that would be used to extract errors from the output. */
        errorRegex?: string;

        /** 
         * When false (or not set): process output is scanned for error messages line by line;
         * 'errorRegex' is applied to each line and if ANY match is found the ENTIRE line is reported.
         *
         * When true: process output is scanned in chunks of up to 10000 lines; 'errorRegex' is applied to
         * each chunk and only the matches are reported.  Furthermore, if 'errorRegex' contains a capture 
         * group named "ErrorMessage", the value of that group is reported; otherwise, the value of the
         * entire match is reported.
         * 
         *   NOTE: because this scanning is done against chunks of text (instead of the entire process output),
         *         false negatives are possible if an error message spans across multiple chunks.  The scanning
         *         is done in chunks because attempting to construct a single string from the entire process
         *         output can easily lead to OutOfMemory exception.
         * 
         * Default: false
         */
        enableMultiLineErrorScanning?: boolean;

        /** Semaphores to acquire */
        acquireSemaphores?: SemaphoreInfo[];

        /** Mutexes to acquire */
        acquireMutexes?: string[];

        /** A custom set of success exit codes. Any other exit code would indicate failure. If unspecified, by default, 0 is the only successful exit code. */
        successExitCodes?: number[];

        /** A custom set of exit codes that causes pip to be retried by BuildXL. If an exit code is also in the successExitCode, then the pip is not retried on exiting with that exit code. */
        retryExitCodes?: number[];

        /** If a pip runs and returns ones of these exit codes, then downstream pips are skipped. */
        succeedFastExitCodes?: number[];

        /**
         * Maximum number of times BuildXL will retry the pip when it returns an exit code in 'retryExitCodes'
         * If not specified, the global configuration for it takes effect.
         * If no retry exit codes are specified, this argument is ignored.
         */
        processRetries?: number;

        /**
         * The name of the environment variable BuildXL will use to communicate the number of times the pip has been retried so far.
         * When defined, the first time the pip is executed the value of this environment variable will be 0. If a retry happens by virtue of 'retryExitCodes',
         * the variable will have value 1, and so on for subsequent retries.
         * This variable will automatically become a passthrough one and will have no effects on caching.
         */
        retryAttemptEnvironmentVariable?: string;

        /** Temporary directory for the tool to use (use Context.getTempDirectory() to obtain one), and set TEMP and TMP. */
        tempDirectory?: Directory;

        /** Additional temporary directories, but none set TEMP or TMP. */
        additionalTempDirectories?: Directory[];

        /** Unsafe arguments */
        unsafe?: UnsafeExecuteArguments;

        /** Whether to mark this process as "light". */
        isLight?: boolean;

        /** Set outputs to remain writable */
        keepOutputsWritable?: boolean;

        /** Privilege level required by this process to execute. */
        privilegeLevel?: "standard" | "admin";

        /** Whether this process should run in an isolated container (i.e. filesystem isolation)
         * When running in a container, the isolation level can be controlled by 'containerIsolationLevel' field.
         * Note: this is an experimental feature for now, use at your own risk 
         * Default is globally controlled by the sandbox configuration
         */
        runInContainer?: boolean;

        /**
         * Configures which inputs and outputs of this process should be isolated when the process runs in a container.
         * Default is globally controlled by the sandbox configuration
         * TODO: input isolation is not implemented
         */
        containerIsolationLevel?: ContainerIsolationLevel;

        /**
         * The policy to apply when a double write occurs.
         * Default is globally controlled by the sandbox configuration
         */
        doubleWritePolicy?: DoubleWritePolicy;

        /**
         * The policy to apply when a source is rewritten.
         * Default is globally controlled by the sandbox configuration
         */
        sourceRewritePolicy?: SourceRewritePolicy;

        /** Whether this process should allow undeclared reads from source files. A source 
         * file is considered to be a file that is not written during the build.
         * Note: this option turns static enforcements based on source file declarations into dynamic
         * ones. The downside is that potential build errors will be reported later in time, during the
         * execution phase, and only based on runtime observations. This means that even if there is something
         * wrong, BuildXL may not see it if the running build doesn't hit it. The advise is: statically declare all 
         * sources if possible, and only use this option for the sources where static predictions are not available. */
        allowUndeclaredSourceReads?: boolean;

        /** The process names, e.g. "mspdbsrv.exe", allowed to be cleaned up by a process pip sandbox job object
         * without throwing a build error DX0041. */
        allowedSurvivingChildProcessNames?: (PathAtom | string)[];

        /** The timeout in milliseconds that the execution sandbox waits for child processes
         * started by the top-level process to exit after the top-level process exits.
         * Defaults to 30000 (30 seconds). */
        nestedProcessTerminationTimeoutMs?: number;

        /** Determines how to treat absent path probes in opaque directories that the process does not depend on. */
        absentPathProbeInUndeclaredOpaquesMode? : AbsentPathProbeInUndeclaredOpaquesMode;

        /** Whether cache lookups should be disabled for this process.
         * Typically this should always be the default value of false, which will enable caching for the process.
         * But there are a few exceptional circumstances where it may be desireable to completely disable cache lookups
         * for a pip. The pip will still be stored to the cache for sake of supporting distribution.
         */
        disableCacheLookup?: boolean;

        /** 
          * True if the executable depends on directories that comprise the current host OS. 
          * For instance on windows this signals that accesses to the Windows Directories %WINDIR% should be allowed.
          * This is the same as the field on ToolDefinition.dependsOnCurrentHostOSDirectories
          */
        dependsOnCurrentHostOSDirectories?: boolean;

        /**
         * The # of process slots this process requires when limiting concurrency of process pips.
         * The total weight of all proceses running concurrently must be less than or equal to the # of available process slots.
         * The # of available process slots is typically a function of the number of cores on the machine, but can also be limited by runtime resource exhaustion or be set per-build by configuration.
         * Valid input range for the weight is [min Int32, max Int32] though all values will be effectively coerced to fit within [1, # of process slots]
         * If a given weight is greater than or equal to # of available process slots, the process will run alone.
         */
        weight?: number;

        /**
         * The priority of this process.  Processes with a higher priority will be run first.
         * Minimum value is 0, maximum value is 99
         */
        priority?: number;

        /**
         * File to write the change affected input list of the pip before it execute.
         */
        changeAffectedInputListWrittenFile?: Path;

        /**
         * When this option is set to true bxl cannot cancel the pip due to correctness reasons.
         * Defaults to false. 
         */
        uncancellable?: boolean;

        /**
         * Defines a collection of directory scopes that will be excluded from being part of opaque directory outputs
         * Any artifact produced under these directories won't be considered part of any opaque (shared or exclusive) directory
         * produced by this process
         */
        outputDirectoryExclusions?: Directory[];

        /**
         * When set, the execution of a process is considered to have failed if the process writes to standard error, regardless
         * of the exit code.
         * Defaults to false.
         */
        writingToStandardErrorFailsExecution?: boolean;

         /**
         * When set, the serialized path set of this process is not normalized wrt casing
         * This is already the behavior when running in a non-Windows OS, therefore this option only has a effect on Windows systems.
         * Setting this option increases the chance BuildXL will preserve path casing on Windows, at the cost of less efficient
         * caching, where the same weak fingerprint may have different path sets that only differ in casing.
         * Defaults to false.
         */
        preservePathSetCasing?: boolean;

        /**
         * When set, weak fingerprint augmentation is enforced when performing cache lookup.
         */
        enforceWeakFingerprintAugmentation?: boolean;
    }

    @@public
    export type ExecuteResult = TransformerExecuteResult;

    @@public
    export type EnvironmentValueType = string | boolean | number | Path | Path[] | File | File[] | Directory | Directory[] | StaticDirectory | StaticDirectory[];

    @@public
    export interface EnvironmentVariable {
        name: string;
        value: EnvironmentValueType;
        separator?: string;
    }

    @@public
    export const enum ExitCodeSuccessCriteria {
        zeroIsSuccess = 1,
        zeroOr255IsSuccess,
    }
    
    @@public
    export interface UnsafeExecuteArguments {
        untrackedPaths?: (File | Directory)[];
        untrackedScopes?: Directory[];
        hasUntrackedChildProcesses?: boolean;
        allowPreservedOutputs?: boolean | number;
        passThroughEnvironmentVariables?: string[];
        
        /** 
         * File/directory output paths that are preserved.
         * If the list is empty, all file and directory outputs are preserved. 
         * If the list is not empty, only given paths are preserved and the rest is deleted
         */
		preserveOutputAllowlist?: (File | Directory)[];
        preserveOutputWhitelist?: (File | Directory)[]; // compatibility
        incrementalTool?: boolean;

        /**
         * Pull unsafe_GlobalPassthroughEnvVars and unsafe_GlobalUntrackedScopes for this process.
         */
        requireGlobalDependencies?: boolean;

        /**
         * Process names that will break away from the sandbox when spawned by the main process
         * The accesses of processes that break away from the sandbox won't be observed.
         * Processes that breakaway can survive the lifespan of the sandbox.
         * Only add to this list processes that are trusted and whose accesses can be safely predicted
         * by some other means.
         */
        childProcessesToBreakawayFromSandbox?: PathAtom[];

        /**
         * This option makes all statically declared artifacts on this process (inputs and outputs) to be automatically
         * added to the sandbox access report, as if the process actually produced those accesses.
         * Useful for automatically augmenting the sandbox access report on trusted process breakaway.
         * Default is false. 
         * Should only be used on trusted process, where the statically declared inputs and outputs are guaranteed to
         * match the process actual behavior.
         * Only takes effect if 'childProcessesToBreakawayFromSandbox' is non-empty. Otherwise it is ignored.
         * This option is only supported for pips with no output directories nor source sealed dependencies.
         */
        trustStaticallyDeclaredAccesses?: boolean;

        /**
         * Disables reparse point resolution for this particular execution.
         * The flag has no effect if reparse point resolution is globally disabled already.
         */
        disableFullReparsePointResolving?: boolean;
    }

    /**
     * Data for a declared semaphore
     */
    @@public
    export interface SemaphoreInfo {
        /** The maximum value */
        limit: number;

        /** The resource name */
        name: string;

        /** The semaphore value */
        incrementBy: number;
    }

    @@public
    export type AbsentPathProbeInUndeclaredOpaquesMode = "unsafe" | "strict" | "relaxed";
}
