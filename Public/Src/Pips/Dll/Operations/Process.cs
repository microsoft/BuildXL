// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Specification of a Process invocation
    /// </summary>
    public sealed partial class Process : Pip
    {
        /// <summary>
        /// Minimum process weight
        /// </summary>
        public const int MinWeight = 1;

        /// <summary>
        /// Minimum priority.  These pips go last.
        /// </summary>
        public const int MinPriority = 0;

        /// <summary>
        /// Maximum priority.  These pips go first.
        /// </summary>
        public const int MaxPriority = 127;

        /// <summary>
        /// Maximum allowed timeout
        /// </summary>
        public static readonly TimeSpan MaxTimeout = int.MaxValue.MillisecondsToTimeSpan();

        /// <summary>
        /// Process options.
        /// </summary>
        public readonly Options ProcessOptions;

        /// <summary>
        /// Mode for absent path probes under opaque directories.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public readonly AbsentPathProbeInUndeclaredOpaquesMode ProcessAbsentPathProbeInUndeclaredOpaquesMode;

        /// <summary>
        /// If valid, load standard input from that file
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public FileArtifact StandardInputFile => StandardInput.File;

        /// <summary>
        /// If valid, use the data as the standard input.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public PipData StandardInputData => StandardInput.Data;

        /// <summary>
        /// If valid, a standard input is present.
        /// </summary>
        public StandardInput StandardInput { get; }

        /// <summary>
        /// If valid, store standard output in that file
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public FileArtifact StandardOutput { get; }

        /// <summary>
        /// If valid, store standard error in that file
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public FileArtifact StandardError { get; }

        /// <summary>
        /// Location where standard output / error may be written to.
        /// Must be valid if any of <see cref="StandardOutput" />, <see cref="StandardError" /> properties are not valid.
        /// </summary>
        /// <remarks>
        /// This property does not participate in cache fingerprinting,
        /// as it doesn't lead to any output that in turn could be consumed by any other tool.
        /// The directory and any files placed in that directory will only be consumed by log events.
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public AbsolutePath StandardDirectory { get; }

        /// <summary>
        /// Directory unique to this pip under which outputs may be written.
        /// </summary>
        /// <remarks>
        /// This property does not participate in cache fingerprinting,
        /// If set, this corresponds to the unique output directory as provided by a pip builder.
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public AbsolutePath UniqueOutputDirectory { get; }

        /// <summary>
        /// Directory unique to this pip under which redirected directories can be created. Used to virtualize
        /// inputs and outputs when the process runs in a container.
        /// </summary>
        /// <remarks>
        /// This property does not participate in cache fingerprinting,
        /// This is set when <see cref="Options.NeedsToRunInContainer"/> is specified, otherwise is invalid
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public AbsolutePath UniqueRedirectedDirectoryRoot { get; }

        /// <summary>
        /// If valid, points to the response (that is also referenced by <see cref="Arguments" />).
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public FileArtifact ResponseFile { get; }

        /// <summary>
        /// If valid, has the contents that should be written to <see cref="ResponseFile"/> before the process is executed
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public PipData ResponseFileData { get; }

        /// <summary>
        /// The tool to execute.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public FileArtifact Executable { get; }

        /// <summary>
        /// The description of the tool associated with the pip, or StringId.Invalid if the pip doesn't map to a tool.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public readonly StringId ToolDescription;

        /// <summary>
        /// The working directory of the process.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public AbsolutePath WorkingDirectory { get; }

        /// <summary>
        /// The Arguments to the process.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public PipData Arguments { get; }

        /// <summary>
        /// The environment variables.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<EnvironmentVariable> EnvironmentVariables { get; }

        /// <summary>
        /// An interval value that indicates after which time a tool will start issuing warnings that it is running longer than
        /// anticipated.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public TimeSpan? WarningTimeout { get; }

        /// <summary>
        /// A hard timeout after which the Process will be marked as failing due to timeout and terminated.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public TimeSpan? Timeout { get; }

        /// <summary>
        /// File dependencies. Each member of the array is distinct.
        /// </summary>
        /// <remarks>
        /// <code>Dependencies</code> and
        /// <code>Outputs</code>
        /// together must mention all file artifacts referenced by other properties of this Pip.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<FileArtifact> Dependencies { get; }

        /// <summary>
        /// Directory dependencies. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<DirectoryArtifact> DirectoryDependencies { get; }

        /// <summary>
        /// Order-only dependencies.
        /// </summary>
        /// <remarks>
        /// Order dependencies should not contribute to fingerprinting. As the name implies,
        /// order dependencies only affect the order of process executions. Order dependencies
        /// do not affect the outputs of a process execution, i.e., the outputs are only affected
        /// by files or dependencies that the process consumes, as well as the command-line argument.
        /// So cache look-up should not depend on the order dependencies.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public ReadOnlyArray<PipId> OrderDependencies { get; }

        /// <summary>
        /// External file dependencies
        /// </summary>
        /// <remarks>
        /// These are file dependencies that are not tracked by the scheduler,
        /// but which must still be declared to make the Detour-based file access watcher happy.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<AbsolutePath> UntrackedPaths { get; }

        /// <summary>
        /// External file dependencies
        /// </summary>
        /// <remarks>
        /// These are entire subdirectory dependencies that are not tracked by the scheduler,
        /// but which must still be declared to make the Detour-based file access watcher happy.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<AbsolutePath> UntrackedScopes { get; }

        /// <summary>
        /// Optional list of exit codes that represent success. If <code>null</code>, only 0 represents success.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<int> SuccessExitCodes { get; }

        /// <summary>
        /// Optional list of exit codes that makes BuildXL retry the process.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public ReadOnlyArray<int> RetryExitCodes { get; }

        /// <summary>
        /// Optional regular expression to detect warnings in console error / output.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public RegexDescriptor WarningRegex { get; }

        /// <summary>
        /// Optional regular expression to detect errors in console error / output.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public RegexDescriptor ErrorRegex { get; }

        /// <summary>
        /// File outputs. Each member of the array is distinct.
        /// </summary>
        /// <remarks>
        /// <code>Dependencies</code> and <code>Outputs</code>
        /// together must mention all file artifacts referenced by other properties of this Pip.
        /// Every output artifact contains an <see cref="FileExistence"/> attribute associated with it.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<FileArtifactWithAttributes> FileOutputs { get; }

        /// <summary>
        /// Directory outputs. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<DirectoryArtifact> DirectoryOutputs { get; }

        /// <summary>
        /// Information about how many semaphore units this pip needs to acquire.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public ReadOnlyArray<ProcessSemaphoreInfo> Semaphores { get; }

        /// <summary>
        /// The temp directory, if access is allowed.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public AbsolutePath TempDirectory { get; }

        /// <summary>
        /// Additional temp directories, but none of them are set to TEMP or TMP.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public ReadOnlyArray<AbsolutePath> AdditionalTempDirectories { get; }

        /// <summary>
        /// Indicates whether this is a shutdown process for a service.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public ServiceInfo ServiceInfo { get; }

        /// <summary>
        /// The # of process slots this process requires when limiting concurrency of process pips.
        /// The total weight of all proceses running concurrently must be less than or equal to the number of available process slots.
        /// The # of available process slots is typically a function of the number of cores on the machine, but can also be limited by runtime resource exhaustion or be set per-build by configuration.
        /// Valid input range for the weight is [min Int32, max Int32] though all values will be effectively coerced to fit within [1, # of process slots]
        /// If a given weight is greater than or equal to # of available process slots, the process will run alone.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public int Weight { get; }

        /// <summary>
        /// Priority hint for scheduling a process pip.
        /// Higher priorities will be scheduled before lower priorities.
        /// Minimum value is 0, max is 99
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public int Priority { get; }

        /// <summary>
        /// A helper flag to indicate if the Test for execution retries is executing.
        /// </summary>
        public bool TestRetries { get; }

        /// <summary>
        /// Whether this Process is a start or shutdown pip for a service
        /// </summary>
        [Pure]
        public bool IsStartOrShutdownKind => ServiceInfo != null && ServiceInfo.IsStartOrShutdownKind;

        /// <summary>
        /// File/directory output paths that are preserved if <see cref="AllowPreserveOutputs"/> is enabled. 
        /// </summary>
        /// <remarks>
        /// If the list is empty, all file and directory outputs are preserved. If the list is not empty,
        /// only given paths are preserved and the rest is deleted.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<AbsolutePath> PreserveOutputWhitelist { get; }

        /// <summary>
        /// Class constructor
        /// </summary>
        public Process(
            FileArtifact executable,
            AbsolutePath workingDirectory,
            PipData arguments,
            FileArtifact responseFile,
            PipData responseFileData,
            ReadOnlyArray<EnvironmentVariable> environmentVariables,
            StandardInput standardInput,
            FileArtifact standardOutput,
            FileArtifact standardError,
            AbsolutePath standardDirectory,
            TimeSpan? warningTimeout,
            TimeSpan? timeout,
            ReadOnlyArray<FileArtifact> dependencies,
            ReadOnlyArray<FileArtifactWithAttributes> outputs,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies,
            ReadOnlyArray<DirectoryArtifact> directoryOutputs,
            ReadOnlyArray<PipId> orderDependencies,
            ReadOnlyArray<AbsolutePath> untrackedPaths,
            ReadOnlyArray<AbsolutePath> untrackedScopes,
            ReadOnlyArray<StringId> tags,
            ReadOnlyArray<int> successExitCodes,
            ReadOnlyArray<ProcessSemaphoreInfo> semaphores,
            PipProvenance provenance,
            StringId toolDescription,
            ReadOnlyArray<AbsolutePath> additionalTempDirectories,
            RegexDescriptor warningRegex = default,
            RegexDescriptor errorRegex = default,
            AbsolutePath uniqueOutputDirectory = default,
            AbsolutePath uniqueRedirectedDirectoryRoot = default,
            AbsolutePath tempDirectory = default,
            Options options = default,
            bool testRetries = false,
            ServiceInfo serviceInfo = null,
            ReadOnlyArray<int>? retryExitCodes = null,
            ReadOnlyArray<PathAtom>? allowedSurvivingChildProcessNames = null,
            TimeSpan? nestedProcessTerminationTimeout = null,
            AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode = AbsentPathProbeInUndeclaredOpaquesMode.Unsafe,
            DoubleWritePolicy doubleWritePolicy = DoubleWritePolicy.DoubleWritesAreErrors,
            ContainerIsolationLevel containerIsolationLevel = ContainerIsolationLevel.None,
            int? weight = null,
            int? priority = null,
            ReadOnlyArray<AbsolutePath>? preserveOutputWhitelist = null)
        {
            Contract.Requires(executable.IsValid);
            Contract.Requires(workingDirectory.IsValid);
            Contract.Requires(arguments.IsValid);
            Contract.RequiresForAll(environmentVariables, environmentVariable => environmentVariable.Name.IsValid);
            Contract.RequiresForAll(environmentVariables, environmentVariable => environmentVariable.Value.IsValid ^ environmentVariable.IsPassThrough);
            Contract.Requires(dependencies.IsValid);
            Contract.RequiresForAll(dependencies, dependency => dependency.IsValid);
            Contract.Requires(directoryDependencies.IsValid);
            Contract.RequiresForAll(directoryDependencies, directoryDependency => directoryDependency.IsValid);
            Contract.Requires(outputs.IsValid);
            Contract.RequiresForAll(outputs, output => output.IsValid);
            Contract.Requires(directoryOutputs.IsValid);
            Contract.RequiresForAll(outputs, output => !output.IsSourceFile);
            Contract.RequiresForAll(directoryOutputs, directoryOutput => directoryOutput.IsValid);
            Contract.Requires(orderDependencies.IsValid);
            Contract.RequiresForAll(orderDependencies, dependency => dependency != PipId.Invalid);
            Contract.Requires(untrackedPaths.IsValid);
            Contract.RequiresForAll(untrackedPaths, path => path.IsValid);
            Contract.Requires(untrackedScopes.IsValid);
            Contract.RequiresForAll(untrackedScopes, scope => scope.IsValid);
            Contract.Requires(!timeout.HasValue || timeout.Value <= MaxTimeout);
            Contract.Requires(standardDirectory.IsValid || (standardOutput.IsValid && standardError.IsValid));
            Contract.Requires(provenance != null);
            Contract.Requires(additionalTempDirectories.IsValid);
            Contract.RequiresForAll(additionalTempDirectories, path => path.IsValid);
            Contract.Requires(tags.IsValid);
            // If the process needs to run in a container, the redirected directory has to be set
            Contract.Requires((options & Options.NeedsToRunInContainer) == Options.None || uniqueRedirectedDirectoryRoot.IsValid);

#if DEBUG   // a little too expensive for release builds
            Contract.Requires(Contract.Exists(dependencies, d => d == executable), "The executable must be declared as a dependency");
            Contract.Requires(
                !standardInput.IsFile || Contract.Exists(dependencies, d => d == standardInput.File),
                "If provided, the standard-input artifact must be declared as a dependency");
            Contract.Requires(
                !standardOutput.IsValid || Contract.Exists(outputs, o => o.ToFileArtifact() == standardOutput),
                "If provided, the standard-error artifact must be declared as an expected output");
            Contract.Requires(
                !standardError.IsValid || Contract.Exists(outputs, o => o.ToFileArtifact() == standardError),
                "If provided, the standard-error artifact must be declared as an expected output");
            Contract.Requires(
                !responseFile.IsValid ^ responseFileData.IsValid,
                "If provided, the response-file artifact must have a corresponding ResponseFileData");

            Contract.Requires(outputs.Length == outputs.Distinct().Count());
            Contract.Requires(directoryOutputs.Length == directoryOutputs.Distinct().Count());
            Contract.Requires(dependencies.Length == dependencies.Distinct().Count());
            Contract.Requires(directoryDependencies.Length == directoryDependencies.Distinct().Count());
            Contract.Requires(untrackedPaths.Length == untrackedPaths.Distinct().Count());
            Contract.Requires(untrackedScopes.Length == untrackedScopes.Distinct().Count());
            Contract.Requires(additionalTempDirectories.Length == additionalTempDirectories.Distinct().Count());
            Contract.RequiresForAll(semaphores, s => s.IsValid);
            Contract.Requires(semaphores.Length == semaphores.Distinct().Count());
#endif

            Provenance = provenance;
            Tags = tags;
            Executable = executable;
            ToolDescription = toolDescription;
            WorkingDirectory = workingDirectory;
            Arguments = arguments;
            ResponseFile = responseFile;
            ResponseFileData = responseFileData;
            StandardOutput = standardOutput;
            StandardError = standardError;
            StandardInput = standardInput;
            StandardDirectory = standardDirectory;
            WarningTimeout = warningTimeout;
            Timeout = timeout;

            // We allow any IEnumerable for these fields, but perform a copy up-front. 
            // See the remarks of RemoveDuplicateFileArtifacts for why it is used on the input / output lists.
            Dependencies = dependencies;
            DirectoryDependencies = directoryDependencies;
            FileOutputs = outputs;
            DirectoryOutputs = directoryOutputs;
            OrderDependencies = orderDependencies;
            UntrackedPaths = untrackedPaths;
            UntrackedScopes = untrackedScopes;
            EnvironmentVariables = environmentVariables;
            SuccessExitCodes = successExitCodes;
            RetryExitCodes = retryExitCodes ?? ReadOnlyArray<int>.Empty;
            WarningRegex = warningRegex;
            ErrorRegex = errorRegex;
            UniqueOutputDirectory = uniqueOutputDirectory;
            UniqueRedirectedDirectoryRoot = uniqueRedirectedDirectoryRoot;
            Semaphores = semaphores;
            TempDirectory = tempDirectory;
            TestRetries = testRetries;
            ServiceInfo = serviceInfo;
            AdditionalTempDirectories = additionalTempDirectories;
            AllowedSurvivingChildProcessNames = allowedSurvivingChildProcessNames ?? ReadOnlyArray<PathAtom>.Empty;
            NestedProcessTerminationTimeout = nestedProcessTerminationTimeout;
            ProcessAbsentPathProbeInUndeclaredOpaquesMode = absentPathProbeMode;
            DoubleWritePolicy = doubleWritePolicy;
            ContainerIsolationLevel = containerIsolationLevel;
            Weight = weight.HasValue && weight.Value >= MinWeight ? weight.Value : MinWeight;
            Priority = priority.HasValue && priority.Value >= MinPriority ? (priority <= MaxPriority ? priority.Value : MaxPriority) : MinPriority;
            PreserveOutputWhitelist = preserveOutputWhitelist ?? ReadOnlyArray<AbsolutePath>.Empty;
            if (PreserveOutputWhitelist.Length != 0)
            {
                options |= Options.HasPreserveOutputWhitelist;
            }

            ProcessOptions = options;
        }

        /// <summary>
        /// Clone and override select properties.
        /// </summary>
        public Process Override(
            FileArtifact? executable = null,
            AbsolutePath? workingDirectory = null,
            PipData? arguments = null,
            FileArtifact? responseFile = null,
            PipData? responseFileData = null,
            ReadOnlyArray<EnvironmentVariable>? environmentVariables = null,
            StandardInput? standardInput = null,
            FileArtifact? standardOutput = null,
            FileArtifact? standardError = null,
            AbsolutePath? standardDirectory = null,
            TimeSpan? warningTimeout = null,
            TimeSpan? timeout = null,
            ReadOnlyArray<FileArtifact>? dependencies = null,
            ReadOnlyArray<FileArtifactWithAttributes>? fileOutputs = null,
            ReadOnlyArray<DirectoryArtifact>? directoryDependencies = null,
            ReadOnlyArray<DirectoryArtifact>? directoryOutputs = null,
            ReadOnlyArray<PipId>? orderDependencies = null,
            ReadOnlyArray<AbsolutePath>? untrackedPaths = null,
            ReadOnlyArray<AbsolutePath>? untrackedScopes = null,
            ReadOnlyArray<StringId>? tags = null,
            ReadOnlyArray<int>? successExitCodes = null,
            ReadOnlyArray<ProcessSemaphoreInfo>? semaphores = null,
            PipProvenance provenance = null,
            StringId? toolDescription = null,
            ReadOnlyArray<AbsolutePath>? additionalTempDirectories = null,
            RegexDescriptor? warningRegex = null,
            RegexDescriptor? errorRegex = null,
            AbsolutePath? uniqueOutputDirectory = null,
            AbsolutePath? redirectedDirectoryRoot = null,
            AbsolutePath? tempDirectory = null,
            Options? options = null,
            bool? testRetries = null,
            ServiceInfo serviceInfo = null,
            ReadOnlyArray<int>? retryExitCodes = null,
            ReadOnlyArray<PathAtom>? allowedSurvivingChildProcessNames = null,
            TimeSpan? nestedProcessTerminationTimeout = null,
            AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode = AbsentPathProbeInUndeclaredOpaquesMode.Unsafe,
            DoubleWritePolicy doubleWritePolicy = DoubleWritePolicy.DoubleWritesAreErrors,
            ContainerIsolationLevel containerIsolationLevel = ContainerIsolationLevel.None,
            int? weight = null,
            int? priority = null,
            ReadOnlyArray<AbsolutePath>? preserveOutputWhitelist = null)
        {
            return new Process(
                executable ?? Executable,
                workingDirectory ?? WorkingDirectory,
                arguments ?? Arguments,
                responseFile ?? ResponseFile,
                responseFileData ?? ResponseFileData,
                environmentVariables ?? EnvironmentVariables,
                standardInput ?? StandardInput,
                standardOutput ?? StandardOutput,
                standardError ?? StandardError,
                standardDirectory ?? StandardDirectory,
                warningTimeout ?? WarningTimeout,
                timeout ?? Timeout,
                dependencies ?? Dependencies,
                fileOutputs ?? FileOutputs,
                directoryDependencies ?? DirectoryDependencies,
                directoryOutputs ?? DirectoryOutputs,
                orderDependencies ?? OrderDependencies,
                untrackedPaths ?? UntrackedPaths,
                untrackedScopes ?? UntrackedScopes,
                tags ?? Tags,
                successExitCodes ?? SuccessExitCodes,
                semaphores ?? Semaphores,
                provenance ?? Provenance,
                toolDescription ?? ToolDescription,
                additionalTempDirectories ?? AdditionalTempDirectories,
                warningRegex ?? WarningRegex,
                errorRegex ?? ErrorRegex,
                uniqueOutputDirectory ?? UniqueOutputDirectory,
                redirectedDirectoryRoot ?? UniqueRedirectedDirectoryRoot,
                tempDirectory ?? TempDirectory,
                options ?? ProcessOptions,
                testRetries ?? TestRetries,
                serviceInfo ?? ServiceInfo,
                retryExitCodes ?? RetryExitCodes,
                allowedSurvivingChildProcessNames,
                nestedProcessTerminationTimeout,
                absentPathProbeMode,
                doubleWritePolicy,
                containerIsolationLevel,
                weight,
                priority,
                preserveOutputWhitelist ?? PreserveOutputWhitelist);
        }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags { get; }

        /// <inheritdoc />
        public override PipProvenance Provenance { get; }

        /// <inheritdoc />
        public override PipType PipType => PipType.Process;

        /// <summary>
        /// Whether to ignore nested processes when considering dependencies
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public bool HasUntrackedChildProcesses => (ProcessOptions & Options.HasUntrackedChildProcesses) != 0;

        /// <summary>
        /// Indicates whether the tool produces path independent outputs (i.e., tool outputs contain full paths).
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public bool ProducesPathIndependentOutputs => (ProcessOptions & Options.ProducesPathIndependentOutputs) != 0;

        /// <summary>
        /// Indicates if the outputs of this process must be left writable (the build engine may not defensively make them readonly).
        /// This prevents hardlinking of these outputs into the build cache, even if otherwise enabled.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public bool OutputsMustRemainWritable => (ProcessOptions & Options.OutputsMustRemainWritable) != 0;

        /// <summary>
        /// Whether this process requires admin privilege
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public bool RequiresAdmin => (ProcessOptions & Options.RequiresAdmin) != 0;

        /// <summary>
        /// Indicates the process may run without deleting prior outputs from a previous run.
        /// </summary>
        public bool AllowPreserveOutputs => (ProcessOptions & Options.AllowPreserveOutputs) != 0;

        /// <summary>
        /// Indicates whether this is a light process.
        /// </summary>
        public bool IsLight => (ProcessOptions & Options.IsLight) != 0;

        /// <summary>
        /// Indicates whether this is a service process.
        /// </summary>
        public bool IsService => ServiceInfo != null && ServiceInfo.Kind == ServicePipKind.Service;

        /// <summary>
        /// <see cref="Options.AllowUndeclaredSourceReads"/>
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public bool AllowUndeclaredSourceReads => (ProcessOptions & Options.AllowUndeclaredSourceReads) != 0;

        /// <summary>
        /// <see cref="Options.NeedsToRunInContainer"/>
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public bool NeedsToRunInContainer => (ProcessOptions & Options.NeedsToRunInContainer) != 0;

        /// <summary>
        /// Shortcut; <see cref="ServiceInfo.ShutdownPipId"/>.
        /// </summary>
        public PipId ShutdownProcessPipId => ServiceInfo?.ShutdownPipId ?? PipId.Invalid;

        /// <summary>
        /// Shortcut; <see cref="ServiceInfo.ServicePipDependencies"/>.
        /// </summary>
        public ReadOnlyArray<PipId> ServicePipDependencies => ServiceInfo?.ServicePipDependencies ?? ReadOnlyArray<PipId>.Empty;

        /// <summary>
        /// Returns the name of the tool
        /// </summary>
        public PathAtom GetToolName(PathTable pathTable) => Executable.Path.GetName(pathTable);

        /// <summary>
        /// The process names, e.g. "mspdbsrv.exe", allowed to be cleaned up by a process pip sandbox job object
        /// without throwing a build error DX0041.
        /// </summary>
        public ReadOnlyArray<PathAtom> AllowedSurvivingChildProcessNames { get; }

        /// <summary>
        /// Wall clock time limit to wait for nested processes to exit after main process has terminated.
        /// Default value is 30 seconds (SandboxedProcessInfo.DefaultNestedProcessTerminationTimeout).
        /// </summary>
        public TimeSpan? NestedProcessTerminationTimeout { get; }

        /// <summary>
        /// Indicates whether this pip is configured to always miss
        /// </summary>
        public bool DisableCacheLookup => (ProcessOptions & Options.DisableCacheLookup) != 0;

        /// <summary>
        /// What policy to apply when merging redirected outputs back
        /// </summary>
        /// <remarks>
        /// Only makes sense when <see cref="NeedsToRunInContainer"/> is true
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public DoubleWritePolicy DoubleWritePolicy { get; }

        /// <summary>
        /// How much of this process (in terms of inputs and outputs) should be isolated in the container
        /// </summary>
        /// <remarks>
        /// Only makes sense when <see cref="NeedsToRunInContainer"/> is true
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ContainerIsolationLevel ContainerIsolationLevel { get; }

        #region PipUniqueOutputHash

        /// <summary>
        /// Caches the unique output hash computed by <see cref="TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </summary>
        private long? m_cachedUniqueOutputHash;

        /// <summary>
        /// The delimiter for different inputs to the unique output hash computed by <see cref="TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </summary>
        private const char UniqueOutputHashDelimiter = ':';

        /// <summary>
        /// Attempts to create a unique string identifier for a pip based off the pip's declared outputs. 
        /// All outputs in BuildXL are unique to one pip (except for shared opaque directories),
        /// so the first output declared can be used to identify a pip across builds.
        /// </summary>
        /// <returns>
        /// True, if a hash can be computed based off a pip's declared outputs that would reliably and uniquely identify the pip across pips;
        /// otherwise, false.
        /// </returns>
        public bool TryComputePipUniqueOutputHash(PathTable pathTable, out long pipUniqueOutputHash, PathExpander pathExpander = null)
        {
            // All pips must have produce at least one output
            Contract.Assert(FileOutputs.Length + DirectoryOutputs.Length > 0);

            if (m_cachedUniqueOutputHash.HasValue)
            {
                pipUniqueOutputHash = m_cachedUniqueOutputHash.Value;
                return true;
            }

            pipUniqueOutputHash = -1;

            AbsolutePath outputPath;
            int rewriteCount = -1;  
            // Arbitrarily use file outputs before directory outputs
            if (FileOutputs.IsValid && FileOutputs.Length > 0)
            {
                var output = FileOutputs[0];
                outputPath = output.Path;
                rewriteCount = output.RewriteCount;
            }
            else
            {
                var output = DirectoryOutputs[0];

                // There can only be one pip producer for a directory output (opaque directory) with the exception of
                // shared opaque directories.
                // There is no good way to differentiate two pips whose only declared output is the same shared opaque directory,
                // so this function should conservatively bail out for shared opaques.
                if (output.IsSharedOpaque)
                {
                    return false;
                }

                outputPath = DirectoryOutputs[0].Path;
            }

            var pathString = pathExpander == null ? outputPath.ToString(pathTable) : pathExpander.ExpandPath(pathTable, outputPath);
            // A file can have more than one pip producer, so the rewrite count must also be included in the hash
            pipUniqueOutputHash = HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(pathString + UniqueOutputHashDelimiter + rewriteCount.ToString());

            m_cachedUniqueOutputHash = pipUniqueOutputHash;
            return true;
        }

        #endregion PipUniqueOutputHash

        #region Serialization
        internal static Process InternalDeserialize(PipReader reader)
        {
            return new Process(
                executable: reader.ReadFileArtifact(),
                workingDirectory: reader.ReadAbsolutePath(),
                arguments: reader.ReadPipData(),
                responseFile: reader.ReadFileArtifact(),
                responseFileData: reader.ReadPipData(),
                environmentVariables: reader.ReadReadOnlyArray(reader1 => ((PipReader)reader1).ReadEnvironmentVariable()),
                standardInput: StandardInput.InternalDeserialize(reader),
                standardOutput: reader.ReadFileArtifact(),
                standardError: reader.ReadFileArtifact(),
                standardDirectory: reader.ReadAbsolutePath(),
                warningTimeout: reader.ReadNullableStruct(reader1 => reader1.ReadTimeSpan()),
                timeout: reader.ReadNullableStruct(reader1 => reader1.ReadTimeSpan()),
                dependencies: reader.ReadReadOnlyArray(reader1 => reader1.ReadFileArtifact()),
                outputs: reader.ReadReadOnlyArray(reader1 => reader1.ReadFileArtifactWithAttributes()),
                directoryDependencies: reader.ReadReadOnlyArray(reader1 => reader1.ReadDirectoryArtifact()),
                directoryOutputs: reader.ReadReadOnlyArray(reader1 => reader1.ReadDirectoryArtifact()),
                orderDependencies: reader.ReadReadOnlyArray(reader1 => ((PipReader)reader1).ReadPipId()),
                untrackedPaths: reader.ReadReadOnlyArray(reader1 => reader1.ReadAbsolutePath()),
                untrackedScopes: reader.ReadReadOnlyArray(reader1 => reader1.ReadAbsolutePath()),
                tags: reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                successExitCodes: reader.ReadReadOnlyArray(reader1 => reader1.ReadInt32()),
                semaphores: reader.ReadReadOnlyArray(reader1 => ((PipReader)reader1).ReadProcessSemaphoreInfo()),
                provenance: reader.ReadPipProvenance(),
                toolDescription: reader.ReadStringId(),
                additionalTempDirectories: reader.ReadReadOnlyArray(reader1 => reader1.ReadAbsolutePath()),
                warningRegex: reader.ReadRegexDescriptor(),
                errorRegex: reader.ReadRegexDescriptor(),
                uniqueOutputDirectory: reader.ReadAbsolutePath(),
                uniqueRedirectedDirectoryRoot: reader.ReadAbsolutePath(),
                tempDirectory: reader.ReadAbsolutePath(),
                options: (Options)reader.ReadInt32(),
                serviceInfo: reader.ReadNullable(reader1 => Operations.ServiceInfo.InternalDeserialize(reader1)),
                retryExitCodes: reader.ReadReadOnlyArray(r => r.ReadInt32()),
                allowedSurvivingChildProcessNames: reader.ReadReadOnlyArray(reader1 => reader1.ReadPathAtom()),
                nestedProcessTerminationTimeout: reader.ReadNullableStruct(reader1 => reader1.ReadTimeSpan()),
                absentPathProbeMode: (AbsentPathProbeInUndeclaredOpaquesMode)reader.ReadByte(),
                doubleWritePolicy: (DoubleWritePolicy)reader.ReadByte(),
                containerIsolationLevel: (ContainerIsolationLevel)reader.ReadByte(),
                weight: reader.ReadInt32Compact(),
                priority: reader.ReadInt32Compact(),
                preserveOutputWhitelist: reader.ReadReadOnlyArray(r => r.ReadAbsolutePath())
                );
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Executable);
            writer.Write(WorkingDirectory);
            writer.Write(Arguments);
            writer.Write(ResponseFile);
            writer.Write(ResponseFileData);
            writer.Write(EnvironmentVariables, (w, v) => ((PipWriter)w).Write(v));
            StandardInput.InternalSerialize(writer);
            writer.Write(StandardOutput);
            writer.Write(StandardError);
            writer.Write(StandardDirectory);
            writer.Write(WarningTimeout, (w, value) => w.Write(value));
            writer.Write(Timeout, (w, value) => w.Write(value));
            writer.Write(Dependencies, (w, v) => w.Write(v));
            writer.Write(FileOutputs, (w, v) => w.Write(v));
            writer.Write(DirectoryDependencies, (w, v) => w.Write(v));
            writer.Write(DirectoryOutputs, (w, v) => w.Write(v));
            writer.Write(OrderDependencies, (w, v) => ((PipWriter)w).Write(v));
            writer.Write(UntrackedPaths, (w, v) => w.Write(v));
            writer.Write(UntrackedScopes, (w, v) => w.Write(v));
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(SuccessExitCodes, (w, v) => w.Write(v));
            writer.Write(Semaphores, (w, v) => ((PipWriter)w).Write(v));
            writer.Write(Provenance);
            writer.Write(ToolDescription);
            writer.Write(AdditionalTempDirectories, (w, v) => w.Write(v));
            writer.Write(WarningRegex);
            writer.Write(ErrorRegex);
            writer.Write(UniqueOutputDirectory);
            writer.Write(UniqueRedirectedDirectoryRoot);
            writer.Write(TempDirectory);
            writer.Write((int)ProcessOptions);
            writer.Write(ServiceInfo, ServiceInfo.InternalSerialize);
            writer.Write(RetryExitCodes, (w, v) => w.Write(v));
            writer.Write(AllowedSurvivingChildProcessNames, (w, v) => w.Write(v));
            writer.Write(NestedProcessTerminationTimeout, (w, t) => w.Write(t));
            writer.Write((byte)ProcessAbsentPathProbeInUndeclaredOpaquesMode);
            writer.Write((byte)DoubleWritePolicy);
            writer.Write((byte)ContainerIsolationLevel);
            writer.WriteCompact(Weight);
            writer.WriteCompact(Priority);
            writer.Write(PreserveOutputWhitelist, (w, v) => w.Write(v));
        }
        #endregion
    }
}
