// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Tracing;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Pips.Operations.Process;
using Logger = BuildXL.Pips.Tracing.Logger;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// A helper class to build a process pip
    /// </summary>
    public class ProcessBuilder : IDisposable
    {
        internal const int MaxCommandLineLength = 8192;

        // State
        private readonly PathTable m_pathTable;

        // Process launch

        /// <nodoc />
        public FileArtifact Executable { get; set; }

        /// <nodoc />
        public DirectoryArtifact WorkingDirectory { get; set; }

        private readonly Dictionary<StringId, (PipData, bool)> m_environmentVariables;

        private readonly PooledObjectWrapper<PipDataBuilder> m_argumentsBuilder;

        /// <nodoc />
        public PipDataBuilder ArgumentsBuilder => m_argumentsBuilder.Instance;

        /// <nodoc />
        public FileArtifact ResponseFile { get; set; }

        private ResponseFileSpecification m_responseFileSpecification;

        /// <nodoc />
        public PipData ResponseFileData { get; set; }

        // Metadata about the pip

        /// <nodoc />
        public PipData Usage { get; set; }

        /// <nodoc />
        public StringId ToolDescription { get; set; }

        /// <nodoc />
        public ReadOnlyArray<StringId> Tags { get; set; } = ReadOnlyArray<StringId>.Empty;

        // Dependencies
        private readonly PooledObjectWrapper<HashSet<FileArtifact>> m_inputFiles;
        private readonly PooledObjectWrapper<HashSet<DirectoryArtifact>> m_inputDirectories;
        private readonly PooledObjectWrapper<HashSet<PipId>> m_servicePipDependencies;
        private readonly PooledObjectWrapper<HashSet<PipId>> m_orderPipDependencies;

        // Outputs 
        private readonly PooledObjectWrapper<HashSet<FileArtifactWithAttributes>> m_outputFiles;
        private readonly PooledObjectWrapper<HashSet<DirectoryArtifact>> m_outputDirectories;
        private readonly PooledObjectWrapper<HashSet<DirectoryArtifact>> m_projectedSharedOutputDirectories;
        private readonly PooledObjectWrapper<HashSet<AbsolutePath>> m_outputDirectoryExclusions;

        // Input/Output streams

        /// <nodoc />
        public StandardInput StandardInput { get; set; }

        private FileArtifact m_standardOutputFile;

        private FileArtifact m_standardErrorFile;

        private FileArtifact m_traceFile;

        // RewrittenWork. This list is lazily created
        private List<(FileArtifact, AbsolutePath)> m_filesToCopyBeforeWrite;

        // Runtime behaviors

        /// <nodoc />
        public ReadOnlyArray<int> SuccessExitCodes { get; set; } = ReadOnlyArray<int>.Empty;

        /// <nodoc />
        public ReadOnlyArray<int> RetryExitCodes { get; set; } = ReadOnlyArray<int>.Empty;

        /// <nodoc />
        public ReadOnlyArray<int> SucceedFastExitCodes { get; set; } = ReadOnlyArray<int>.Empty;

        private Dictionary<StringId, ProcessSemaphoreInfo> m_semaphores;

        /// <summary>
        /// The weighted value of this pip when limiting concurrency of process pips.
        /// The higher the weight, the fewer process pips that can run in parallel.
        /// </summary>
        public int? Weight { get; set; } = null;

        /// <summary>
        /// The priority value of this pip when scheduling process pips.
        /// The higher the priority, the sooner it will run.
        /// </summary>
        public int? Priority { get; set; } = null;

        /// <nodoc />
        public TimeSpan? Timeout { get; set; }

        /// <nodoc />
        public TimeSpan? WarningTimeout { get; set; }

        /// <summary>
        /// Wall clock time limit to wait for nested processes to exit after main process has terminated.
        /// Default value is 30 seconds (SandboxedProcessInfo.DefaultNestedProcessTerminationTimeout).
        /// </summary>
        public TimeSpan? NestedProcessTerminationTimeout { get; set; }

        /// <nodoc />
        public RegexDescriptor? WarningRegex { get; set; }

        /// <nodoc />
        public RegexDescriptor? ErrorRegex { get; set; }

        /// <nodoc />
        public bool EnableMultiLineErrorScanning { get; set; }

        /// <nodoc />
        public Options Options { get; set; }

        /// <nodoc />
        public int PreserveOutputsTrustLevel { get; set; } = (int)PreserveOutputsTrustValue.Lowest;

        /// <nodoc />
        public ReadOnlyArray<AbsolutePath> PreserveOutputAllowlist { get; set; } = ReadOnlyArray<AbsolutePath>.Empty;

        // Container related

        /// <summary>
        /// <see cref="RewritePolicy"/>. 
        /// </summary>
        public RewritePolicy RewritePolicy { get; set; }

        /// <summary>
        /// <see cref="ContainerIsolationLevel"/>. Only in effect if <see cref="Options.NeedsToRunInContainer"/> is true.
        /// </summary>
        public ContainerIsolationLevel ContainerIsolationLevel { get; set; }

        /// <nodoc />
        public AbsentPathProbeInUndeclaredOpaquesMode AbsentPathProbeUnderOpaquesMode {get; set; }

        // Service related

        /// <nodoc />
        public ServicePipKind ServiceKind { get; set; }

        /// <nodoc />
        public PipId ShutDownProcessPipId { get; set; }

        /// <nodoc />
        public ReadOnlyArray<PipId> FinalizationPipIds { get; set; } = ReadOnlyArray<PipId>.Empty;

        private StringId m_serviceTrackableTag;
        private StringId m_serviceTrackableTagDisplayName;

        // temp & untracked

        private bool m_enableAutomaticTempDirectory;

        /// <nodoc />
        public AbsolutePath TempDirectory { get; set; }

        /// <nodoc />
        public ReadOnlyArray<AbsolutePath> AdditionalTempDirectories { get; set; } = ReadOnlyArray<AbsolutePath>.Empty;

        private readonly PooledObjectWrapper<HashSet<AbsolutePath>> m_untrackedFilesAndDirectories;

        private readonly PooledObjectWrapper<HashSet<DirectoryArtifact>> m_untrackedDirectoryScopes;

        /// <nodoc />
        public ReadOnlyArray<PathAtom> AllowedSurvivingChildProcessNames { get; set; } = ReadOnlyArray<PathAtom>.Empty;

        /// <nodoc />
        public ReadOnlyArray<PathAtom> ChildProcessesToBreakawayFromSandbox { get; set; } = ReadOnlyArray<PathAtom>.Empty;

        private readonly AbsolutePath m_realUserProfilePath;
        private readonly AbsolutePath m_redirectedUserProfilePath;

        private FileArtifact m_changeAffectedInputListWrittenFile;
        private StringId m_retryAttemptEnvironmentVariable = StringId.Invalid;

        private int? m_processRetries;

        // CredScan
        private readonly CredentialScanner m_credentialScanner;
        private readonly IReadOnlyList<string> m_credScanEnvironmentVariablesAllowList;
        private readonly LoggingContext m_loggingContext;

        /// <nodoc />
        private ProcessBuilder(PathTable pathTable, PooledObjectWrapper<PipDataBuilder> argumentsBuilder, CredentialScanner credentialScanner, LoggingContext loggingContext, IReadOnlyList<string> credScanEnvironmentVariablesAllowList = null)
        {
            m_pathTable = pathTable;
            m_argumentsBuilder = argumentsBuilder;
            m_inputFiles = Pools.GetFileArtifactSet();
            m_inputDirectories = Pools.GetDirectoryArtifactSet();
            m_servicePipDependencies = PipPools.PipIdSetPool.GetInstance();
            m_orderPipDependencies = PipPools.PipIdSetPool.GetInstance();

            m_outputFiles = Pools.GetFileArtifactWithAttributesSet();
            m_outputDirectories = Pools.GetDirectoryArtifactSet();
            m_projectedSharedOutputDirectories = Pools.GetDirectoryArtifactSet();
            m_outputDirectoryExclusions = Pools.GetAbsolutePathSet();

            m_untrackedFilesAndDirectories = Pools.GetAbsolutePathSet();
            m_untrackedDirectoryScopes = Pools.GetDirectoryArtifactSet();

            m_environmentVariables = new Dictionary<StringId, (PipData, bool)>();

            m_realUserProfilePath = AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));
            m_redirectedUserProfilePath = AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));

            // if the paths point to the same location, there is no user profile redirect
            if (m_realUserProfilePath == m_redirectedUserProfilePath)
            {
                m_redirectedUserProfilePath = AbsolutePath.Invalid;
            }

            // TODO: change this once Unsafe mode is removed / no longer a default mode
            AbsentPathProbeUnderOpaquesMode = AbsentPathProbeInUndeclaredOpaquesMode.Unsafe;

            //CredScan
            m_credentialScanner = credentialScanner;
            m_credScanEnvironmentVariablesAllowList = credScanEnvironmentVariablesAllowList;
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Creates a new ProcessBuilder
        /// </summary>
        public static ProcessBuilder Create(PathTable pathTable, PooledObjectWrapper<PipDataBuilder> argumentsBuilder, CredentialScanner credentialScanner, LoggingContext loggingContext, IReadOnlyList<string> credScanEnvironmentVariablesAllowList = null)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(argumentsBuilder.Instance != null);
            return new ProcessBuilder(pathTable, argumentsBuilder, credentialScanner, loggingContext, credScanEnvironmentVariablesAllowList);
        }

        /// <summary>
        /// Helper to create a new ProcessBuilder for testing that doesn't need to pass the pooled PipDataBuilder for convenience
        /// </summary>
        public static ProcessBuilder CreateForTesting(PathTable pathTable, CredentialScanner credentialScanner, LoggingContext loggingContext, IReadOnlyList<string> credScanEnvironmentVariablesAllowList = null)
        {
            var tempPool = new ObjectPool<PipDataBuilder>(() => new PipDataBuilder(pathTable.StringTable), _ => { });
            return new ProcessBuilder(pathTable, tempPool.GetInstance(), credentialScanner, loggingContext, credScanEnvironmentVariablesAllowList);
        }

        /// <nodoc />
        public void AddInputFile(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            m_inputFiles.Instance.Add(file);
        }

        /// <summary>
        /// Returns the file dependencies that have been added so far.
        /// </summary>
        /// <remarks>
        /// This is used by IpcProcess pips
        /// </remarks>
        public ReadOnlyArray<FileArtifact> GetInputFilesSoFar()
        {
            return ReadOnlyArray<FileArtifact>.From(m_inputFiles.Instance);
        }

        /// <nodoc />
        public void AddInputDirectory(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);

            m_inputDirectories.Instance.Add(directory);
        }

        /// <summary>
        /// Returns the directory dependencies that have been added so far.
        /// </summary>
        /// <remarks>
        /// This is used by IpcProcess pips
        /// </remarks>
        public ReadOnlyArray<DirectoryArtifact> GetInputDirectoriesSoFar()
        {
            return ReadOnlyArray<DirectoryArtifact>.From(m_inputDirectories.Instance);
        }

        /// <summary>
        /// Returns the untracked directory scopes that have been added so far.
        /// </summary>
        public ReadOnlyArray<DirectoryArtifact> GetUntrackedDirectoryScopesSoFar()
        {
            return ReadOnlyArray<DirectoryArtifact>.From(m_untrackedDirectoryScopes.Instance);
        }

        /// <nodoc />
        public void AddOutputFile(AbsolutePath file, FileExistence attribute)
        {
            Contract.Requires(file.IsValid);

            m_outputFiles.Instance.Add(new FileArtifactWithAttributes(file, 1, attribute));
        }

        /// <nodoc />
        public void AddOutputFile(FileArtifact file, FileExistence attribute)
        {
            Contract.Requires(file.IsValid);

            m_outputFiles.Instance.Add(new FileArtifactWithAttributes(file, file.RewriteCount + 1, attribute));
        }

        /// <nodoc />
        public void AddOutputDirectory(DirectoryArtifact directory, SealDirectoryKind kind)
        {
            Contract.Requires(directory.IsValid);

            switch (kind)
            {
                case SealDirectoryKind.Opaque:
                    m_outputDirectories.Instance.Add(directory);
                    break;
                case SealDirectoryKind.SharedOpaque:
                    m_projectedSharedOutputDirectories.Instance.Add(directory);
                    break;
                default:
                    throw Contract.AssertFailure("Unsupported output directory kind");
            }
        }

        /// <summary>
        /// Adds an output directory exclusion
        /// </summary>
        /// <remarks>
        /// Any output directory artifact produced under the specified root will not be considered part of any opaque directory
        /// </remarks>
        public void AddOutputDirectoryExclusion(AbsolutePath exclusionRoot)
        {
            Contract.Requires(exclusionRoot.IsValid);
            m_outputDirectoryExclusions.Instance.Add(exclusionRoot);
        }

        /// <nodoc />
        public void AddRewrittenFileInPlace(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            m_inputFiles.Instance.Add(file);
            m_outputFiles.Instance.Add(new FileArtifactWithAttributes(file.Path, file.RewriteCount + 1, FileExistence.Required));
        }

        /// <nodoc />
        public void AddRewrittenFileWithCopy(AbsolutePath path, FileArtifact file)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(file.IsValid);

            m_filesToCopyBeforeWrite = m_filesToCopyBeforeWrite ?? new List<(FileArtifact, AbsolutePath)>();
            m_filesToCopyBeforeWrite.Add((file, path));
            // We add the in-place rewrite information by 'spoofing' the output filter
            AddRewrittenFileInPlace(FileArtifact.CreateOutputFile(path));
        }

        /// <nodoc />
        public void AddServicePipDependency(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);

            // It is a service client if it has a dependency to a service pip.
            ServiceKind = ServicePipKind.ServiceClient;
            m_servicePipDependencies.Instance.Add(pipId);
        }

        /// <summary>
        /// This is for testing purposes only.
        /// </summary>        
        public void AddOrderDependency(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);

            m_orderPipDependencies.Instance.Add(pipId);
        }

        /// <nodoc />
        public void AddUntrackedFile(AbsolutePath file)
        {
            Contract.Requires(file.IsValid);

            m_untrackedFilesAndDirectories.Instance.Add(file);
        }

        /// <nodoc />
        public void AddUntrackedDirectoryScope(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);

            m_untrackedDirectoryScopes.Instance.Add(directory);
        }

        /// <summary>
        /// Adds the common folders that almost every app reads as either untracked scopes if they should
        /// have no effect on the build output (like the OS kernel binary), as dependencies in case of OS-Wide settings like a timezeone file.
        /// </summary>
        public void AddCurrentHostOSDirectories()
        {
            Options |= Options.DependsOnCurrentOs;
        }

        /// <summary>
        /// Indicates that no file accesses should be tracked under the AppData directory
        /// </summary>
        /// <remarks>This includes both ApplicationData and LocalApplicationData folders</remarks>
        public void AddUntrackedAppDataDirectories()
        {
            Options |= Options.DependsOnWindowsAppData;
        }

        /// <summary>
        /// Indicates that no file accesses should be tracked under the c:\ProgramData user-independent app data directory.
        /// </summary>
        public void AddUntrackedProgramDataDirectories()
        {
            Options |= Options.DependsOnWindowsProgramData;
        }

        /// <nodoc />
        public void SetStandardOutputFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Assert(!m_standardOutputFile.IsValid, "Value already set");

            AddOutputFile(path, FileExistence.Required);
            m_standardOutputFile = FileArtifact.CreateOutputFile(path);
        }

        /// <nodoc />
        public void SetStandardErrorFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Assert(!m_standardErrorFile.IsValid, "Value already set");

            AddOutputFile(path, FileExistence.Required);
            m_standardErrorFile = FileArtifact.CreateOutputFile(path);
        }

        /// <nodoc />
        public void SetTraceFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Assert(!m_traceFile.IsValid, "Value already set");

            AddOutputFile(path, FileExistence.Required);
            m_traceFile = FileArtifact.CreateOutputFile(path);
        }

        /// <nodoc />
        public void SetChangeAffectedInputListWrittenFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Assert(!m_changeAffectedInputListWrittenFile.IsValid, "Value already set");

            m_changeAffectedInputListWrittenFile = FileArtifact.CreateSourceFile(path);
            AddUntrackedFile(path);
        }

        /// <nodoc />
        public void EnableTempDirectory()
        {
            if (!TempDirectory.IsValid)
            {
                m_enableAutomaticTempDirectory = true;
            }
        }

        /// <nodoc />
        public void SetTempDirectory(DirectoryArtifact tempDirectory)
        {
            Contract.Requires(tempDirectory.IsValid);
            Contract.Assert(!TempDirectory.IsValid, "Value already set");

            TempDirectory = tempDirectory;
            AddUntrackedDirectoryScope(tempDirectory);

            foreach (var tmpVar in BuildParameters.DisallowedTempVariables)
            {
                SetEnvironmentVariable(StringId.Create(m_pathTable.StringTable, tmpVar), tempDirectory.Path, isPassThrough: false);
            }
        }

        /// <nodoc />
        public void SetEnvironmentVariable(StringId key, PipDataAtom value, bool isPassThrough)
        {
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid);

            var pipData = PipDataBuilder.CreatePipData(m_pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, value);
            SetEnvironmentVariable(key, pipData, isPassThrough);
        }

        /// <nodoc />
        public void SetEnvironmentVariable(StringId key, PipData value, bool isPassThrough)
        {
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid || isPassThrough);

            m_environmentVariables[key] = (value, isPassThrough);
        }

        /// <nodoc />
        public void SetPassthroughEnvironmentVariable(StringId key)
        {
            Contract.Requires(key.IsValid);

            m_environmentVariables[key] = (PipData.Invalid, true);
        }

        /// <nodoc />
        public void SetProcessRetries(int? processRetries)
        {
            m_processRetries = processRetries;
        }

        private ReadOnlyArray<EnvironmentVariable> FinishEnvironmentVariables()
        {
            if (m_environmentVariables == null)
            {
                return ReadOnlyArray<EnvironmentVariable>.Empty;
            }

            EnvironmentVariable[] envVars = new EnvironmentVariable[m_environmentVariables.Count];
            int idx = 0;
            foreach (var kvp in m_environmentVariables)
            {
                envVars[idx++] = new EnvironmentVariable(kvp.Key, kvp.Value.Item1, kvp.Value.Item2);
            }

            return ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(envVars);
        }

        /// <nodoc />
        public void SetSemaphore(string name, int limit, int incrementBy)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(incrementBy > 0);
            Contract.Requires(limit > 0);

            m_semaphores = m_semaphores ?? new Dictionary<StringId, ProcessSemaphoreInfo>();
            var key = StringId.Create(m_pathTable.StringTable, name);
            m_semaphores[key] = new ProcessSemaphoreInfo(key, incrementBy, limit);
        }

        /// <summary>
        /// Set the <see cref="ResponseFileSpecification"/> that will be used to create a response file (if needed)
        /// </summary>
        public void SetResponseFileSpecification(ResponseFileSpecification specification)
        {
            m_responseFileSpecification = specification;
        }

        /// <nodoc/>
        public void SetRetryAttemptEnvironmentVariable(StringId environmentVariableName)
        {
            Contract.Assert(environmentVariableName.IsValid);

            // This env var is always a passthrough from a caching perspective.
            SetPassthroughEnvironmentVariable(environmentVariableName);

            m_retryAttemptEnvironmentVariable = environmentVariableName;
        }

        /// <nodoc/>
        public void SetServiceTrackableTag(StringId tag, StringId displayName)
        {
            m_serviceTrackableTag = tag;
            m_serviceTrackableTagDisplayName = displayName;
        }

        private PipData FinishArgumentsAndCreateResponseFileIfNeeded(DirectoryArtifact defaultDirectory)
        {
            Contract.Requires(defaultDirectory.IsValid);
            return m_responseFileSpecification.SplitArgumentsAndCreateResponseFileIfNeeded(this, defaultDirectory, m_pathTable);
        }

        private void ScanEnvVarsForCredentials(string pipDescription, PipConstructionHelper pipConstructionHelper)
        {
            List<string> envVarsWithCredentialsToLogList = new List<string>();
            // There are two possible implementations to handle the scenario when a credential is detected in the env var.
            // 1.) The env var is set as a PassthroughEnvVariable, SetPassThroughEnvironmentVariable method is used for that.
            // 2.) An error is thrown to break the build and the user is suggested to pass that env var using the /credScanEnvironmentVariablesAllowList flag to ensure that the variable is not scanned for credentials.
            // The results of credscan are being logged. Based on the results obtained from telemetry one of the above two implementations is opted.
            // Adding a stopwatch here to measure the performance of the credscan implementation.
            using (m_credentialScanner.Counters.StartStopwatch(CredScanCounter.CredScanOfEnvironmentVariablesDuration))
            {
                foreach (var kvp in m_environmentVariables)
                {
                    string envVarKey = m_pathTable.StringTable.GetString(kvp.Key);
                    if (kvp.Value.Item1.IsValid && !(m_credScanEnvironmentVariablesAllowList?.Contains(envVarKey)==true) 
                        && m_credentialScanner.CredentialsDetected(envVarKey, kvp.Value.Item1.ToString(m_pathTable)))
                    {
                        envVarsWithCredentialsToLogList.Add(envVarKey);
                    }
                }

                if (envVarsWithCredentialsToLogList != null && envVarsWithCredentialsToLogList.Any())
                {
                    var envVarsWithCredentialsToLog = string.Join(",", envVarsWithCredentialsToLogList);
                    Logger.Log.CredScanDetection(m_loggingContext, pipDescription, envVarsWithCredentialsToLog);
                }
            }
        }

        /// <summary>
        /// Wraps up all generated state, bundles it in the Process pip and the ProcessOutputs object to be
        /// handed down to those who need to get the files out and then releases all the temporary structures
        /// back to their respective pools.
        /// </summary>
        public bool TryFinish(PipConstructionHelper pipConstructionHelper, out Process process, out ProcessOutputs processOutputs)
        {
            // Provenance and default directory
            var provenance = pipConstructionHelper.CreatePipProvenance(Usage);
            PathAtom folderName = Executable.Path.GetName(m_pathTable).Concat(m_pathTable.StringTable, PathAtom.Create(m_pathTable.StringTable, ".std"));
            var defaultDirectory = pipConstructionHelper.GetUniqueObjectDirectory(folderName);

            // If the process is configured to run in a container, let's get a unique redirected root for it
            var redirectedDirectoryRoot = DirectoryArtifact.Invalid;
            if ((Options & Options.NeedsToRunInContainer) != Options.None)
            {
                redirectedDirectoryRoot = pipConstructionHelper.GetUniqueRedirectedDirectory(folderName);
            }

            // arguments
            var arguments = FinishArgumentsAndCreateResponseFileIfNeeded(defaultDirectory);

            // Handle temp files
            if (m_enableAutomaticTempDirectory && !TempDirectory.IsValid)
            {
                SetTempDirectory(pipConstructionHelper.GetUniqueTempDirectory());
            }

            // Handle output files
            var outputFileMap = new Dictionary<AbsolutePath, FileArtifactWithAttributes>();
            foreach (var file in m_outputFiles.Instance)
            {
                outputFileMap.Add(file.Path, file);
            }
            var outputFiles = ReadOnlyArray<FileArtifactWithAttributes>.From(m_outputFiles.Instance);

            // Handle output directories
            var directoryOutputMap = new Dictionary<AbsolutePath, StaticDirectory>();
            int i = 0;
            var directoryOutputArray = new DirectoryArtifact[m_outputDirectories.Instance.Count + m_projectedSharedOutputDirectories.Instance.Count];
            foreach (var directoryArtifact in m_outputDirectories.Instance)
            {
                // Add the directory
                var staticDirectory = new StaticDirectory(directoryArtifact, SealDirectoryKind.Opaque, PipConstructionHelper.EmptyStaticDirContents);
                directoryOutputMap.Add(directoryArtifact.Path, staticDirectory);
                directoryOutputArray[i] = directoryArtifact;
                i++;
            }
            foreach (var sharedDirectoryOutput in m_projectedSharedOutputDirectories.Instance)
            {
                var partialDirectoryArtifact = pipConstructionHelper.ReserveSharedOpaqueDirectory(sharedDirectoryOutput.Path);
                var staticDirectory = new StaticDirectory(partialDirectoryArtifact, SealDirectoryKind.SharedOpaque, PipConstructionHelper.EmptyStaticDirContents);

                directoryOutputMap.Add(sharedDirectoryOutput, staticDirectory);
                directoryOutputArray[i] = partialDirectoryArtifact;
                i++;
            }
            var directoryOutputs = directoryOutputArray.Length == 0 
                ? ReadOnlyArray<DirectoryArtifact>.Empty
                : ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(directoryOutputArray);

            var outputDirectoryExclusions = m_outputDirectoryExclusions.Instance.Count == 0 
                ? CollectionUtilities.EmptySet<AbsolutePath>()
                : new ReadOnlyHashSet<AbsolutePath>(m_outputDirectoryExclusions.Instance);

            // Handle temp directories
            foreach (var additionalTempDirectory in AdditionalTempDirectories)
            {
                if (additionalTempDirectory.IsValid)
                {
                    AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(additionalTempDirectory));
                }
            }

            // Handle copies for rewrites:
            if (m_filesToCopyBeforeWrite != null)
            {
                foreach (var (from, to) in m_filesToCopyBeforeWrite)
                {
                    if (!pipConstructionHelper.TryCopyFile(from, to, CopyFile.Options.None, new string[0], string.Empty, out _))
                    {
                        // Error should have been reported
                        processOutputs = null;
                        process = null;
                        return false;
                    }
                }
            }

            var serviceInfo = ServiceKind == ServicePipKind.None
                ? ServiceInfo.None
                : new ServiceInfo(
                       kind: ServiceKind,
                       shutdownProcessPipId: ShutDownProcessPipId,
                       servicePipDependencies: ReadOnlyArray<PipId>.From(m_servicePipDependencies.Instance),
                       finalizationPipIds: FinalizationPipIds,
                       tagToTrack: m_serviceTrackableTag,
                       displayNameForTag: m_serviceTrackableTagDisplayName);

            processOutputs = new ProcessOutputs(
                outputFileMap,
                directoryOutputMap
                );

            
            // Trusting statically declared accesses is not compatible with declaring opaque or source sealed directories
            if ((Options & Options.TrustStaticallyDeclaredAccesses) != Options.None && 
                    (directoryOutputs.Length > 0 || 
                    m_inputDirectories.Instance.Any(directory => pipConstructionHelper.PipGraph.TryGetSealDirectoryKind(directory, out var kind) && kind.IsSourceSeal()))
            )
            {
                processOutputs = null;
                process = null;

                return false;
            }

            process = new Process(
                executable: Executable,
                workingDirectory: WorkingDirectory.IsValid ? WorkingDirectory : defaultDirectory,
                arguments: arguments,
                responseFile: ResponseFile,
                responseFileData: ResponseFileData,

                environmentVariables: FinishEnvironmentVariables(),
                standardInput: StandardInput,
                standardOutput: m_standardOutputFile,
                standardError: m_standardErrorFile,
                standardDirectory: (m_standardOutputFile.IsValid && m_standardErrorFile.IsValid) ? AbsolutePath.Invalid : defaultDirectory,
                traceFile: m_traceFile,

                dependencies: ReadOnlyArray<FileArtifact>.From(m_inputFiles.Instance),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(m_inputDirectories.Instance),
                orderDependencies: ReadOnlyArray<PipId>.From(m_orderPipDependencies.Instance),

                outputs: outputFiles,
                directoryOutputs: directoryOutputs,

                tempDirectory: TempDirectory,
                additionalTempDirectories: AdditionalTempDirectories,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(m_untrackedFilesAndDirectories.Instance),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(m_untrackedDirectoryScopes.Instance.Select(dir => dir.Path)),

                tags: Tags,
                provenance: provenance,
                toolDescription: ToolDescription,

                successExitCodes: SuccessExitCodes,
                retryExitCodes: RetryExitCodes,
                succeedFastExitCodes: SucceedFastExitCodes,
                semaphores: m_semaphores != null ? ReadOnlyArray<ProcessSemaphoreInfo>.From(m_semaphores.Values) : ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                warningTimeout: WarningTimeout,
                timeout: Timeout,
                warningRegex: WarningRegex ?? RegexDescriptor.CreateDefaultForWarnings(m_pathTable.StringTable),
                errorRegex: ErrorRegex ?? RegexDescriptor.CreateDefaultForErrors(m_pathTable.StringTable),
                enableMultiLineErrorScanning: EnableMultiLineErrorScanning,

                uniqueOutputDirectory: defaultDirectory,
                uniqueRedirectedDirectoryRoot: redirectedDirectoryRoot,
                options: Options,
                serviceInfo: serviceInfo,
                allowedSurvivingChildProcessNames: AllowedSurvivingChildProcessNames,
                nestedProcessTerminationTimeout: NestedProcessTerminationTimeout,
                rewritePolicy: RewritePolicy,
                containerIsolationLevel: ContainerIsolationLevel,
                absentPathProbeMode: AbsentPathProbeUnderOpaquesMode,
                weight: Weight,
                priority: Priority,
                preserveOutputAllowlist: PreserveOutputAllowlist,
                changeAffectedInputListWrittenFile: m_changeAffectedInputListWrittenFile,
                preserveOutputsTrustLevel: PreserveOutputsTrustLevel,
                childProcessesToBreakawayFromSandbox: ChildProcessesToBreakawayFromSandbox,
                outputDirectoryExclusions: ReadOnlyArray<AbsolutePath>.From(outputDirectoryExclusions),
                processRetries: m_processRetries,
                retryAttemptEnvironmentVariable: m_retryAttemptEnvironmentVariable
                );

            if (m_credentialScanner.EnableCredScan)
            {
                // CredScan helper method.
                ScanEnvVarsForCredentials(process.GetDescription(pipConstructionHelper.Context), pipConstructionHelper);
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_argumentsBuilder.Dispose();

            m_inputFiles.Dispose();
            m_inputDirectories.Dispose();
            m_servicePipDependencies.Dispose();
            m_orderPipDependencies.Dispose();

            m_outputFiles.Dispose();
            m_outputDirectories.Dispose();
            m_projectedSharedOutputDirectories.Dispose();
            m_outputDirectoryExclusions.Dispose();

            m_untrackedFilesAndDirectories.Dispose();
            m_untrackedDirectoryScopes.Dispose();
        }
    }
}
