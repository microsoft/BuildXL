# BuildXL Flags

This page lists flags that can be used to configure BuildXL.

| Name | Value |
| ---- | ----- |
| AdditionalConfigFile | Additional configuration files that can contain module definitions (short form: /ac) |
| AdminRequiredProcessExecutionMode | Mode for running processes that required admin privilege. Allowed values are 'Internal' (BuildXL starts the process as an immediate child process), 'ExternalTool' (BuildXL starts a sandboxed process executor tool as a child process, and in turn the tool starts the admin-required process as its child process), 'ExternalVM' (BuildXL sends command to VM to execute a sandboxed process executor tool in VM, and in turn the tool starts the admin-required process as its child process) . For Internal and ExternalTool, the process will be run with the same elevation level as BuildXL. For ExternalVM, the process will be run with the elevation level in the VM. Defaults to 'Internal'. |
| AdoConsoleMaxIssuesToLog | Specifies the maximum number of issues(errors and warnings) in the ADO console. |
| AllowCopySymlink | When enabled, allow copying symlink. Defaults to true. |
| AllowDuplicateTemporaryDirectory | When enabled, pips with duplicate temporary directories are allowed. Defaults to false. |
| AllowFetchingCachedGraphFromContentCache | Allow fetching cached graph from content cache. Defaults to on. |
| AllowInternalDetoursErrorNotificationFile | When enabled, the detoured processes will store internal errors in a special file. If this file contains data at the end of pip execution, the pip (build) will fail. Defaults to on. |
| AllowMissingOutputs | When specified, suppresses errors for specified-but-not-produced outputs with the given name. May be specified more than once. Using this option results in an unsafe configuration (for diagnostic purposes only). Defaults to false. |
| AllowMissingSpecs | When enabled, missing module and spec files will be logged as verbose events rather than errors during workspace generation. |
| AlwaysRemoteInjectDetoursFrom32BitProcess | Always use remote detours injection when launching processes from a 32-bit process. Defaults to true. |
| AnalyzeDependencyViolations | When enabled, file monitoring violations will be analyzed to emit additional warnings about high-level dependency problems (examples include double-writes and read-write races). Defaults to on. |
| AssumeCleanOutputs | When enabled, BuildXL assumes there are no stale outputs from previous builds. |
| AugmentingPathSetCommonalityFactor | Used to compute the number of times (i.e. {augmentingPathSetCommonalityFactor} * {pathSetThreshold}) an entry must appear among paths in the observed path set in order to be included in the common path set. Value must be (0, 1] |
| BreakOnUnexpectedFileAccess | Break into the debugger when {ShortProductName} detects that a tool accesses a file that was not declared in the specification dependencies. This option is useful when developing a new tool or SDKs using these tools. Defaults to off. |
| BuildLockPolling | Number of seconds to wait for an executing build to finish before polling again for completion. Defaults to 15 sec. |
| BuildManifestVerifyFileContentOnHashComputation | When enabled, ensures that file's content matches the hash provided by the engine before proceeding to compute a build manifest hash for that file. |
| BuildWaitTimeout | Number minutes to wait for an executing build to finish before failing the current one. Defaults to 0 minute. |
| CacheConfigFilePath | Path to cache config file. |
| CacheDirectory | Specifies the root directory for the incremental artifact cache (short form: /cd) |
| CacheGraph | Caches the build graph between runs, avoiding the parse and evaluation phases when no graph inputs have changed. Defaults to on. |
| CacheMiss | When enabled, {ShortProductName} performs the on-the-fly cache miss analysis during the execute phase. If no value is given (/cacheMiss+),  the local FingerprintStore is used for comparison. The user can pass the list of changesets if they want to compare the build to a remote FingerprintStore: /cacheMiss:[<sha>:<sha>:...]. Defaults to off. |
| CacheMissDiffFormat | Diff format for cache miss analysis. Allowed values are CustomJsonDiff and JsonPatchDiff. Defaults to CustomJsonDiff |
| CacheOnly | Only processes cache hits. Any pips that are cache misses will skip execution. Skipped pips will not cause the build session to fail. |
| CacheSessionName | If specified, this is the new unique cache session name for this build.  If not given, the build cache session will not be tracked. |
| CacheSpecs | Caches build specification files to a single large file to improve read performance on spinning disks. When unset the behavior is dynamic based on whether root configuration file is on a drive that is detected to have a seek penalty. May be forced on or off using the flag. |
| CanonicalizeFilterOutputs | Canonicalize filter for output filtering. Defaults to on. |
| Channel | Identifies a communication channel. If not specified, the default channel name is derived from the main configuration file. Use together with /listen. |
| CheckDetoursMessageCount | When enabled, {ShortProductName} will check the count of messages sent to {ShortProductName} from the pip process tree and will fail the pip (build) if there is mismatch. Defaults to on. |
| CleanOnly | Deletes output files that would have been produced by the build. Pips will not be executed. |
| CleanTempDirectories | Cleans per pip temp directories after the pip successfully exits to save disk space. Defaults to on. |
| Color | Use colors for warnings and errors. Defaults to using colors. |
| CompressGraphFiles | When enabled, graph files are compressed. |
| ConfigFile | Configuration file that determines {ShortProductName}'s behavior (short form: /c) |
| ConsoleVerbosity | Sets the console logging verbosity. Allowed values are 'Off', 'Error', 'Warning', 'Informational' and 'Verbose', and the single-character prefixes of those values. Defaults to Informational. (short form: /cv) |
| CustomLog | Sets a custom log file for a specific set of event IDs. Event list should be comma separated integers excluding the DX prefix. |
| DebugIgnoreChangeJournal | If disabled, {ShortProductName} will not use the NTFS / ReFS change journal for caching or incremental builds. This is an unsafe configuration (for diagnostic purposes only). Defaults to on. |
| DependencySelection | Specifies additional pips to run based on dependency information of pips matched in filter. May be: empty (all dependencies) or "+" (dependencies and dependents). |
| DeterminismProbeUsage | DeterminismProbe feature was removed, this option will do nothing |
| Diagnostic | Enables diagnostic logging for a functional area. This option may be specified multiple times. Areas: Scheduler, Parser, Storage, Transformers, Engine, Viewer, PipExecutor, PipInputAssertions, ChangeJournalService, HostApplication, CommonInfrastructure, CacheInteraction, HybridInterop. (short form: /diag) |
| DisableConHostSharing | Disables sharing of the Windows ConHost process between pips. Defaults to sharing enabled. |
| DisableCycleDetection | Disables cycle detection during evaluation. Defaults to off. |
| DisableDetours | When enabled, {ShortProductName} will not detour any processes. This might lead to incorrect builds because any file accesses will not be enforced. |
| DisableGraphPostValidation | Disables post validation of graph construction. Defaults to on. |
| DiskSpaceForPip | Specify the required minimum available disk space in Gigabytes. Default value set to 0GB (disabled). Pips will fail if the specified amount of disk space in unavailable. |
| DistributedBuildOrchestratorLocation | Specifies the IP address or host name and TCP port of the orchestrator machine to which a worker will connect to join a build session.  This argument is redundant if the orchestratro is invoked with /distributedBuildWorker specified for this worker. (short form: /dbo) |
| DistributedBuildRole | Specifies the role the node plays in the distributed build: None, Orchestrator, or Worker. This argument is required for executing a distributed build. (short form: /dbr) |
| DistributedBuildServicePort | Specifies the TCP port of a locally running distributed build service (orchestrator or worker) which peers can connect to during a distributed build. This argument is required for executing a distributed build.  (short form: /dbsp) |
| DistributedBuildWorker | Specifies the IP address or host name and TCP port of remote worker build services which this process can dispatch work to during a distributed build (can specify multiple). This argument is redundant if the corresponding worker is invoked with /distributedBuildOrchestratorLocation specified. (short form: /dbw) |
| DistributedBuildWorkerSourceMaterialization | Enables materialization of source files on distributed workers. NOTE: Source files are required to be present in the worker's remote or local cache. |
| DumpFailedPips | When enabled, the runtime dump pip lite analyzer will log information regarding failed pips under Out/Logs/FailedPips for debugging. |
| DumpFailedPipsLogLimit | Sets the maximum number of log files that are allowed to be generated by the dump pip lite analyzer (default 50). |
| DumpFailedPipsWithDynamicData | Enable this option to dump observed file accesses and processes with the dump pip lite analyzer (requires /logObservedFileAccesses+ and/or /logProcesses+ to be set as well). |
| EmitSpotlightIndexingWarning | (MacOS only) Controls if the engine should emit a warning that indicates that artifact folders are indexed by Spotlight. This flag is on by default. |
| EnableDedupChunk | When enabled, DedupChunk hashing algorithm is used instead of VSO0. Defaults to off. |
| EnableHistoricCommitMemoryProjection | Controls whether Scheduler should use commit memory projection based on historic commit memory usage. |
| EnableIncrementalFrontEnd | Enables incremental spec analysis based on number of changed specs. Defaults to on. |
| EnableLazyOutputs | Enables lazy materialization (deployment) of pips' outputs from local cache. Defaults to on. |
| EnableLinuxPTraceSandbox | Enables the ptrace sandbox on Linux when a statically linked binary is detected. Note that this will have a negative impact on performance, but is necessary to ensure correctness on some Linux builds. |
| EnablePlugins | When enabled, plugins are allowed to be loaded. Defaults to off. |
| EnableProcessRemoting | Enable process remoting via AnyBuild. Defaults to off. |
| EnforceAccessPoliciesOnDirectoryCreation | Indicates whether {ShortProductName} should enforce access policies on CreateDirectory for paths under writable mounts as well as the cases when the directory already exists. Defaults to off. |
| EnforceFileAccesses | Whether {ShortProductName} is to monitor file accesses of individual tools at all. Disabling monitoring results in an unsafe configuration (for diagnostic purposes only). Defaults to on. |
| EnforceFullReparsePointsUnderPath | Enforce that files accessed which begin with the given path will enforce reparse points underneath said path. All transitive reparse points encountered after enforcing and resolving the first one are also enforced, regardless of path. |
| EngineCacheDirectory | Allows overriding where engine state will be cached. If unset, it will be stored in a subdirectory of the artifact cache. |
| Environment | Environment build is running in. Allowed values '{0}'. |
| ExistingDirectoryProbesAsEnumerations | When enabled, {ShortProductName} will report existing directory probes as enumerations. This might lead to cases where pips will be executed even when there is no need for it. |
| ExitOnNewGraph | When enabled, exit early if a new graph needs to be created.  This differs from phase:schedule because it doesn't actually create the graph. |
| Experimental__0 | Enables an experimental feature (short form: /exp). Available experimental features: {0} |
| ExplicitlyReportDirectoryProbes | When enabled, detours will explicitly report directory probes. Note that this may result in an increased amount of DFAs. |
| ExportGraph | Specifies the optional file path to export a JSON-formatted graph of pips. |
| FancyConsole | When enabled, the console will give frequent updates of the status of processes that are running. Defaults to off. |
| FancyConsoleMaxStatusPips | Maximum number of concurrently executing pips to render in Fancy Console view. Defaults to 5. |
| FileChangeTrackerInitializationMode | Modes for initializing file change tracker. Allowed values are ResumeExisting, ForceRestart. Defaults to ResumeExisting. |
| FileChangeTrackingExclusionRoot | Specifies one or more roots to be excluded from file change tracking. NOTE: This flag is not compatible with /incrementalScheduling which requires all paths to be tracked. |
| FileChangeTrackingInclusionRoot | Specifies one or more roots to be included in file change tracking. NOTE: When specified all paths not under inclusion roots are excluded from file change tracking. This flag is not compatible with /incrementalScheduling which requires all paths to be tracked. |
| FileContentTableEntryTimeToLive | Time-to-live for file content table entries. Min value is 1, max value is 65535. Defaults to 255. |
| FileContentTableFile | Path to file content table to use. Defaults to FileContentTable in engine cache. |
| FileSystemMode | Specifies the type of filesystem rules to use when the sandbox computes input assertions. Allowed values are: 'RealAndMinimalPipGraph' ({ShortScriptName} default) - uses the real filesystem for read only mounts and a minimal pip graph based view for writeable mounts, 'RealAndPipGraph' (XML default) - same as RealAndMinimalPipGraph except a full pip graph is used, 'AlwaysMinimalGraph' - always uses the pip graph. |
| FileVerbosity | Sets the file logging verbosity. Allowed values are 'Off', 'Error', 'Warning', 'Informational' and 'Verbose', and the single-character prefixes of those values. Defaults to Verbose. (short form: /fv) |
| Filter | Specifies a filter expression (short form: /f). See verbose help (/help:verbose) for details about constructing filter expressions. |
| FilteringInfo | You can choose to build only a subset of available pips by using filtering. |
| FingerprintSalt | Salts fingerprints used for caching. May be specified multiple times and values concatenate with delimiter. Empty value clears previous values. '*' is unique each run. |
| FlushPageCacheToFileSystemOnStoringOutputsToCache | Flush page cache to file system on storing outputs to cache. Defaults to off. |
| ForceAddExecutionPermission | When set to true, it enables the execution permission for the root process of process pips in Linux builds. Defaults to true. |
| ForceSkipDependencies | Specifies that dependencies of processes requested in the filter should be skipped as long as all the inputs are present. |
| ForwardWorkerLog | Configure additional verbose event IDs that workers will forward to the orchestrator, in addition to warnings and errors (which are always forwarded).  |
| GenerateCgManifest | Generates a cgmanifest.json file at the specified path. This file contains the names and versions for all Nuget packages used within BuildXL, and is used for Component Governance during CloudBuild. |
| HardExitOnErrorInDetours | When enabled, detours will exit the process on Detours error with a special exit code. Defaults to on. |
| Help | Display this usage message (Short form: /?). See verbose help with /help:verbose. See DX code specific help with /help:1234. |
| HonorDirectoryCasingOnDisk | When true, casing of directories for dynamic outputs will match the ones found on disk when a pip is done executing (as opposed to using the casing of the first time the path is mentioned in the build). Useful on Windows when tools are case sensitive. Defaults to false. |
| IgnoreDynamicWritesOnAbsentProbes | When enabled, {ShortProductName} will not flag as violations absent path probes that coexist with writes under output directories for those same paths. |
| IgnoreFullReparsePointResolving | When enabled, {ShortProductName} will not fully resolve paths containing any sort of reparse point. This might lead to incorrect builds because some file accesses will not be enforced or tracked at all. |
| IgnoreGetFinalPathNameByHandle | When enabled, {ShortProductName} will not intercept GetFinalPathNameByHandle calls. This may lead to failures when using subst because non-subst paths will be used. Defaults to off. |
| IgnoreNonCreateFileReparsePoints | When enabled, {ShortProductName} will not follow symlinks for access validation and reporting for API's outisde of CreateFile, NtCreate, and OpenFile. This might lead to incorrect builds because some file accesses will not be enforced. Defaults to off. |
| IgnoreNonExistentProbes | When enabled, {ShortProductName} will not report non existent probes, that are not in sealed directories. This might lead to incorrect builds because some file accesses will not be enforced and validated. Certain calls to GetFileAttribute method for non existing files will not be reported to {ShortProductName}. |
| IgnoreNtCreateFile | When enabled, {ShortProductName} will not intercept NtCreateFile calls. This might lead to incorrect builds because some file accesses will not be enforced. |
| IgnorePreloadedDlls | When enabled, {ShortProductName} will not report Dlls loaded before Detours was started. This might lead to incorrect builds because some file accesses will not be enforced. |
| IgnoreReparsePoints | When enabled, {ShortProductName} will not track reparse points. This might lead to incorrect builds because some file accesses will not be enforced. Any reparse points (symlinks and mount points) will not be followed. |
| IgnoreSetFileInformationByHandle | When enabled, {ShortProductName} will not detour the SetFileInformationByHandle API. This might lead to incorrect builds because some file accesses will not be enforced. |
| IgnoreZwCreateOpenQueryFamily | When enabled, {ShortProductName} will not intercept ZwCreateFile, ZwOpenFile, and ZwQueryDirectoryFile calls. This might lead to incorrect builds because some file accesses will not be enforced. This flag is off by default. |
| IgnoreZwOtherFileInformation | When enabled, {ShortProductName} will not detour the ZwLinkFileInformation, ZwFileNameFileInformation, ZwDispositionFileInformation, ZwModeFileInformation APIs. This might lead to incorrect builds because some file accesses will not be enforced. Defaults to off - the functions are detoured. |
| IgnoreZwRenameFileInformation | When enabled, {ShortProductName} will not detour the ZwRenameFileInformation API. This might lead to incorrect builds because some file accesses will not be enforced. |
| Incremental | When enabled, artifacts are built incrementally based on which source files have changed. Defaults to on. |
| IncrementalScheduling | When enabled, scheduling is performed incrementally. Defaults to off. |
| InferNonExistenceBasedOnParentPathInRealFileSystem | Infers the non-existence of a path based on the parent path when checking the real file system in file system view. Defaults to on. |
| InjectCacheMisses | Sets a rate for artificial cache misses (pips may be re-run with this likelihood, when otherwise not necessary). Miss rate and options are specified as "[~]Rate[#Seed]". The '~' symbol negates the rate (it becomes an allowed hit rate). The 'Rate' must be a numeric value in the range [0.0, 1.0]. The optional 'Seed' is an integer value fully determining the random aspect of the miss rate (the same seed and miss rate will always pick the same set of pips). |
| InputChanges | Path to file containing a list of input changes, separated by new lines. The formats of an input change is '<path>|<change kind>' or '<path>', where change kinds are 'DataOrMetadataChanged', 'Removed', 'MembershipChanged', 'NewlyPresentAsFile', 'NewlyPresentAsDirectory'. |
| Interactive | When enabled indicates that {ShortProductName} is allowed to interact with the user either via console or popups. A common use case is to allow front ends like nuget to display authentication prompts in case the user is not authenticated. |
| KextNumberOfKextConnections | [macOS] Specifies the number of connections that drain the sandbox kernel extension file access reports on macOS systems - the sandbox kernel extension allocates one report queue per connection to balance the system load when reporting file accesses. |
| KextReportQueueSizeMb | [macOS] Specifies the size in MB of a sandbox kernel extension report queue. Use this cautiously, because it allocates the specified size in wired system memory multiplied by the number of kernel extension connections. |
| KextThrottleCpuUsageBlockThresholdPercent | [macOS] Causes throttling to be triggered whenever CPU usage is above this value. |
| KextThrottleCpuUsageWakeupThresholdPercent | [macOS] A blocked process can be awakened only when CPU usage drops below this value (defaults to /KextThrottleCpuUsageBlockThresholdPercent). |
| KextThrottleMinAvailableRamMB | [macOS] Causes throttling to be triggered whenever available RAM drops below this value. (takes precedence over CPU usage thresholds) |
| LaunchDebugger | Launches the debugger during boot (in the server process if applicable). |
| LimitPathSetsOnCacheLookup | Limits the number of path sets to be checked during cache lookup. Once the limit is reached, the pip is determined to have a cache miss. Defaults to off. The number of path sets can also be set. Defaults to 0 (off). |
| LoadGraph | Loads a cached build graph stored under the given fingerprint (40 digit hex string, no delimiters), path to cached graph directory, or canonical name. |
| LogCatalog | Records the set of spec files added to the catalog. Defaults to off. |
| LogCounters | Collects various performance counters and logs phase specific aggregations. Defaults to on. |
| LogExecution | Logs an execution trace to the default trace file in the same folder as the main log file. Defaults to on. |
| LogFileEnforcementTables | Records the file enforcement access tables for individual pips to the log. Defaults to off. |
| LogMemory | Collects actual memory usage when collection performance counters. This has a negaitve performance impact and should only be used when analyzing memory consumption. Defaults to off. |
| LogObservedAccesses | Records the files observed to be accessed by individual pips to the log. Defaults to off. |
| LogOutput | Specifies how process standard error and standard output should be reported. Allowed values are 'TruncatedOutputOnError', 'FullOutputAlways', 'FullOutputOnError', 'FullOutputOnWarningOrError'. Default is 'TruncatedOutputOnError'. |
| LogPipStaticFingerprintTexts | Log pip static fingerprint texts. Defaults to off. |
| LogPrefix | The prefix to add to all log file names (default: {ShortProductName}) |
| LogProcessData | When enabled, records process execution times and IO counts and transfers. Requires /LogProcesses to be enabled. Defaults to off. |
| LogProcessDetouringStatus | When enabled, store the Detouring Status messages in the Execution log. Defaults to off. |
| LogProcesses | Records all launched processes, including nested processes, of each pip to the log. Defaults to off. |
| LogsDirectory | Specifies the path to the logs directory. |
| LogStats | Logs key/value statistics to default stats file in the same folder as the main log file. Defaults to on. |
| LogStatus | Logs build status information to a CSV file in the same folder as the main log file. Defaults to on. |
| LogsToRetain | The number of previous logs to retain. |
| LogToConsole | Displays the specified messages in the console. |
| LogToKusto | Whether to send log events to Kusto. If enabled, a valid authentication mechanism should be available with enough permissions to write into the blob storage account where logs are piped to Kusto. Use /logToKustoBlobUri:https://{storage-account-name}/{container-name} and /logToKustoIdentityId:{Identity guid} to specify the destination of the log messages. |
| LowPriority | Runs the build engine and all tools at a lower priority in order to provide better responsiveness to interactive processes on the current machine. |
| MachineHostName | Specifies the host name where the machine running the build can be reached. This value should only be overriden by build runners, never by a user. In particular, we need it to be overriddable because on ADO networks the machines are not reachable in the hostname that GetHostName returns, and we need a special suffix that is appended by the AdoBuildRunner. |
| ManageMemoryMode | Specifies the mode to manage memory under pressure. Defaults to CancellationRam where {ShortProductName} attemps to cancel processes. EmptyWorkingSet mode will empty working set of processes instead of cancellation. Suspend mode will suspend processes to free memory. |
| MaskUntrackedAccesses | When enabled, {ShortProductName} does not consider any access under untracked paths or scopes for sake of cache lookup. Defaults to on. |
| MaxCacheLookup | Specifies the maximum number of cache lookups that {ShortProductName} will launch at one time. The default value is three times the number of processors in the current machine. |
| MaxChooseWorker | Specifies the maximum number of choose worker operations that {ShortProductName} will launch at one time for distributed builds. The default value is 1/4 of the number of processors in the current machine, but at least 1. |
| MaxChooseWorkerCacheLookup | Specifies the maximum number of choose worker cache lookup operations that {ShortProductName} will launch at one time for distributed builds. The default is 1. |
| MaxChooseWorkerLight | Specifies the maximum number of choose worker operations that {ShortProductName} will launch at one time for light pips in distributed builds. The default is 1. |
| MaxCommitUtilizationPercentage | Specifies the maximum machine wide commit utilization allowed before the scheduler will stop scheduling more work to allow resources to be freed. Defaults to 95%. |
| MaxFrontEndConcurrency | Specifies the maximum concurrency level for constructing the pip graph in the FrontEnd. The default value is 25% more than the total number of processors in the current machine. (short form: /mf) |
| MaxIO | Specifies the maximum number of I/O operations that {ShortProductName} will launch at one time. The default value is 1/4 of the number of processors in the current machine, but at least 1. |
| MaxIOMultiplier | Specifies maxIO in terms of a multiplier of the machine's processor count. Defaults to 0.25. |
| MaxLightProc | Specifies the maximum number of "light" processes that {ShortProductName} will launch at one time. Build specs can mark certain processes as "light" to indicate that they won't use much CPU time. Defaults to 0. |
| MaxMaterialize | Specifies the maximum number of materialize operations that {ShortProductName} will launch at one time. Defaults to twice the number of processors in the current machine. |
| MaxMemoryPressureLevel | [macOS] Causes scheduling to pause / cancel pips to free up system resources if specified maximum pressure level is overstepped. Allowed values are 'Normal' (Default), 'Warning' and 'Critical'. NOTE: Using 'Critical' can lead to system instablities and eventual kernel panics due to resource starvation! |
| MaxNumPipTelemetryBatches | Specifies the maximum number of batched messages to be sent to telemetry, default set to 10 messages. |
| MaxProc | Specifies the maximum number of processes that {ShortProductName} will launch at one time. Defaults to 25% more than the total number of processors in the current machine. |
| MaxProcMultiplier | Specifies maxProc in terms of a multiplier of the machine's processor count. Defaults to 1.25. |
| MaxRamUtilizationPercentage | Specifies the maximum machine wide RAM utilization allowed before the scheduler will stop scheduling more work to allow resources to be freed. Defaults to 85%. |
| MaxRelativeOutputDirectoryLength | Directories under the object directory root will get shortened to avoid too long path names. Defaults to 64 characters for relative output directories. |
| MaxTypeCheckingConcurrency | Specifies the maximum concurrency level type checking phase. Defaults to /maxFrontEndConcurrency - 25% more than the total number of processors in the current machine. |
| MinAvailableRam | This flag is deprecated. |
| MsBuild_EnableBinLogTracing | Controls whether MSBuild binlog tracing should be enabled for the build. The binlog is placed in the logs directory for each MSBuild project as 'msbuild.binlog'. WARNING: This option increases build I/O and should only be used temporarily to avoid increased build times. |
| MsBuild_EnableEngineTracing | Controls whether MSBuild engine/scheduler tracing should be enabled for the build. WARNING: Use this option only temporarily as it will significantly increase build times. |
| MsBuild_LogVerbosity | Activates MSBuild file logging for each MSBuild project file to 'msbuild.log' in the log directory, using the specified MSBuild log verbosity. WARNING: This option adds I/O overhead to your build, since MSBuild console logging is already enabled and captured, and use of Detailed or Diagnostic levels should only be used temporarily to avoid significantly increased build times. |
| NoExecutionLog | Removes a set of event IDs from the execution log (.xlg). |
| NoLog | Removes a set of event IDs from the standard log. Does not apply to warning, error, critical, and always level events. |
| NoLogo | Suppress copyright message |
| NormalizeReadTimestamps | When enabled, all file reads seen by processes will have normalized timestamps across builds. When disabled, the actual timestamps will be allowed to flow through to processes, so long as they are newer than the static timestamp used to enforce rewrite ordering (2002). Defaults to on. |
| NoWarn | Disable specific warning messages. These messages will still be logged in the main log file. |
| NumberofFailedPipRetry | Specify the number of times a pip failing due to worker failures, should be retried on another worker. Default value set to 0 (disabled). |
| NumRemoteAgentLeases | Static number of remote agent leases. Only applicable when /enableProcessRemoting is set to true. Defaults to 2 * /maxProc. |
| Obj | Specifies the root directory for primary build outputs (short form: /o) |
| OptimizedAstConversion | When enabled, optimized AST conversion by disabling some analyses and skipping some AST constructs. By disabling analyses linter policies are not enforced. The types in the resulting AST are stripped away as they are not needed for evaluation. Defauts to off. |
| OrchestratorCpuMultiplier | Specifies the cpu queue limit in terms of a multiplier of the normal limit when at least one remote worker gets connected. Defaults to 0. |
| OutputMaterializationExclusionRoot | Specifies one or more roots to be excluded from output materialization. NOTE: Files needed for execution are always materialized even if under this root. |
| Paths | Paths to output files or spec files which determine what gets built. This is a shorthand for a full filter expression. |
| PathSetAugmentationMonitoring | Check that the paths used in creating an augmented pathset/weak fingerprint are used during the execution of a pip. If a path is not used it's logged together with its hash. The argument controls the max number of paths logged per executed pip. If the value is set to 0, the monitoring is disabled. |
| PathSetThreshold | The maximum number of visited path sets allowed before switching to an 'augmented' weak fingerprint computed from common dynamically accessed paths. |
| Phase | Specifies the phase until which {ShortProductName} runs. Allowed values are None (no phase is run), Parse (run until parsing is done), Evaluate (run until value evaluation is done), Schedule (run until scheduling is done), Execute (run until execution is done). Default is Execute. |
| PinCachedOutputs | Indicates whether outputs should be pinned in CAS for cached pips. Defaults to on. |
| PipProperty | Sets execution behaviors for a pip. Supported properties: <br> PipFingerprintSalt - adds a pip specific salt value or '*' for a random salt. Ex: /pipProperty:Pip4354554[PipFingerprintingSalt=*] <br> EnableVerboseProcessLogging - Enables verbose sandbox logging for specific pips. This is equivalent to switching /logObservedFileAccesses and /logProcesses for these pips, and also enabling verbose debug logging in the sandbox. Example: /pipProperty:Pip232325435435[EnableVerboseProcessLogging] |
| PipTimeout | How long to wait before terminating individual processes, in milliseconds. Setting this value will only have an effect if no other timeout is specified for a process. |
| PipTimeoutMultiplier | Multiplier applied to the final timeout for individual processes. Setting a multiplier greater than one will increase the timeout accordingly for all pips, even those with an explicit non-default timeout set. |
| PipWarningTimeout | After how much time to issue a warning that an individual process runs too long, in milliseconds. Setting this value will only have an effect if no other timeout is specified for a process. |
| PipWarningTimeoutMultiplier | Multiplier applied to the warning timeout for individual processes. Setting a multiplier greater than one will increase the warning timeout accordingly for all pips, even those with an explicit non-default warning timeout set. |
| PluginPaths | Specify a list of plugin paths that be loaded -  each path is seperated by ";'. Defaults to empty list |
| PosixDeleteMode | Controls the applicability of file/directory deletion using POSIX delete. Allowed values are NoRun, RunFirst, and RunLast. Defaults for Windows is RunLast, and for Unix is RunFirst |
| PreserveOutputs | When enabled, {ShortProductName} will preserve the existing state of Process pip output files instead of deleting them before starting the process. This may lead to incorrect builds depending on how the process behaves when prior outputs are present. Specify "/unsafe_preserveOutputs:Reset" to reset the salt added to cached processes run with preserved outputs. |
| ProcessCanRunRemoteTags | Tags for processes that can run remotely when process remoting is enabled. When unspecified, every process can be remoted, unless it has a tag specified in /processMustRunLocalTags.  |
| ProcessMustRunLocalTags | Tags for processes that must run locally when process remoting is enabled. When unspecified, it is assumed to be empty. |
| ProcessRetries | Number of retries for process execution if the process exits with exit codes that allow for retries. Defaults to 0. |
| ProfileReportDestination | Destination file of the profiling report. Default is '{0}' and it is generated in the current directory. Only considered if /profileScript is specified. |
| ProfileScript | Runs a profiler for {ShortScriptName} evaluation, generating a TSV file with profiling information. |
| Property | Specifies a property that overrides an allowed environment variable (short form: /p) |
| Qualifiers | Qualifiers controlling what flavor to build (short form: /q) |
| RelatedActivityId | An external related ETW activity identifier. The top level {ShortProductName} activity will be logged as a child of this one. |
| RemoteAgentWaitTimeSec | The amount of wait time in seconds for getting a remote agent to execute process pip remotely when /enableProcessRemoting is set to true. Defaults to 2s. |
| RemoteTelemetry | When enabled, sends telemetry information for remote collection. Defaults to off. |
| RemotingThresholdMultiplier | Multiplier for threshold before starting to remote process pips when /enableProcessRemoting is set to true. The threshold is obtained by multiplying /maxProc with this multiplier. Defaults to 1.5. |
| ReplayWarnings | When enabled, {ShortProductName} will replay warning messages from pips that were cache hits. Defaults to on. |
| ResponseFile | Read response file for more options |
| ReuseEngineState | Reuse engine state between client sessions if /server and /cacheGraph are enabled. Defaults to on. |
| ReuseOutputsOnDisk | Reuse outputs on disk for checking up-to-dateness during cache look-up and for file materialization |
| RootMap | Specifies a drive mapping applied during this build. Paths under specified letters will be mapped to the corresponding paths at the system level for the build process and the tools launched as a part of the build. (short form: /rm) |
| RunInSubst | Improves path stability across potentially heterogeneous machines by internally mapping a source path (typically the source of the repo to build) into a drive letter. If the source path is not explicitly provided with /substSource, the location of the main config file is used. Only effective on Windows, in other platforms the option is ignored. Useful for dev cache. |
| SandboxKind | Specifies the sandbox kind. Allowed values are 'None' (no sandboxing), 'Default' (default sandboxing), 'WinDetours', 'MacOsKext'. Default is 'Default'. |
| ScanChangeJournal | Scans volume change journals to determine spec file changes for graph reuse check. Defaults to on. |
| ScanChangeJournalTimeLimitInSec | Time limit in second for scanning volume change journal. Set to -1 for no limit. Defaults to 30 seconds. |
| ScriptDebugScript | Whether to launch {ShortScriptName} debugger on start.  Intended to be used by IDEs only (if you pass this option when running form command line, {ShortScriptName} evaluator will just wait forever).  Defaults to off. |
| ScriptDebugScriptBreakOnExit | Whether to break at the end of the evaluation phase. Defaults to off. |
| ScriptDebugScriptPort | TCP/IP port for the {ShortScriptName} debugger to listen on. Defaults to 41177. |
| ScriptShowLargest | Indicates whether {ShortProductName} should log information about the largest {ShortScriptName} files. Defaults to off. |
| ScriptShowSlowest | Indicates whether {ShortProductName} should log information about the slowest {ShortScriptName} elements by phase. Defaults to off. |
| ScriptTypeCheck | Type checks specifications. Defaults to on. |
| Scrub | Before executing, scrubs (deletes) files not marked as inputs or outputs of the current build. Only applies to mounts marked as Scrubbable. This includes the object directory but none others by default. Defaults to off. |
| ScrubDirectory | Before executing, scrubs (deletes) files and directories not marked as inputs or outputs of the current build in the specified directory. Only applies to directories under mounts marked as Scrubbable. |
| Server | Launches or connects to a {ShortProductName} app-server (builds may occur in a re-usable background process, with reduced startup time). Defaults to on. |
| ServerDeploymentDir | Sets the directory where the server process deployment will be created. It not specified, the deployment will be created in the directory {MainExecutableName} is running from. |
| ServerMaxIdleTimeInMinutes | Maximum idle time in minutes for server mode. Defaults to 60 minutes. |
| ShowingStandardHelp | Standard help shown. For complete help options, use /help:verbose |
| Snapshot | When enabled, captures build inputs needed for evaluation or the full build depending on the specified snapshot mode (see /snapshotMode) |
| SnapshotMode | Specifies the mode used to snapshot the build: None, Full (capture all build inputs in VHD file), Evaluation (captures build specifications in zip file). Defaults to Evaluation. |
| SolutionName | Specifies the name of the solution which will be generated via /vs. |
| StopOnFirstError | Stops the build engine the first time an error is generated by either {ShortProductName} or one of the tool it runs. Defaults to off. |
| StopOnFirstInternalError | Stops the build engine the first time an internal error is generated by {ShortProductName}. Defaults to off for single machine builds. |
| StoreFingerprints | Stores fingerprint computation information for each pip seen in the build in a peristent key-value store that can be accessed from the logs. |
| StoreFingerprintsWithMode | Stores fingerprint computation information for each pip seen in the build in a peristent key-value store that can be accessed from the logs. "Default" mode will check for pre-existing entries before overwriting entries. "ExecutionFingerprintsOnly" mode will only store fingerprints computed when a pip executes. On strong fingerprint cache misses, setting this will SKIP storing fingerprints computed at cache lookup time, which are useful for analyzing strong fingerprint misses. "IgnoreExistingEntries" mode will overwrite entries without doing any (random) reads, this is only recommended on spin drives experiencing slowdowns.  |
| StoreOutputsToCache | When enabled, {ShortProductName} stores pip outputs to the cache. Defaults to on. |
| SubstSource | Path of the original root that has been substituted to another. Log messages will be converted back to this path root. Must be specified with /substTarget. |
| SubstTarget | Path of the subst target. Messages rooted at this path will be converted to the root configured in SubstSource. Must be specified with /substSource |
| TelemetryTagPrefix | Prefix of tag considered for sending aggregate statistics to telemetry |
| TempDirectory | Specifies the root directory for per-pip temp directories. When unspecified, temp directories will be created under the per-pip object directories. |
| TraceInfo | Attaches tracing information to the build. May be specified multiple times. Ex: /TraceInfo:Branch=MyBranch |
| TrackBuildsInUserFolder | When enabled, {ShortProductName} will log every build invocation in the users folder so analyzers can use that to figure out what to use. Defaults to on. |
| TrackGvfsProjections | When enabled, {ShortProductName} will track every .gvfs/GVFS_projection file found in any readable mount and disable features that depend on USN journal scanning whenever any of those files change. Defaults to off. |
| TrackMethodInvocations | When enabled, {ShortProductName} captures most frequently invoked {ShortScriptName} methods. Defaults to off. |
| TranslateDirectory | Specify translation of directories before access policy is applied - the fromPath is replaced with toPath in the names of paths accessed. Make sure to add the trailing path separators. Valid from/to path separators are '<' and ' and '::'. Recommended separator is '::'. Defaults to no directory translation is done. |
| TreatAbsentDirectoryAsExistentUnderOpaque | Treats absent directory as existent when it is probed and the path is under an opaque directory. Defaults to true.  |
| TreatDirectoryAsAbsentFileOnHashingInputContent | Treats directory as absent file on hashing the content of input |
| UnexpectedFileAccessesAreErrors | When enabled, if {ShortProductName} detects that a tool accesses a file that was not declared in the specification dependencies, it is treated as an error instead of a warning. Turning this option off results in an unsafe configuration (for diagnostic purposes only). Defaults to on. |
| UpdateFileContentTableByScanningChangeJournal | When enabled, file content table is updated during the scanning of change journal. Defaults to on. |
| UseCustomPipDescriptionOnConsole | Indicates whether pip descriptions should be shortened to (semi-stable hash, customer-supplied pip description) when reporting errors and warnings on the console. Defaults to on. |
| UseExtraThreadToDrainNtClose | When enabled, the draining of the {ShortProductName} handle cache happens on a different thread than the one that called the NtClose. Handles to be cleaned are collected in a thread safe, non-locking list and removed from the cache usinga different thread. Defaults to on. |
| UseFileContentTable | When enabled, use file content table. Defaults to on on Windows, but off on Unix |
| UseFileContentTablePathMappings | Use file content table path mappings to avoid opening handles for hashing files already in the table. Defaults to off. |
| UseHardlinks | When enabled, hardlinks will be used (when possible) to de-duplicate content in output directories with content in the build cache. This reduces space usage and improves performance, since copies are avoided. Creating hardlinks requires that the output directories and cache are on the same volume, and that volume must use NTFS. Defaults to on.  |
| UseLargeNtClosePreallocatedList | When enabled, it uses a larger initial preallocated list for draining NtClose events. Defaults to off. |
| UsePartialEvaluation | When enabled, a partial graph may be constructed to decrease evaluation time. Defaults to off for XML specs. |
| ValidateCgManifest | Validates the cgmanifest.json file at the specified path. This file should contain up-to-date names and versions of all Nuget packages used within BuildXL for Component Governance. Any mismatch will cause the Build to fail. Updated file can be created using the /generateCgManifestForNugets:<path> |
| ValidateDistribution | Performs validations to ensure the build can be distributed. Defaults to on for distributed builds, disabled in single machine builds. |
| VerifyCacheLookupPin | Verifies pins for cache lookup output content by attempting to materialize the content. Defaults to off. |
| VerifyJournalForEngineVolumes | Verifies that change journal is available for engine volumes (source/object/cache directories). Defaults to on. |
| VfsCasRoot | Specifies the root of the virtualized CAS directory. This should be the same as the root passed to bvfs.exe. |
| VmConcurrencyLimit | Max number of processes executed in VM. Value 0 means unbounded. Defaults to 0. |
| VS | Generates a VS solution file and MSBuild files for C# and C++ projects. Defaults to off. |
| VsOutputSrc | MSBuild project files are written under the source tree. Defaults to off. |
| WarnAsError | Treat all warnings as errors. |
| WarnAsErrorWithList | Report specific warnings as errors. |
