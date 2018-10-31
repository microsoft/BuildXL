// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Pips.Operations.Process;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// A helper class to build a process pip
    /// </summary>
    public class ProcessBuilder : IDisposable
    {
        private const int MaxCommandLineLength = 8192;

        private static readonly string s_windowsPath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string s_internetCachePath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache);
        private static readonly string s_historyPath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History);
        private static readonly string s_applicationDataPath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string s_localApplicationDataPath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string s_commonApplicationDataPath = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // State
        private readonly PathTable m_pathTable;

        // Process launch

        /// <nodoc />
        public FileArtifact Executable { get; set; }

        /// <nodoc />
        public DirectoryArtifact WorkingDirectory { get; set; }

        private readonly Dictionary<StringId, PipData> m_environmentVariables;

        private readonly PooledObjectWrapper<PipDataBuilder> m_argumentsBuilder;

        /// <nodoc />
        public PipDataBuilder ArgumentsBuilder => m_argumentsBuilder.Instance;

        /// <nodoc />
        public FileArtifact ResponseFile { get; set; }

        /// <nodoc />
        public PipData ResponseFileData { get; set; }

        private bool m_responseFileForceCreation;
        private PipDataBuilder.Cursor m_responseFileFirstArg = PipDataBuilder.Cursor.Default; // indicates "no response file"
        private string m_responseFilePrefix;

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

        // Outputs 
        private readonly PooledObjectWrapper<HashSet<FileArtifactWithAttributes>> m_outputFiles;

        // Input/Output streams

        /// <nodoc />
        public StandardInput StandardInput { get; set; }

        private FileArtifact m_standardOutputFile;

        private FileArtifact m_standardErrorFile;

        // Runtime behaviors

        /// <nodoc />
        public ReadOnlyArray<int> SuccessExitCodes { get; set; } = ReadOnlyArray<int>.Empty;

        /// <nodoc />
        public ReadOnlyArray<int> RetryExitCodes { get; set; } = ReadOnlyArray<int>.Empty;

        private Dictionary<StringId, ProcessSemaphoreInfo> m_semaphores;

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
        public Options Options { get; set; }

        /// <nodoc />
        public UnsafeOptions UnsafeOptions { get; set; }

        // Service related

        /// <nodoc />
        public PipId ShutDownProcessPipId { get; set; }

        /// <nodoc />
        public ReadOnlyArray<PipId> FinalizationPipIds { get; set; } = ReadOnlyArray<PipId>.Empty;


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
        private ProcessBuilder(PathTable pathTable, PooledObjectWrapper<PipDataBuilder> argumentsBuilder)
        {
            m_pathTable = pathTable;
            m_argumentsBuilder = argumentsBuilder;

            m_inputFiles = Pools.GetFileArtifactSet();
            m_inputDirectories = Pools.GetDirectoryArtifactSet();
            m_servicePipDependencies = PipPools.PipIdSetPool.GetInstance();

            m_outputFiles = Pools.GetFileArtifactWithAttributesSet();

            m_untrackedFilesAndDirectories = Pools.GetAbsolutePathSet();
            m_untrackedDirectoryScopes = Pools.GetDirectoryArtifactSet();

            m_environmentVariables = new Dictionary<StringId, PipData>();
        }

        /// <summary>
        /// Creates a new ProcessBuilder
        /// </summary>
        public static ProcessBuilder Create(PathTable pathTable, PooledObjectWrapper<PipDataBuilder> argumentsBuilder)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(argumentsBuilder.Instance != null);
            return new ProcessBuilder(pathTable, argumentsBuilder);
        }

        /// <summary>
        /// Helper to create a new ProcessBuilder for testing that doesn't need to pass the pooled PipDataBuilder for convenience
        /// </summary>
        public static ProcessBuilder CreateForTesting(PathTable pathTable)
        {
            var tempPool = new ObjectPool<PipDataBuilder>(() => new PipDataBuilder(pathTable.StringTable), _ => { });
            return new ProcessBuilder(pathTable, tempPool.GetInstance());
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
        public void AddRewrittenFileInPlace(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            m_inputFiles.Instance.Add(file);
            m_outputFiles.Instance.Add(new FileArtifactWithAttributes(file.Path, file.RewriteCount + 1, FileExistence.Required));
        }

        /// <nodoc />
        public void AddServicePipDependency(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);

            m_servicePipDependencies.Instance.Add(pipId);
        }

        /// <nodoc />
        public void AddUntrackedFile(AbsolutePath file)
        {
            Contract.Requires(file.IsValid);

            m_untrackedFilesAndDirectories.Instance.Add(file);
        }

        /// <summary>
        /// Bug: For now this is doing the same as <see cref="AddUntrackedDirectoryScope(DirectoryArtifact)"/> for back-compat
        /// reasons. TODO: fix consumers and change it to only untrack the directory, not its cone. Bug #1335917.
        /// </summary>
        public void AddUntrackedDirectory(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);

            m_untrackedDirectoryScopes.Instance.Add(directory);
        }

        /// <nodoc />
        public void AddUntrackedDirectoryScope(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);

            m_untrackedDirectoryScopes.Instance.Add(directory);
        }

        /// <summary>
        /// Indicates that no file accesses should be tracked under the Windows directory.
        /// </summary>
        public void AddUntrackedWindowsDirectories()
        {
            var untrackedPaths = !OperatingSystemHelper.IsUnixOS
                ? new[] { s_windowsPath, s_internetCachePath, s_historyPath }
                : new string[] { }; // TODO: figure out what to add here for non-Windows systems

            foreach (var path in untrackedPaths)
            {
                AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_pathTable, path));
            }
        }

        /// <summary>
        /// Indicates that no file accesses should be tracked under the AppData directory
        /// </summary>
        /// <remarks>This includes both ApplicationData and LocalApplicationData folders</remarks>
        public void AddUntrackedAppDataDirectories()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_pathTable, s_applicationDataPath));
                AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_pathTable, s_localApplicationDataPath));
            }
        }

        /// <summary>
        /// Indicates that no file accesses should be tracked under the c:\ProgramData user-independent app data directory.
        /// </summary>
        public void AddUntrackedProgramDataDirectories()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_pathTable, s_commonApplicationDataPath));
            }
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
                SetEnvironmentVariable(StringId.Create(m_pathTable.StringTable, tmpVar), tempDirectory.Path);
            }
        }

        /// <nodoc />
        public void SetEnvironmentVariable(StringId key, PipDataAtom value)
        {
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid);

            var pipData = PipDataBuilder.CreatePipData(m_pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, value);
            SetEnvironmentVariable(key, pipData);
        }

        /// <nodoc />
        public void SetEnvironmentVariable(StringId key, PipData value)
        {
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid);

            m_environmentVariables[key] = value;
        }

        /// <nodoc />
        public void SetPassthroughEnvironmentVariable(StringId key)
        {
            Contract.Requires(key.IsValid);

            m_environmentVariables[key] = PipData.Invalid;
        }

        private ReadOnlyArray<EnvironmentVariable> FinishEnvironmentVariables()
        {
            if (m_environmentVariables == null)
            {
                return ReadOnlyArray<EnvironmentVariable>.Empty;
            }

            EnvironmentVariable[] envVars = new EnvironmentVariable[m_environmentVariables.Count];
            int idx = 0;
            foreach (var kvp in m_environmentVariables.OrderBy(kv => kv.Key, m_pathTable.StringTable.OrdinalComparer))
            {
                // if the value is invalid, then it is a pass through env variable.
                var isPassThrough = !kvp.Value.IsValid;
                envVars[idx++] = new EnvironmentVariable(kvp.Key, kvp.Value, isPassThrough);
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
        /// Specifies whether the process can use response files.
        /// </summary>
        /// <nodoc />
        public void SetResponseFileForRemainingArguments(PipDataBuilder.Cursor cursor, bool force, string responseFilePrefix)
        {
            m_responseFileForceCreation = force;
            m_responseFileFirstArg = cursor;
            m_responseFilePrefix = responseFilePrefix;
        }

        private PipData FinishArgumentsAndOverflowArgumentsToResponseFile(DirectoryArtifact defaultDirectory)
        {
            Contract.Requires(defaultDirectory.IsValid);

            PipData arguments = default;
            var argumentsBuilder = m_argumentsBuilder.Instance;

            if (!m_responseFileFirstArg.IsDefault)
            {
                bool mkRespFile = m_responseFileForceCreation;
                if (!mkRespFile)
                {
                    // make a response file only if the command-line is too long
                    arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);

                    // Normalize choice to use response file by assuming paths are of length max path with a space. This will
                    // ensure there are no cases where changing the root will change whether a response file is used.
                    int cmdLineLength = arguments.GetMaxPossibleLength(m_pathTable.StringTable);
                    mkRespFile = cmdLineLength > MaxCommandLineLength;
                }

                if (mkRespFile)
                {
                    // create a pip data for the stuff in the response file
                    ResponseFileData = argumentsBuilder.ToPipData("\r\n", PipDataFragmentEscaping.CRuntimeArgumentRules, m_responseFileFirstArg);

                    // generate the file
                    ResponseFile = FileArtifact.CreateSourceFile(defaultDirectory.Path.Combine(m_pathTable, PathAtom.Create(m_pathTable.StringTable, "args.rsp")));

                    argumentsBuilder.TrimEnd(m_responseFileFirstArg);

                    AddUntrackedFile(ResponseFile);
                    if (string.IsNullOrEmpty(m_responseFilePrefix))
                    {
                        argumentsBuilder.Add(ResponseFile);
                    }
                    else
                    {
                        using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, m_pathTable.StringTable.Empty))
                        {
                            argumentsBuilder.Add(m_responseFilePrefix);
                            argumentsBuilder.Add(ResponseFile);
                        }
                    }

                    arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
                }
            }
            else
            {
                arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
            }

            return arguments;
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

            // arguments
            var arguments = FinishArgumentsAndOverflowArgumentsToResponseFile(defaultDirectory);

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

            // Handle temp directories
            foreach (var additionalTempDirectory in AdditionalTempDirectories)
            {
                if (additionalTempDirectory.IsValid)
                {
                    AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(additionalTempDirectory));
                }
            }

            processOutputs = new ProcessOutputs(outputFileMap);

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

                dependencies: ReadOnlyArray<FileArtifact>.From(m_inputFiles.Instance),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(m_inputDirectories.Instance),
                orderDependencies: ReadOnlyArray<PipId>.Empty, // There is no code setting this yet.

                outputs: outputFiles,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty, 

                tempDirectory: TempDirectory,
                additionalTempDirectories: AdditionalTempDirectories,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(m_untrackedFilesAndDirectories.Instance),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(m_untrackedDirectoryScopes.Instance.Select(dir => dir.Path)),

                tags: Tags,
                provenance: provenance,
                toolDescription: ToolDescription,

                successExitCodes: SuccessExitCodes,
                retryExitCodes: RetryExitCodes,
                semaphores: m_semaphores != null ? ReadOnlyArray<ProcessSemaphoreInfo>.From(m_semaphores.Values) : ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                warningTimeout: WarningTimeout,
                timeout: Timeout,
                warningRegex: WarningRegex ?? RegexDescriptor.CreateDefaultForWarnings(m_pathTable.StringTable),
                errorRegex: ErrorRegex ?? RegexDescriptor.CreateDefaultForErrors(m_pathTable.StringTable),

                uniqueOutputDirectory: defaultDirectory,
                options: Options,
                unsafeOptions: UnsafeOptions,
                allowedSurvivingChildProcessNames: AllowedSurvivingChildProcessNames,
                nestedProcessTerminationTimeout: NestedProcessTerminationTimeout
            );

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_argumentsBuilder.Dispose();

            m_inputFiles.Dispose();
            m_inputDirectories.Dispose();
            m_servicePipDependencies.Dispose();

            m_outputFiles.Dispose();

            m_untrackedFilesAndDirectories.Dispose();
            m_untrackedDirectoryScopes.Dispose();
        }
     }
}
