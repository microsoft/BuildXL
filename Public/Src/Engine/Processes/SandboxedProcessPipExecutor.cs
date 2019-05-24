// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.VmCommandProxy;
using static BuildXL.Utilities.BuildParameters;

namespace BuildXL.Processes
{
    /// <summary>
    /// Adapter from <see cref="Process" /> pips to real (uncached) execution in a <see cref="SandboxedProcess" />.
    /// </summary>
    public sealed class SandboxedProcessPipExecutor : ISandboxedProcessFileStorage
    {
        /// <summary>
        /// Max console length for standard output/error before getting truncated.
        /// </summary>
        public const int MaxConsoleLength = 2048; // around 30 lines

        private static readonly string s_appDataLocalMicrosoftClrPrefix =
            Path.Combine(SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "CLR");

        private static readonly string s_nvidiaProgramDataPrefix =
            Path.Combine(SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation");

        private static readonly string s_nvidiaProgramFilesPrefix =
            Path.Combine(SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation");

        private static readonly string s_forefrontTmgClientProgramFilesX86Prefix =
            Path.Combine(SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Forefront TMG Client");

        private static readonly string s_userProfilePath =
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

        private static readonly string s_possibleRedirectedUserProfilePath =
            SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

        // Compute if WCI and Bind are available on this machine
        private static readonly bool s_isIsolationSupported = ProcessUtilities.IsWciAndBindFiltersAvailable();

        /// <summary>
        /// The maximum number that this executor will try to launch the given process pip.
        /// </summary>
        public const int ProcessLaunchRetryCountMax = 5;

        /// <summary>
        /// When directing full output to the console, number of lines to read before writing out the log event. This
        /// prevents a pip with very long output from consuming large amounts of memory in this process
        /// </summary>
        public const int OutputChunkInLines = 10000;

        private static readonly ConcurrentDictionary<ExpandedRegexDescriptor, Lazy<Task<Regex>>> s_regexTasks =
            new ConcurrentDictionary<ExpandedRegexDescriptor, Lazy<Task<Regex>>>();

        private readonly PipExecutionContext m_context;
        private readonly PathTable m_pathTable;

        private readonly ISandboxConfiguration m_sandboxConfig;

        private readonly IReadOnlyDictionary<string, string> m_rootMappings;

        private readonly FileAccessManifest m_fileAccessManifest;

        private readonly bool m_disableConHostSharing;

        private readonly Action<int> m_processIdListener;

        private readonly PipFragmentRenderer m_pipDataRenderer;

        private readonly FileAccessWhitelist m_fileAccessWhitelist;

        private readonly Process m_pip;

        private readonly Task<Regex> m_warningRegexTask;

        private readonly Task<Regex> m_errorRegexTask;

        private readonly Func<FileArtifact, Task<bool>> m_makeInputPrivate;

        private readonly Func<string, Task<bool>> m_makeOutputPrivate;

        private readonly bool m_warningRegexIsDefault;

        private readonly string m_workingDirectory;

        private string m_standardDirectory;

        private Regex m_warningRegex;

        private Regex m_errorRegex;

        private int m_numWarnings;

        private TimeSpan m_timeout;

        private readonly FileAccessPolicy m_excludeReportAccessMask;

        private readonly SemanticPathExpander m_semanticPathExpander;

        private readonly ISandboxedProcessLogger m_logger;

        private readonly bool m_shouldPreserveOutputs;

        private readonly PipEnvironment m_pipEnvironment;

        private readonly AbsolutePath m_buildEngineDirectory;

        private readonly bool m_validateDistribution;

        private readonly IDirectoryArtifactContext m_directoryArtifactContext;

        private string m_detoursFailuresFile;

        private readonly ILayoutConfiguration m_layoutConfiguration;

        private readonly ILoggingConfiguration m_loggingConfiguration;

        private readonly LoggingContext m_loggingContext;

        private readonly int m_remainingUserRetryCount;

        private readonly ITempDirectoryCleaner m_tempDirectoryCleaner;

        private readonly ReadOnlyHashSet<AbsolutePath> m_sharedOpaqueDirectoryRoots;

        private readonly ProcessInContainerManager m_processInContainerManager;
        private readonly ContainerConfiguration m_containerConfiguration;

        private readonly VmInitializer m_vmInitializer;

        /// <summary>
        /// The active sandboxed process (if any)
        /// </summary>
        private ISandboxedProcess m_activeProcess;

        /// <summary>
        /// Creates an executor for a process pip. Execution can then be started with <see cref="RunAsync" />.
        /// </summary>
        public SandboxedProcessPipExecutor(
            PipExecutionContext context,
            LoggingContext loggingContext,
            Process pip,
            ISandboxConfiguration sandBoxConfig,
            ILayoutConfiguration layoutConfig,
            ILoggingConfiguration loggingConfig,
            IReadOnlyDictionary<string, string> rootMappings,
            ProcessInContainerManager processInContainerManager,
            FileAccessWhitelist whitelist,
            Func<FileArtifact, Task<bool>> makeInputPrivate,
            Func<string, Task<bool>> makeOutputPrivate,
            SemanticPathExpander semanticPathExpander,
            bool disableConHostSharing,
            PipEnvironment pipEnvironment,
            bool validateDistribution,
            IDirectoryArtifactContext directoryArtifactContext,
            ISandboxedProcessLogger logger = null,
            Action<int> processIdListener = null,
            PipFragmentRenderer pipDataRenderer = null,
            AbsolutePath buildEngineDirectory = default,
            DirectoryTranslator directoryTranslator = null,
            int remainingUserRetryCount = 0,
            bool isQbuildIntegrated = false,
            VmInitializer vmInitializer = null,
            ITempDirectoryCleaner tempDirectoryCleaner = null,
            SubstituteProcessExecutionInfo shimInfo = null)
        {
            Contract.Requires(pip != null);
            Contract.Requires(context != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(sandBoxConfig != null);
            Contract.Requires(rootMappings != null);
            Contract.Requires(pipEnvironment != null);
            Contract.Requires(directoryArtifactContext != null);
            Contract.Requires(processInContainerManager != null);

            m_context = context;
            m_loggingContext = loggingContext;
            m_pathTable = context.PathTable;
            m_pip = pip;
            m_sandboxConfig = sandBoxConfig;
            m_rootMappings = rootMappings;
            m_workingDirectory = pip.WorkingDirectory.ToString(m_pathTable);
            m_fileAccessManifest =
                new FileAccessManifest(m_pathTable, directoryTranslator)
                {
                    MonitorNtCreateFile = sandBoxConfig.UnsafeSandboxConfiguration.MonitorNtCreateFile,
                    MonitorZwCreateOpenQueryFile = sandBoxConfig.UnsafeSandboxConfiguration.MonitorZwCreateOpenQueryFile,
                    ForceReadOnlyForRequestedReadWrite = sandBoxConfig.ForceReadOnlyForRequestedReadWrite,
                    IgnoreReparsePoints = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreReparsePoints,
                    IgnorePreloadedDlls = sandBoxConfig.UnsafeSandboxConfiguration.IgnorePreloadedDlls,
                    IgnoreZwRenameFileInformation = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreZwRenameFileInformation,
                    IgnoreZwOtherFileInformation = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreZwOtherFileInformation,
                    IgnoreNonCreateFileReparsePoints = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreNonCreateFileReparsePoints,
                    IgnoreSetFileInformationByHandle = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreSetFileInformationByHandle,
                    NormalizeReadTimestamps =
                        sandBoxConfig.NormalizeReadTimestamps &&
                        // Do not normalize read timestamps if preserved-output mode is enabled and pip wants its outputs to be preserved.
                        (sandBoxConfig.UnsafeSandboxConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled || !pip.AllowPreserveOutputs),
                    UseLargeNtClosePreallocatedList = sandBoxConfig.UseLargeNtClosePreallocatedList,
                    UseExtraThreadToDrainNtClose = sandBoxConfig.UseExtraThreadToDrainNtClose,
                    DisableDetours = sandBoxConfig.UnsafeSandboxConfiguration.DisableDetours(),
                    LogProcessData = sandBoxConfig.LogProcesses && sandBoxConfig.LogProcessData,
                    IgnoreGetFinalPathNameByHandle = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreGetFinalPathNameByHandle,
                    // SemiStableHash is 0 for pips with no provenance;
                    // since multiple pips can have no provenance, SemiStableHash is not always unique across all pips
                    PipId = m_pip.SemiStableHash != 0 ? m_pip.SemiStableHash : m_pip.PipId.Value,
                    QBuildIntegrated = isQbuildIntegrated,
                    SubstituteProcessExecutionInfo = shimInfo,
                };

            if (!sandBoxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses)
            {
                // If monitoring of file accesses is disabled, make sure a valid
                // manifest is still provided and disable detours for this pip.
                m_fileAccessManifest.DisableDetours = true;
            }

            m_fileAccessWhitelist = whitelist;
            m_makeInputPrivate = makeInputPrivate;
            m_makeOutputPrivate = makeOutputPrivate;
            m_semanticPathExpander = semanticPathExpander;
            m_logger = logger ?? new SandboxedProcessLogger(m_loggingContext, pip, context);
            m_disableConHostSharing = disableConHostSharing;
            m_shouldPreserveOutputs = m_pip.AllowPreserveOutputs && m_sandboxConfig.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled;
            m_processIdListener = processIdListener;
            m_pipEnvironment = pipEnvironment;
            m_pipDataRenderer = pipDataRenderer ?? new PipFragmentRenderer(m_pathTable);

            if (pip.WarningRegex.IsValid)
            {
                var expandedDescriptor = new ExpandedRegexDescriptor(pip.WarningRegex.Pattern.ToString(context.StringTable), pip.WarningRegex.Options);
                m_warningRegexTask = GetRegexAsync(expandedDescriptor);
                m_warningRegexIsDefault = RegexDescriptor.IsDefault(expandedDescriptor.Pattern, expandedDescriptor.Options);
            }

            if (pip.ErrorRegex.IsValid)
            {
                var expandedDescriptor = new ExpandedRegexDescriptor(pip.ErrorRegex.Pattern.ToString(context.StringTable), pip.ErrorRegex.Options);
                m_errorRegexTask = GetRegexAsync(expandedDescriptor);
            }

            m_excludeReportAccessMask = ~FileAccessPolicy.ReportAccess;
            if (!m_sandboxConfig.MaskUntrackedAccesses)
            {
                // TODO: Remove this when we ascertain that our customers are not
                // negatively impacted by excluding these reports
                // Use 'legacy' behavior where directory enumerations are tracked even
                // when report access is masked.
                m_excludeReportAccessMask |= FileAccessPolicy.ReportDirectoryEnumerationAccess;
            }

            m_buildEngineDirectory = buildEngineDirectory;
            m_validateDistribution = validateDistribution;
            m_directoryArtifactContext = directoryArtifactContext;
            m_layoutConfiguration = layoutConfig;
            m_loggingConfiguration = loggingConfig;
            m_remainingUserRetryCount = remainingUserRetryCount;
            m_tempDirectoryCleaner = tempDirectoryCleaner;

            m_sharedOpaqueDirectoryRoots = m_pip.DirectoryOutputs
                .Where(directory => directory.IsSharedOpaque)
                .Select(directory => directory.Path)
                .ToReadOnlySet();

            m_processInContainerManager = processInContainerManager;

            if ((m_pip.ProcessOptions & Process.Options.NeedsToRunInContainer) != 0)
            {
                m_containerConfiguration = m_processInContainerManager.CreateContainerConfigurationForProcess(m_pip);
            }
            else
            {
                m_containerConfiguration = ContainerConfiguration.DisabledIsolation;
            }

            m_vmInitializer = vmInitializer;
        }

        /// <inheritdoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            FileArtifact fileArtifact = file.PipFileArtifact(m_pip);

            string filename = null;
            if (fileArtifact.IsValid)
            {
                // If the file is valid, that means it also got included as a declared output file
                // so if isolation is enabled for files, it must be redirected as well
                if (m_containerConfiguration.IsIsolationEnabled && m_pip.ContainerIsolationLevel.IsolateOutputFiles())
                {
                    var success = m_containerConfiguration.OriginalDirectories.TryGetValue(fileArtifact.Path.GetParent(m_pathTable), out var redirectedDirectories);
                    if (!success)
                    {
                        Contract.Assert(false, $"File artifact '{fileArtifact.Path.ToString(m_pathTable)}' of type ${file} should be part of the pip outputs, and therefore it should be a redirected file");
                    }

                    // An output file has a single redirected directory
                    var redirectedDirectory = redirectedDirectories.Single();

                    filename = Path.Combine(redirectedDirectory.ExpandedPath, fileArtifact.Path.GetName(m_pathTable).ToString(m_pathTable.StringTable));
                }
                else
                {
                    filename = fileArtifact.Path.ToString(m_pathTable);
                }
            }
            else
            {
                filename = Path.Combine(GetStandardDirectory(), file.DefaultFileName());
            }

            return filename;
        }

        /// <summary>
        /// <see cref="SandboxedProcess.GetActivePeakMemoryUsage"/>
        /// </summary>
        public ulong? GetActivePeakMemoryUsage()
        {
            return m_activeProcess?.GetActivePeakMemoryUsage();
        }

        private static Task<Regex> GetRegexAsync(ExpandedRegexDescriptor descriptor)
        {
            // ConcurrentDictionary.GetOrAdd might evaluate the delegate multiple times for the same key
            // if its called concurrently for the same key and there isn't an entry in the dictionary yet.
            // However, only one is actually stored in the dictionary.
            // To avoid creating multiple tasks that do redundant expensive work, we wrap the task creation in a Lazy.
            // While multiple lazies might get created, only one of them is actually evaluated: the one that was actually inserted into the dictionary.
            // All others are forgotten, and thus, we only ever do the expensive Regex work once.
            // The overhead of the Lazy is irrelevant in practice given that the race itself is unlikely, and considering that the actual
            // Regex object dwarfs the size of the Lazy overhead by several orders of magnitude.
            return s_regexTasks.GetOrAdd(
                descriptor,
                descriptor2 => Lazy.Create(
                    () => Task.Factory.StartNew(
                        () => new Regex(descriptor2.Pattern, descriptor2.Options | RegexOptions.Compiled | RegexOptions.CultureInvariant)))).Value;
        }

        private string GetDetoursInternalErrorFilePath(LoggingContext loggingContext)
        {
            string tempDir = null;

            if (m_layoutConfiguration != null)
            {
                // First try the object folder.
                // In fully instantiated engine this will never be null, but for some tests it is.
                tempDir = m_layoutConfiguration.ObjectDirectory.ToString(m_pathTable) + "\\Detours";
            }

            if (string.IsNullOrEmpty(tempDir) && m_standardDirectory != null)
            {
                // Then try the Standard directory.
                tempDir = m_standardDirectory;
            }

            // Normalize the temp location. And make sure it is valid.
            // Our tests sometime set the ObjectDirectory to be "\\Test\...".
            if (!string.IsNullOrEmpty(tempDir) && tempDir.StartsWith(@"\", StringComparison.OrdinalIgnoreCase))
            {
                tempDir = null;
            }
            else if (!string.IsNullOrEmpty(tempDir))
            {
                AbsolutePath absPath = AbsolutePath.Create(m_pathTable, tempDir);
                if (absPath.IsValid)
                {
                    tempDir = absPath.ToString(m_pathTable);
                }
                else
                {
                    tempDir = null;
                }
            }

            if (string.IsNullOrEmpty(tempDir))
            {
                // Get and use the BuildXL temp dir.
                tempDir = Path.GetTempPath();
            }

            if (!FileUtilities.DirectoryExistsNoFollow(tempDir))
            {
                try
                {
                    FileUtilities.CreateDirectory(tempDir);
                }
                catch (BuildXLException ex)
                {
                    Tracing.Logger.Log.LogFailedToCreateDirectoryForInternalDetoursFailureFile(
                        loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        tempDir,
                        ex.ToStringDemystified());
                    throw;
                }
            }

            if (!tempDir.EndsWith("\\", StringComparison.OrdinalIgnoreCase))
            {
                tempDir += "\\";
            }

            return tempDir + m_pip.SemiStableHash.ToString("X16", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString() + ".tmp";
        }

        private string GetStandardDirectory()
        {
            if (m_standardDirectory == null)
            {
                Contract.Assert(m_pip.StandardDirectory.IsValid, "Pip builder should guarantee that the assertion holds.");
                m_standardDirectory = m_pip.StandardDirectory.ToString(m_pathTable);
            }

            return m_standardDirectory;
        }

        /// <remarks>
        /// Adapted from Microsoft.Build.Utilities.Core / CanonicalError.cs / Parse
        /// </remarks>>
        private bool IsWarning(string line)
        {
            Contract.Requires(line != null);

            if (m_warningRegex == null)
            {
                return false;
            }

            // An unusually long string causes pathologically slow Regex back-tracking.
            // To avoid that, only scan the first 400 characters. That's enough for
            // the longest possible prefix: MAX_PATH, plus a huge subcategory string, and an error location.
            // After the regex is done, we can append the overflow.
            if (line.Length > 400)
            {
                line = line.Substring(0, 400);
            }

            // If a tool has a large amount of output that isn't a warning (eg., "dir /s %hugetree%")
            // the regex matching below may be slow. It's faster to pre-scan for "warning"
            // and bail out if neither are present.
            if (m_warningRegexIsDefault &&
                line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }

            return m_warningRegex.IsMatch(line);
        }

        private bool IsError(string line)
        {
            Contract.Requires(line != null, "line must not be null.");

            // in absence of regex, treating everything as error.
            if (m_errorRegex == null)
            {
                return true;
            }

            // An unusually long string causes pathologically slow Regex back-tracking.
            // To avoid that, only scan the first 400 characters. That's enough for
            // the longest possible prefix: MAX_PATH, plus a huge subcategory string, and an error location.
            // After the regex is done, we can append the overflow.
            if (line.Length > 400)
            {
                line = line.Substring(0, 400);
            }

            return m_errorRegex.IsMatch(line);
        }

        private void Observe(string line)
        {
            if (IsWarning(line))
            {
                Interlocked.Increment(ref m_numWarnings);
            }
        }

        /// <summary>
        /// Filters items from sandboxed output, depending on the predicate provided.
        /// </summary>
        /// <param name="output">Output stream (from sandboxed process) to read from.</param>
        /// <param name="filterPredicate"> Predicate, used to filter lines of interest.</param>
        /// <param name="appendNewLine">Whether to append newLine on non-empty content. Defaults to false.</param>
        private async Task<string> TryFilterAsync(SandboxedProcessOutput output, Predicate<string> filterPredicate, bool appendNewLine = false)
        {
            try
            {
                using (PooledObjectWrapper<StringBuilder> wrapper = Pools.StringBuilderPool.GetInstance())
                {
                    using (TextReader reader = output.CreateReader())
                    {
                        StringBuilder sb = wrapper.Instance;
                        while (true)
                        {
                            string line = await reader.ReadLineAsync();
                            if (line == null)
                            {
                                break;
                            }

                            if (filterPredicate(line))
                            {
                                // only add leading newlines (when needed).
                                // Trailing newlines would cause the message logged to have double newlines
                                if (appendNewLine && sb.Length > 0)
                                {
                                    sb.AppendLine();
                                }

                                sb.Append(line);
                                if (sb.Length >= MaxConsoleLength)
                                {
                                    // Make sure we have a newline before the ellipsis
                                    sb.AppendLine();
                                    sb.AppendLine("[...]");
                                    await output.SaveAsync();
                                    sb.Append(output.FileName);
                                    break;
                                }
                            }
                        }

                        return sb.ToString();
                    }
                }
            }
            catch (IOException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return null;
            }
            catch (AggregateException ex)
            {
                if (TryLogRootIOException(GetFileName(output.File), ex))
                {
                    return null;
                }

                throw;
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return null;
            }
        }

        /// <summary>
        /// Runs the process pip (uncached).
        /// </summary>
        public async Task<SandboxedProcessPipExecutionResult> RunAsync(CancellationToken cancellationToken = default, IKextConnection sandboxedKextConnection = null)
        {
            try
            {
                var sandboxPrepTime = System.Diagnostics.Stopwatch.StartNew();
                var environmentVariables = m_pipEnvironment.GetEffectiveEnvironmentVariables(m_pip, m_pipDataRenderer);

                if (!PrepareWorkingDirectory())
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                if (!PrepareTempDirectory(environmentVariables))
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                if (!await PrepareResponseFile())
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                using (var allInputPathsUnderSharedOpaquesWrapper = Pools.GetAbsolutePathSet())
                {
                    // Here we collect all the paths representing inputs under shared opaques dependencies
                    // These paths need to be flagged appropriately so timestamp faking happen for them. It is also used to identify accesses
                    // that belong to inputs vs accesses that belong to outputs under shared opaques
                    // Both dynamic inputs (the content of shared opaque dependencies) and static inputs are accumulated here.
                    // These paths represent not only the file artifacts, but also the directories that contain those artifacts, up
                    // to the root of the outmost shared opaque that contain them. This makes sure that if timestamps are retrieved
                    // on directories containing inputs, those are faked as well.
                    // The set is kept for two reasons 1) so we avoid duplicate work: as soon as a path is found to be already in this set, we can
                    // shortcut the upward traversal on a given path when doing timestamp faking setup and 2) so GetObservedFileAccesses
                    // doesn't need to recompute this and it can distinguish between accesses that only pertain to outputs vs inptus
                    // in the scope of a shared opaque
                    HashSet<AbsolutePath> allInputPathsUnderSharedOpaques = allInputPathsUnderSharedOpaquesWrapper.Instance;

                    if (m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses && !TryPrepareFileAccessMonitoring(m_loggingContext, allInputPathsUnderSharedOpaques))
                    {
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    if (!await PrepareOutputs())
                    {
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    string executable = m_pip.Executable.Path.ToString(m_pathTable);
                    string arguments = m_pip.Arguments.ToString(m_pipDataRenderer);
                    m_timeout = GetEffectiveTimeout(m_pip.Timeout, m_sandboxConfig.DefaultTimeout, m_sandboxConfig.TimeoutMultiplier);

                    SandboxedProcessInfo info = new SandboxedProcessInfo(
                        m_pathTable,
                        this,
                        executable,
                        m_fileAccessManifest,
                        m_disableConHostSharing,
                        m_containerConfiguration,
                        m_pip.TestRetries,
                        m_loggingContext,
                        sandboxedKextConnection: sandboxedKextConnection)
                    {
                        Arguments = arguments,
                        WorkingDirectory = m_workingDirectory,
                        RootMappings = m_rootMappings,
                        EnvironmentVariables = environmentVariables,
                        Timeout = m_timeout,
                        PipSemiStableHash = m_pip.SemiStableHash,
                        PipDescription = m_pip.GetDescription(m_context),
                        ProcessIdListener = m_processIdListener,
                        TimeoutDumpDirectory = ComputePipTimeoutDumpDirectory(m_sandboxConfig, m_pip, m_pathTable),
                        SandboxKind = m_sandboxConfig.UnsafeSandboxConfiguration.SandboxKind,
                        AllowedSurvivingChildProcessNames = m_pip.AllowedSurvivingChildProcessNames.Select(n => n.ToString(m_pathTable.StringTable)).ToArray(),
                        NestedProcessTerminationTimeout = m_pip.NestedProcessTerminationTimeout ?? SandboxedProcessInfo.DefaultNestedProcessTerminationTimeout,
                    };

                    if (m_sandboxConfig.AdminRequiredProcessExecutionMode == AdminRequiredProcessExecutionMode.Internal
                        || !m_pip.RequiresAdmin
                        || m_processIdListener != null
                        || m_containerConfiguration.IsIsolationEnabled
                        || OperatingSystemHelper.IsUnixOS)
                    {
                        return await RunInternalAsync(info, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
                    }
                    else
                    {
                        return await RunExternalAsync(info, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
                    }
                }
            }
            finally
            {
                Contract.Assert(m_fileAccessManifest != null);

                m_fileAccessManifest.UnsetMessageCountSemaphore();
            }
        }

        private async Task<SandboxedProcessPipExecutionResult> RunInternalAsync(
            SandboxedProcessInfo info,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            System.Diagnostics.Stopwatch sandboxPrepTime,
            CancellationToken cancellationToken = default)
        {
            using (Stream standardInputStream = TryOpenStandardInputStream(out bool openStandardInputStreamSuccess))
            {
                if (!openStandardInputStreamSuccess)
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                using (StreamReader standardInputReader = standardInputStream == null ? null : new StreamReader(standardInputStream, CharUtilities.Utf8NoBomNoThrow))
                {
                    info.StandardInputReader = standardInputReader;
                    info.StandardInputEncoding = standardInputReader?.CurrentEncoding;

                    Action<string> observer = m_warningRegexTask == null ? null : (Action<string>)Observe;

                    info.StandardOutputObserver = observer;
                    info.StandardErrorObserver = observer;

                    if (info.GetCommandLine().Length > SandboxedProcessInfo.MaxCommandLineLength)
                    {
                        LogCommandLineTooLong(info);
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    if (!await TryInitializeWarningRegexAsync())
                    {
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    sandboxPrepTime.Stop();
                    ISandboxedProcess process = null;

                    // Sometimes the injection of Detours fails with error ERROR_PARTIAL_COPY (0x12b)
                    // This is random failure, not consistent at all and it seems to be in the lower levels of
                    // Detours. If we get such error attempt running the process up to RetryStartCount times
                    // before bailing out and reporting an error.
                    int processLaunchRetryCount = 0;
                    long maxDetoursHeapSize = 0L;
                    bool shouldRelaunchProcess = true;

                    while (shouldRelaunchProcess)
                    {
                        try
                        {
                            shouldRelaunchProcess = false;

                            // If the process should be run in a container, we (verbose) log the remapping information
                            if (m_containerConfiguration.IsIsolationEnabled)
                            {
                                // If the process was specified to run in a container but isolation is not supported, then we bail out
                                // before even trying to run the process
                                if (!s_isIsolationSupported)
                                {
                                    Tracing.Logger.Log.PipSpecifiedToRunInContainerButIsolationIsNotSupported(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));
                                    return SandboxedProcessPipExecutionResult.PreparationFailure(processLaunchRetryCount, (int)EventId.PipSpecifiedToRunInContainerButIsolationIsNotSupported, maxDetoursHeapSize: maxDetoursHeapSize);
                                }

                                Tracing.Logger.Log.PipInContainerStarting(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), m_containerConfiguration.ToDisplayString());
                            }

                            process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: false);

                            // If the process started in a container, the setup of it is ready at this point, so we (verbose) log it
                            if (m_containerConfiguration.IsIsolationEnabled)
                            {
                                Tracing.Logger.Log.PipInContainerStarted(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));
                            }
                        }
                        catch (BuildXLException ex)
                        {
                            if (ex.LogEventErrorCode == NativeIOConstants.ErrorFileNotFound)
                            {
                                LocationData location = m_pip.Provenance.Token;
                                string specFile = location.Path.ToString(m_pathTable);

                                Tracing.Logger.Log.PipProcessStartFailed(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), 2,
                                    string.Format(CultureInfo.InvariantCulture, "File '{0}' was not found on disk. The tool is referred in '{1}({2})'.", info.FileName, specFile, location.Position));
                            }
                            else if (ex.LogEventErrorCode == NativeIOConstants.ErrorPartialCopy && (processLaunchRetryCount < ProcessLaunchRetryCountMax))
                            {
                                processLaunchRetryCount++;
                                shouldRelaunchProcess = true;
                                Tracing.Logger.Log.RetryStartPipDueToErrorPartialCopyDuringDetours(
                                    m_loggingContext,
                                    m_pip.SemiStableHash,
                                    m_pip.GetDescription(m_context),
                                    ex.LogEventErrorCode,
                                    processLaunchRetryCount);

                                // We are about to retry a process execution.
                                // Make sure we wait for the process to end. This way the reporting messages get flushed.
                                if (process != null)
                                {
                                    maxDetoursHeapSize = process.GetDetoursMaxHeapSize();

                                    try
                                    {
                                        await process.GetResultAsync();
                                    }
                                    finally
                                    {
                                        process.Dispose();
                                    }
                                }

                                continue;
                            }
                            else
                            {
                                // not all start failures map to Win32 error code, so we have a message here too
                                Tracing.Logger.Log.PipProcessStartFailed(
                                    m_loggingContext,
                                    m_pip.SemiStableHash,
                                    m_pip.GetDescription(m_context),
                                    ex.LogEventErrorCode,
                                    ex.LogEventMessage);
                            }

                            return SandboxedProcessPipExecutionResult.PreparationFailure(processLaunchRetryCount, ex.LogEventErrorCode, maxDetoursHeapSize: maxDetoursHeapSize);
                        }
                    }

                    return await GetAndProcessResultAsync(process, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
                }
            }
        }

        private async Task<SandboxedProcessPipExecutionResult> RunExternalAsync(
            SandboxedProcessInfo info,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            System.Diagnostics.Stopwatch sandboxPrepTime,
            CancellationToken cancellationToken = default)
        {
            StandardInputInfo standardInputSource = m_pip.StandardInput.IsData
                ? StandardInputInfo.CreateForData(m_pip.StandardInput.Data.ToString(m_context.PathTable))
                : (m_pip.StandardInput.IsFile
                    ? StandardInputInfo.CreateForFile(m_pip.StandardInput.File.Path.ToString(m_context.PathTable))
                    : null);

            info.StandardInputSourceInfo = standardInputSource;

            if (m_pip.WarningRegex.IsValid)
            {
                var observerDescriptor = new SandboxObserverDescriptor
                {
                    WarningRegex = new ExpandedRegexDescriptor(m_pip.WarningRegex.Pattern.ToString(m_context.StringTable), m_pip.WarningRegex.Options)
                };

                info.StandardObserverDescriptor = observerDescriptor;
            }

            // Preparation should be finished.
            sandboxPrepTime.Stop();
            ISandboxedProcess process = null;

            try
            {
                var externalSandboxedProcessExecutor = new ExternalToolSandboxedProcessExecutor(Path.Combine(
                    m_layoutConfiguration.BuildEngineDirectory.ToString(m_context.PathTable),
                    ExternalToolSandboxedProcessExecutor.DefaultToolRelativePath));

                if (m_sandboxConfig.AdminRequiredProcessExecutionMode == AdminRequiredProcessExecutionMode.ExternalTool)
                {
                    Tracing.Logger.Log.PipProcessStartExternalTool(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), externalSandboxedProcessExecutor.ExecutablePath);

                    process = await ExternalSandboxedProcess.StartAsync(
                        info,
                        spi => new ExternalToolSandboxedProcess(spi, externalSandboxedProcessExecutor));
                }
                else
                {
                    Contract.Assert(m_sandboxConfig.AdminRequiredProcessExecutionMode == AdminRequiredProcessExecutionMode.ExternalVM);

                    Tracing.Logger.Log.PipProcessStartExternalVm(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));

                    await m_vmInitializer.LazyInitVmAsync.Value;

                    process = await ExternalSandboxedProcess.StartAsync(
                        info,
                        spi => new ExternalVmSandboxedProcess(spi, m_vmInitializer, externalSandboxedProcessExecutor));
                }
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipProcessStartFailed(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    ex.LogEventErrorCode,
                    ex.LogEventMessage);

                return SandboxedProcessPipExecutionResult.PreparationFailure(0, ex.LogEventErrorCode);
            }

            return await GetAndProcessResultAsync(process, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
        }

        private async Task<SandboxedProcessPipExecutionResult> GetAndProcessResultAsync(
            ISandboxedProcess process,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            System.Diagnostics.Stopwatch sandboxPrepTime,
            CancellationToken cancellationToken)
        {
            using (var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_context.CancellationToken))
            {
                var cancellationTokenRegistration = cancellationTokenSource.Token.Register(() => process.KillAsync());

                SandboxedProcessResult result;

                int lastMessageCount = 0;
                bool isMessageCountSemaphoreCreated = false;

                try
                {
                    m_activeProcess = process;
                    result = await process.GetResultAsync();
                    lastMessageCount = process.GetLastMessageCount() + result.LastMessageCount;
                    m_numWarnings += result.WarningCount;
                    isMessageCountSemaphoreCreated = m_fileAccessManifest.MessageCountSemaphore != null || result.MessageCountSemaphoreCreated;

                    if (process is ExternalSandboxedProcess externalSandboxedProcess)
                    {
                        int exitCode = externalSandboxedProcess.ExitCode ?? -1;
                        string stdOut = Environment.NewLine + "StdOut:" + Environment.NewLine + externalSandboxedProcess.StdOut;
                        string stdErr = Environment.NewLine + "StdErr:" + Environment.NewLine + externalSandboxedProcess.StdErr;

                        if (process is ExternalToolSandboxedProcess externalToolSandboxedProcess)
                        {
                            Tracing.Logger.Log.PipProcessFinishedExternalTool(
                                m_loggingContext,
                                m_pip.SemiStableHash,
                                m_pip.GetDescription(m_context),
                                exitCode,
                                stdOut,
                                stdErr);
                        }
                        else if (process is ExternalVmSandboxedProcess externalVmSandboxedProcess)
                        {
                            Tracing.Logger.Log.PipProcessFinishedExternalVm(
                                m_loggingContext,
                                m_pip.SemiStableHash,
                                m_pip.GetDescription(m_context),
                                exitCode,
                                stdOut,
                                stdErr);
                        }
                    }
                }
                finally
                {
                    m_activeProcess = null;
#pragma warning disable AsyncFixer02
                    cancellationTokenRegistration.Dispose();
#pragma warning restore AsyncFixer02
                    process.Dispose();
                }

                SandboxedProcessPipExecutionResult executionResult =
                    await
                        ProcessSandboxedProcessResultAsync(
                            m_loggingContext,
                            result,
                            sandboxPrepTime.ElapsedMilliseconds,
                            cancellationTokenSource.Token,
                            process.GetDetoursMaxHeapSize() + result.DetoursMaxHeapSize,
                            allInputPathsUnderSharedOpaques);

                return ValidateDetoursCommunication(
                    executionResult,
                    lastMessageCount,
                    isMessageCountSemaphoreCreated);
            }
        }

        /// <summary>
        /// These various validations that the detours communication channel
        /// </summary>
        private SandboxedProcessPipExecutionResult ValidateDetoursCommunication(
            SandboxedProcessPipExecutionResult result,
            int lastMessageCount,
            bool isMessageSemaphoreCountCreated)
        {
            // If we have a failure already, that could have cause some of the mismatch in message count of writing the side communication file.
            if (result.Status == SandboxedProcessPipExecutionStatus.Succeeded && !string.IsNullOrEmpty(m_detoursFailuresFile))
            {
                FileInfo fi;

                try
                {
                    fi = new FileInfo(m_detoursFailuresFile);
                }
                catch (Exception ex)
                {
                    Tracing.Logger.Log.LogGettingInternalDetoursErrorFile(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        ex.ToStringDemystified());
                    return SandboxedProcessPipExecutionResult.DetouringFailure(result);
                }

                bool fileExists = fi.Exists;
                if (fileExists)
                {
                    Tracing.Logger.Log.LogInternalDetoursErrorFileNotEmpty(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        File.ReadAllText(m_detoursFailuresFile, Encoding.Unicode));

                    return SandboxedProcessPipExecutionResult.DetouringFailure(result);
                }

                // Avoid eager reporting of message count mismatch. We have observed two failures in WDG and both were due to
                // a pip running longer than the timeout (5 hours). The pip gets killed and in such cases the message count mismatch
                // is legitimate.
                // Report a counter mismatch only if there are no other errors.
                if (result.Status == SandboxedProcessPipExecutionStatus.Succeeded && isMessageSemaphoreCountCreated)
                {
                    if (lastMessageCount != 0)
                    {
                        Tracing.Logger.Log.LogMismatchedDetoursVerboseCount(
                            m_loggingContext,
                            m_pip.SemiStableHash,
                            m_pip.GetDescription(m_context),
                            lastMessageCount);

                        return SandboxedProcessPipExecutionResult.MismatchedMessageCountFailure(result);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if the warning regex is valid.
        /// </summary>
        public async Task<bool> TryInitializeWarningRegexAsync()
        {
            if (m_warningRegexTask != null)
            {
                try
                {
                    m_warningRegex = await m_warningRegexTask;
                }
                catch (ArgumentException)
                {
                    LogInvalidWarningRegex();
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> TryInitializeErrorRegexAsync()
        {
            if (m_errorRegexTask != null)
            {
                try
                {
                    m_errorRegex = await m_errorRegexTask;
                }
                catch (ArgumentException)
                {
                    LogInvalidErrorRegex();
                    return false;
                }
            }

            return true;
        }

        private bool TryLogRootIOException(string path, AggregateException aggregateException)
        {
            var flattenedEx = aggregateException.Flatten();
            if (flattenedEx.InnerExceptions.Count == 1 && flattenedEx.InnerExceptions[0] is IOException)
            {
                PipStandardIOFailed(path, flattenedEx.InnerExceptions[0]);
                return true;
            }

            return false;
        }

        private void PipStandardIOFailed(string path, Exception ex)
        {
            BuildXL.Processes.Tracing.Logger.Log.PipStandardIOFailed(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                path,
                ex.GetLogEventErrorCode(),
                ex.GetLogEventMessage());
        }

        private void LogOutputPreparationFailed(string path, BuildXLException ex)
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessOutputPreparationFailed(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                path,
                ex.LogEventErrorCode,
                ex.LogEventMessage,
                ex.ToString());
        }

        private void LogInvalidWarningRegex()
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessInvalidWarningRegex(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                m_pathTable.StringTable.GetString(m_pip.WarningRegex.Pattern),
                m_pip.WarningRegex.Options.ToString());
        }

        private void LogInvalidErrorRegex()
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessInvalidErrorRegex(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                m_pathTable.StringTable.GetString(m_pip.ErrorRegex.Pattern),
                m_pip.ErrorRegex.Options.ToString());
        }

        private void LogCommandLineTooLong(SandboxedProcessInfo info)
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessCommandLineTooLong(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                info.GetCommandLine(),
                SandboxedProcessInfo.MaxCommandLineLength);
        }

        private async Task<SandboxedProcessPipExecutionResult> ProcessSandboxedProcessResultAsync(
            LoggingContext loggingContext,
            SandboxedProcessResult result,
            long sandboxPrepMs,
            CancellationToken cancellationToken,
            long maxDetoursHeapSize,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool canceled = result.Killed && cancellationToken.IsCancellationRequested;
            bool hasMessageParsingError = result?.MessageProcessingFailure != null;
            bool exitedWithSuccessExitCode = m_pip.SuccessExitCodes.Length == 0
                ? result.ExitCode == 0
                : m_pip.SuccessExitCodes.Contains(result.ExitCode);
            bool exitedSuccessfullyAndGracefully = !canceled && exitedWithSuccessExitCode;
            bool exitedButCanBeRetried = m_pip.RetryExitCodes.Contains(result.ExitCode) && m_remainingUserRetryCount > 0;

            bool allOutputsPresent = false;

            ProcessTimes primaryProcessTimes = result.PrimaryProcessTimes;
            JobObject.AccountingInformation? jobAccounting = result.JobAccountingInformation;

            TimeSpan time = primaryProcessTimes.TotalWallClockTime;
            if (result.TimedOut)
            {
                LogTookTooLongError(result, m_timeout, time);

                if (result.DumpCreationException != null)
                {
                    Tracing.Logger.Log.PipFailedToCreateDumpFile(
                        loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        result.DumpCreationException.GetLogEventMessage());
                }
            }
            else
            {
                TimeSpan warningTimeout = GetEffectiveTimeout(
                    m_pip.WarningTimeout,
                    m_sandboxConfig.DefaultWarningTimeout,
                    m_sandboxConfig.WarningTimeoutMultiplier);

                if (time > warningTimeout)
                {
                    LogTookTooLongWarning(m_timeout, time, warningTimeout);
                }

                // There are cases where the process exit code is not successful and the injection has failed.
                // (ExitCode code is set by Windows - TerminateProcess, kill(), process crash).
                // The build needs to fail in this case(s) as well and log that we had injection failure.
                if (result.HasDetoursInjectionFailures)
                {
                    BuildXL.Processes.Tracing.Logger.Log.PipProcessFinishedDetourFailures(loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));
                }

                if (exitedSuccessfullyAndGracefully)
                {
                    Tracing.Logger.Log.PipProcessFinished(loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), result.ExitCode);
                    allOutputsPresent = CheckExpectedOutputs();
                }
                else
                {
                    if (!canceled)
                    {
                        LogFinishedFailed(result);

                        if (exitedButCanBeRetried)
                        {
                            return SandboxedProcessPipExecutionResult.RetryProcessDueToExitCode(
                                result.NumberOfProcessLaunchRetries,
                                result.ExitCode,
                                primaryProcessTimes,
                                jobAccounting,
                                result.DetouringStatuses,
                                sandboxPrepMs,
                                sw.ElapsedMilliseconds,
                                result.ProcessStartTime,
                                maxDetoursHeapSize,
                                m_containerConfiguration);
                        }
                    }
                }
            }

            int numSurvivingChildErrors = 0;
            if (result.SurvivingChildProcesses != null &&
                result.SurvivingChildProcesses.Any())
            {
                numSurvivingChildErrors = ReportSurvivingChildProcesses(result);
            }

            var fileAccessReportingContext = new FileAccessReportingContext(
                loggingContext,
                m_context,
                m_sandboxConfig,
                m_pip,
                m_validateDistribution,
                m_fileAccessWhitelist);

            // Note that when MonitorFileAccesses == false, we should not assume the various reported-access sets are non-null.
            if (m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses)
            {
                // First remove all the paths that are Injectable from in the process.
                RemoveInjectableFileAccesses(result.AllUnexpectedFileAccesses);

                foreach (ReportedFileAccess unexpectedFileAccess in result.AllUnexpectedFileAccesses)
                {
                    Contract.Assume(
                        unexpectedFileAccess.Status == FileAccessStatus.Denied ||
                        unexpectedFileAccess.Status == FileAccessStatus.CannotDeterminePolicy);

                    fileAccessReportingContext.ReportFileAccessDeniedByManifest(unexpectedFileAccess);
                }
            }

            if (result.Killed && numSurvivingChildErrors > 0)
            {
                LogChildrenSurvivedKilled();
            }

            bool mainProcessExitedCleanly =
                (!result.Killed || numSurvivingChildErrors == 0) &&
                exitedSuccessfullyAndGracefully;

            if (!mainProcessExitedCleanly)
            {
                Tracing.Logger.Log.PipExitedUncleanly(
                    loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    canceled,
                    result.ExitCode,
                    result.Killed,
                    numSurvivingChildErrors);
            }

            // This is the overall success of the process. At a high level, there are 4 things that can cause a process pip to fail:
            //      1. The process being killed (built into mainProcessExitedCleanly)
            //      2. The process not exiting with the appropriate exit code (mainProcessExitedCleanly)
            //      3. The process not creating all outputs (allOutputsPresent)
            //      4. The process running in a container, and even though succeeding, its outputs couldn't be moved to their expected locations
            bool mainProcessSuccess = mainProcessExitedCleanly && allOutputsPresent;

            bool standardOutHasBeenWrittenToLog = false;
            bool errorOrWarnings = !mainProcessSuccess || m_numWarnings > 0;
            bool loggingSuccess = true;

            bool shouldPersistStandardOutput = errorOrWarnings || m_pip.StandardOutput.IsValid;
            if (shouldPersistStandardOutput)
            {
                if (!await TrySaveAndLogStandardOutput(result))
                {
                    loggingSuccess = false;
                }
            }

            bool shouldPersistStandardError = errorOrWarnings || m_pip.StandardError.IsValid;
            if (shouldPersistStandardError)
            {
                if (!await TrySaveAndLogStandardError(result))
                {
                    loggingSuccess = false;
                }
            }

            SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observed =
                GetObservedFileAccesses(
                    result,
                    allInputPathsUnderSharedOpaques,
                    out var unobservedOutputs,
                    out var sharedDynamicDirectoryWriteAccesses);

            // After standard output and error may been saved, and shared dynamic write accesses were identified, we can merge
            // outputs back to their original locations
            if (!await m_processInContainerManager.MergeOutputsIfNeededAsync(m_pip, m_containerConfiguration, m_context, sharedDynamicDirectoryWriteAccesses))
            {
                // If the merge failed, update the status flags
                mainProcessSuccess = false;
                errorOrWarnings = true;
            }

            bool errorWasTruncated = false;
            // if some outputs are missing, we are logging this process as a failed one (even if it finished with a success exit code).
            if ((!mainProcessExitedCleanly || !allOutputsPresent) && !canceled && loggingSuccess)
            {
                standardOutHasBeenWrittenToLog = true;

                LogErrorResult logErrorResult = await TryLogErrorAsync(result, exitedWithSuccessExitCode);
                errorWasTruncated = logErrorResult.ErrorWasTruncated;
                loggingSuccess = logErrorResult.Success;
            }

            if (m_numWarnings > 0 && loggingSuccess)
            {
                if (!await TryLogWarningAsync(result.StandardOutput, result.StandardError))
                {
                    loggingSuccess = false;
                }
            }

            // The full output may be requested based on the result of the pip. If the pip failed, the output may have been reported
            // in TryLogErrorAsync above. Only replicate the output if the error was truncated due to an error regex
            if ((!standardOutHasBeenWrittenToLog || errorWasTruncated) && loggingSuccess)
            {
                // If the pip succeeded, we must check if one of the non-error output modes have been chosen
                if ((m_sandboxConfig.OutputReportingMode == OutputReportingMode.FullOutputAlways) ||
                    (m_sandboxConfig.OutputReportingMode == OutputReportingMode.FullOutputOnWarningOrError && errorOrWarnings) ||
                    (m_sandboxConfig.OutputReportingMode == OutputReportingMode.FullOutputOnError && !mainProcessSuccess))
                {
                    if (!await TryLogOutputAsync(result))
                    {
                        loggingSuccess = false;
                    }
                }
            }

            // N.B. here 'observed' means 'all', not observed in the terminology of SandboxedProcessPipExecutor.
            List<ReportedFileAccess> allFileAccesses = null;

            if (m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses && m_sandboxConfig.LogObservedFileAccesses)
            {
                allFileAccesses = new List<ReportedFileAccess>(result.FileAccesses);
                allFileAccesses.AddRange(result.AllUnexpectedFileAccesses);
            }

            m_logger.LogProcessObservation(
                processes: m_sandboxConfig.LogProcesses ? result.Processes : null,
                fileAccesses: allFileAccesses,
                detouringStatuses: result.DetouringStatuses);

            if (mainProcessSuccess && loggingSuccess)
            {
                Contract.Assert(!shouldPersistStandardOutput || result.StandardOutput.IsSaved);
                Contract.Assert(!shouldPersistStandardError || result.StandardError.IsSaved);
            }

            // Log a warning for having converted ReadWrite file access request to Read file access request and the pip was not canceled and failed.
            if (!mainProcessSuccess && !canceled && result.HasReadWriteToReadFileAccessRequest)
            {
                Tracing.Logger.Log.ReadWriteFileAccessConvertedToReadWarning(loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));
            }

            var finalStatus = canceled
                ? SandboxedProcessPipExecutionStatus.Canceled
                : (mainProcessSuccess && loggingSuccess
                    ? SandboxedProcessPipExecutionStatus.Succeeded
                    : SandboxedProcessPipExecutionStatus.ExecutionFailed);

            if (result.StandardInputException != null && finalStatus == SandboxedProcessPipExecutionStatus.Succeeded)
            {
                // When process execution succeeded, standard input exception should not occur.

                // Log error to correlate the pip with the standard input exception.
                Tracing.Logger.Log.PipProcessStandardInputException(
                    loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    m_pip.Provenance.Token.Path.ToString(m_pathTable),
                    m_workingDirectory,
                    result.StandardInputException.Message + Environment.NewLine + result.StandardInputException.StackTrace);
            }

            if (hasMessageParsingError)
            {
                Tracing.Logger.Log.PipProcessMessageParsingError(
                    loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    result.MessageProcessingFailure.Content);
            }

            SandboxedProcessPipExecutionStatus status = SandboxedProcessPipExecutionStatus.ExecutionFailed;
            if (result.HasDetoursInjectionFailures)
            {
                status = SandboxedProcessPipExecutionStatus.PreparationFailed;
            }
            else if (canceled)
            {
                status = SandboxedProcessPipExecutionStatus.Canceled;
            }
            else if (mainProcessSuccess && loggingSuccess && !hasMessageParsingError)
            {
                status = SandboxedProcessPipExecutionStatus.Succeeded;
            }

            if (m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses &&
                m_sandboxConfig.UnsafeSandboxConfiguration.SandboxKind != SandboxKind.MacOsKextIgnoreFileAccesses &&
                status == SandboxedProcessPipExecutionStatus.Succeeded &&
                m_pip.PipType == PipType.Process &&
                unobservedOutputs != null)
            {
                foreach (var outputPath in unobservedOutputs)
                {
                    // Report non observed access only if the output exists.
                    // If the output was not produced, missing declared output logic
                    // will report another error.
                    string expandedOutputPath = outputPath.ToString(m_pathTable);
                    bool isFile;
                    if ((isFile = FileUtilities.FileExistsNoFollow(expandedOutputPath)) ||
                        FileUtilities.DirectoryExistsNoFollow(expandedOutputPath))
                    {
                        BuildXL.Processes.Tracing.Logger.Log.PipOutputNotAccessed(
                          m_loggingContext,
                          m_pip.SemiStableHash,
                          m_pip.GetDescription(m_context),
                          "'" + expandedOutputPath + "'. " + (isFile ? "Found path is a file" : "Found path is a directory"));

                        status = SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed;
                    }
                }
            }

            // If a PipProcessError was logged, the pip cannot be marked as succeeded
            Contract.Assert(!standardOutHasBeenWrittenToLog || status != SandboxedProcessPipExecutionStatus.Succeeded);

            return new SandboxedProcessPipExecutionResult(
                status: status,
                observedFileAccesses: observed,
                sharedDynamicDirectoryWriteAccesses: sharedDynamicDirectoryWriteAccesses,
                unexpectedFileAccesses: fileAccessReportingContext,
                encodedStandardOutput: loggingSuccess && shouldPersistStandardOutput ? GetEncodedStandardConsoleStream(result.StandardOutput) : null,
                encodedStandardError: loggingSuccess && shouldPersistStandardError ? GetEncodedStandardConsoleStream(result.StandardError) : null,
                numberOfWarnings: m_numWarnings,
                primaryProcessTimes: primaryProcessTimes,
                jobAccountingInformation: jobAccounting,
                numberOfProcessLaunchRetries: result.NumberOfProcessLaunchRetries,
                exitCode: result.ExitCode,
                sandboxPrepMs: sandboxPrepMs,
                processSandboxedProcessResultMs: sw.ElapsedMilliseconds,
                processStartTime: result.ProcessStartTime,
                allReportedFileAccesses: allFileAccesses,
                detouringStatuses: result.DetouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: m_containerConfiguration);
        }

        private Tuple<AbsolutePath, Encoding> GetEncodedStandardConsoleStream(SandboxedProcessOutput output)
        {
            Contract.Requires(output.IsSaved);
            Contract.Requires(!output.HasException);

            return Tuple.Create(AbsolutePath.Create(m_pathTable, output.FileName), output.Encoding);
        }

        private bool TryPrepareFileAccessMonitoring(LoggingContext loggingContext, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            if (!PrepareFileAccessMonitoringCommon(loggingContext))
            {
                return false;
            }

            TryPrepareFileAccessMonitoringForPip(m_pip, allInputPathsUnderSharedOpaques);

            return true;
        }

        private bool PrepareFileAccessMonitoringCommon(LoggingContext loggingContext)
        {
            if (m_pip.IsService)
            {
                // Service pip is allowed to read all files because it has to read all files on behalf of its clients.
                // This behavior is safe because service pip and its clients are not cacheable.
                // Service pips do not report, because this introduces unnecessary attempts to process observed inputs
                m_fileAccessManifest.AddScope(
                    AbsolutePath.Invalid,
                    FileAccessPolicy.MaskNothing,
                    FileAccessPolicy.AllowReadAlways);
            }
            else
            {
                // The default policy is to only allow absent file probes. But if undeclared source reads are enabled, we allow any kind of read
                var rootFileAccessPolicy = m_pip.AllowUndeclaredSourceReads ?
                    (FileAccessPolicy.AllowReadAlways | FileAccessPolicy.ReportAccess) :
                    // All directory enumerations are reported unless explicitly excluded
                    (FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportDirectoryEnumerationAccess | FileAccessPolicy.ReportAccessIfNonexistent);

                m_fileAccessManifest.AddScope(
                    AbsolutePath.Invalid,
                    FileAccessPolicy.MaskNothing,
                    rootFileAccessPolicy);
            }

            // Add a scope entry to allow reading in the BuildXL directories. We do read at least BuildXLServices.dll for the 2 currently supported platforms.
            if (m_buildEngineDirectory.IsValid)
            {
                m_fileAccessManifest.AddScope(
                    m_buildEngineDirectory,
                    FileAccessPolicy.MaskNothing,
                    FileAccessPolicy.AllowReadAlways);
            }

            // CreateDirectory access is allowed anywhere under a writable root. The rationale is that this is safe because file writes
            // are still blocked. If a previous build or external factor had created the directory, the CreateDirectory would be
            // interpreted as a probe anyway. So blocking it on the first build and allowing it on the second is inconsistent.
            //
            // This behavior is now controlled by the command line argument. We want to prevent the tools to secretly communicate
            // between each other by means of creating empty directories. Because empty directories are not stored in cache, this leads to
            // a non deterministic behavior. When EnforceAccessPoliciesOnDirectoryCreation is set to true, we will not downgrade
            // the CreateDirectory to a read-only probe in Detours and will emit file access error/warning for all CreateDirectory
            // calls that do not have an explicit allowing policy in the manifest, regardless of whether the corresponding directory exists or not.
            if (m_semanticPathExpander != null && !m_sandboxConfig.EnforceAccessPoliciesOnDirectoryCreation)
            {
                foreach (AbsolutePath writableRoot in m_semanticPathExpander.GetWritableRoots())
                {
                    m_fileAccessManifest.AddScope(writableRoot, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowCreateDirectory);
                }
            }

            // For some static system mounts (currently only for AppData\Roaming) we allow CreateDirectory requests for all processes.
            // This is done because many tools are using CreateDirectory to check for the existence of that directory. Since system directories
            // always exist, allowing such requests would not lead to any changes on the disk. Moreover, we are applying an exact path policy (i.e., not a scope policy).
            if (m_semanticPathExpander != null)
            {
                foreach (AbsolutePath path in m_semanticPathExpander.GetPathsWithAllowedCreateDirectory())
                {
                    m_fileAccessManifest.AddPath(path, mask: FileAccessPolicy.MaskNothing, values: FileAccessPolicy.AllowCreateDirectory);
                }
            }

            m_fileAccessManifest.ReportUnexpectedFileAccesses = true;
            m_fileAccessManifest.ReportFileAccesses = m_sandboxConfig.LogObservedFileAccesses;
            m_fileAccessManifest.BreakOnUnexpectedAccess = m_sandboxConfig.BreakOnUnexpectedFileAccess;
            m_fileAccessManifest.FailUnexpectedFileAccesses = m_sandboxConfig.FailUnexpectedFileAccesses;
            m_fileAccessManifest.ReportProcessArgs = m_sandboxConfig.LogProcesses;
            m_fileAccessManifest.EnforceAccessPoliciesOnDirectoryCreation = m_sandboxConfig.EnforceAccessPoliciesOnDirectoryCreation;

            bool allowInternalErrorsLogging = m_sandboxConfig.AllowInternalDetoursErrorNotificationFile;
            bool checkMessageCount = m_fileAccessManifest.CheckDetoursMessageCount = m_sandboxConfig.CheckDetoursMessageCount;

            if (allowInternalErrorsLogging || checkMessageCount)
            {
                // Create unique file name.
                m_detoursFailuresFile = GetDetoursInternalErrorFilePath(loggingContext);

                // Delete the file
                if (FileUtilities.FileExistsNoFollow(m_detoursFailuresFile))
                {
                    Analysis.IgnoreResult(FileUtilities.TryDeleteFile(m_detoursFailuresFile, tempDirectoryCleaner: m_tempDirectoryCleaner));
                }

                if (!string.IsNullOrEmpty(m_detoursFailuresFile))
                {
                    if (allowInternalErrorsLogging)
                    {
                        m_fileAccessManifest.InternalDetoursErrorNotificationFile = m_detoursFailuresFile;
                    }

                    // TODO: named semaphores are not supported in NetStandard2.0
                    if ((!m_pip.RequiresAdmin || m_sandboxConfig.AdminRequiredProcessExecutionMode == AdminRequiredProcessExecutionMode.Internal) 
                        && checkMessageCount 
                        && !OperatingSystemHelper.IsUnixOS)
                    {
                        // Semaphore names don't allow '\\' chars.
                        if (!m_fileAccessManifest.SetMessageCountSemaphore(m_detoursFailuresFile.Replace('\\', '_')))
                        {
                            Tracing.Logger.Log.LogMessageCountSemaphoreExists(loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context));
                            return false;
                        }
                    }
                }
            }

            m_fileAccessManifest.HardExitOnErrorInDetours = m_sandboxConfig.HardExitOnErrorInDetours;
            m_fileAccessManifest.LogProcessDetouringStatus = m_sandboxConfig.LogProcessDetouringStatus;

            if (m_sandboxConfig.FileAccessIgnoreCodeCoverage)
            {
                m_fileAccessManifest.IgnoreCodeCoverage = true;
            }

            return true;
        }

        private void AllowCreateDirectoryForDirectoriesOnPath(AbsolutePath path, HashSet<AbsolutePath> processedPaths, bool startWithParent = true)
        {
            Contract.Assert(path.IsValid);

            if (m_sandboxConfig.EnforceAccessPoliciesOnDirectoryCreation)
            {
                var parentPath = startWithParent ? path.GetParent(m_pathTable) : path;
                while (parentPath.IsValid && !processedPaths.Contains(parentPath))
                {
                    processedPaths.Add(parentPath);
                    m_fileAccessManifest.AddPath(parentPath, values: FileAccessPolicy.AllowCreateDirectory, mask: FileAccessPolicy.MaskNothing);
                    parentPath = parentPath.GetParent(m_pathTable);
                }
            }
        }

        private void TryPrepareFileAccessMonitoringForPip(Process pip, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            // While we always define the %TMP% and %TEMP% variables to point to some legal directory
            // (many tools fail if those variables are not defined),
            // we don't allow any access to the default temp directory.
            m_fileAccessManifest.AddScope(
                AbsolutePath.Create(m_pathTable, PipEnvironment.RestrictedTemp),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.Deny);

            using (var poolPathSet = Pools.GetAbsolutePathSet())
            {
                var processedPaths = poolPathSet.Instance;
                using (var wrapper = Pools.GetAbsolutePathSet())
                {
                    var outputIds = wrapper.Instance;

                    foreach (FileArtifactWithAttributes attributedOutput in pip.FileOutputs)
                    {
                        var output = attributedOutput.ToFileArtifact();

                        if (output != pip.StandardOutput && output != pip.StandardError)
                        {
                            // We mask 'report' here, since outputs are expected written and should never fail observed-access validation (directory dependency, etc.)
                            // Note that they would perhaps fail otherwise (simplifies the observed access checks in the first place).
                            // We allow the real input timestamps to be seen since if any of these outputs are rewritten, we should block input timestamp faking in favor of output timestamp faking
                            m_fileAccessManifest.AddPath(
                                output.Path,
                                values: FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowRealInputTimestamps, // Always report output file accesses, so we can validate that output was produced.
                                mask: m_excludeReportAccessMask);
                            outputIds.Add(output.Path);

                            AllowCreateDirectoryForDirectoriesOnPath(output.Path, processedPaths);
                        }
                    }

                    AddStaticFileDependenciesToFileAccessManifest(wrapper, pip.Dependencies, allInputPathsUnderSharedOpaques);
                }

                var userProfilePath = AbsolutePath.Create(m_pathTable, s_userProfilePath);
                var redirectedUserProfilePath = m_layoutConfiguration != null && m_layoutConfiguration.RedirectedUserProfileJunctionRoot.IsValid
                    ? AbsolutePath.Create(m_pathTable, s_possibleRedirectedUserProfilePath)
                    : AbsolutePath.Invalid;

                foreach (AbsolutePath path in pip.UntrackedPaths)
                {
                    // We mask Report to simplify handling of explicitly-reported directory-dependency or transitive-dependency accesses
                    // (they should never fail for untracked accesses, which should be invisible).

                    // We allow the real input timestamp to be seen for untracked paths. This is to preserve existing behavior, where the timestamp of untracked stuff is never modified.
                    m_fileAccessManifest.AddPath(path, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps, mask: m_excludeReportAccessMask);
                    AllowCreateDirectoryForDirectoriesOnPath(path, processedPaths);

                    var correspondingPath = CreatePathForActualRedirectedUserProfilePair(path, userProfilePath, redirectedUserProfilePath);
                    if (correspondingPath.IsValid)
                    {
                        m_fileAccessManifest.AddPath(correspondingPath, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps, mask: m_excludeReportAccessMask);
                        AllowCreateDirectoryForDirectoriesOnPath(correspondingPath, processedPaths);
                    }

                    // Untrack real logs directory if the redirected one is untracked.
                    if (m_loggingConfiguration != null && m_loggingConfiguration.RedirectedLogsDirectory.IsValid && m_loggingConfiguration.RedirectedLogsDirectory == path)
                    {
                        m_fileAccessManifest.AddPath(m_loggingConfiguration.LogsDirectory, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps, mask: m_excludeReportAccessMask);
                        AllowCreateDirectoryForDirectoriesOnPath(m_loggingConfiguration.LogsDirectory, processedPaths);
                    }
                }

                foreach (AbsolutePath path in pip.UntrackedScopes)
                {
                    // Note that untracked scopes are quite dangerous. We allow writes, reads, and probes for non-existent files.
                    // We mask Report to simplify handling of explicitly-reported directory-dependence or transitive-dependency accesses
                    // (they should never fail for untracked accesses, which should be invisible).

                    // The default mask for untracked scopes is to not report anything.
                    // We block input timestamp faking for untracked scopes. This is to preserve existing behavior, where the timestamp of untracked stuff is never modified.
                    m_fileAccessManifest.AddScope(path, mask: m_excludeReportAccessMask, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);
                    AllowCreateDirectoryForDirectoriesOnPath(path, processedPaths);

                    var correspondingPath = CreatePathForActualRedirectedUserProfilePair(path, userProfilePath, redirectedUserProfilePath);
                    if (correspondingPath.IsValid)
                    {
                        m_fileAccessManifest.AddScope(correspondingPath, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps, mask: m_excludeReportAccessMask);
                        AllowCreateDirectoryForDirectoriesOnPath(correspondingPath, processedPaths);
                    }

                    // Untrack real logs directory if the redirected one is untracked.
                    if (m_loggingConfiguration != null && m_loggingConfiguration.RedirectedLogsDirectory.IsValid && m_loggingConfiguration.RedirectedLogsDirectory == path)
                    {
                        m_fileAccessManifest.AddScope(m_loggingConfiguration.LogsDirectory, mask: m_excludeReportAccessMask, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);
                        AllowCreateDirectoryForDirectoriesOnPath(m_loggingConfiguration.LogsDirectory, processedPaths);
                    }
                }

                using (var wrapper = Pools.GetAbsolutePathSet())
                {
                    HashSet<AbsolutePath> directoryOutputIds = wrapper.Instance;

                    foreach (DirectoryArtifact directory in pip.DirectoryOutputs)
                    {
                        // We need to allow the real timestamp to be seen under a directory output (since these are outputs). If this directory output happens to share the root with
                        // a directory dependency (shared opaque case), this is overridden for specific input files when processing directory dependencies below
                        var values =
                            FileAccessPolicy.AllowAll | // Symlink creation is allowed under opaques.
                            FileAccessPolicy.AllowRealInputTimestamps |
                            // For shared opaques, we need to know the (write) accesses that occurred, since we determine file ownership based on that.
                            (directory.IsSharedOpaque? FileAccessPolicy.ReportAccess : FileAccessPolicy.Deny);

                        // For exclusive opaques, we don't need reporting back and the content is discovered by enumerating the disk
                        var mask = directory.IsSharedOpaque ? FileAccessPolicy.MaskNothing : m_excludeReportAccessMask;

                        var directoryPath = directory.Path;

                        m_fileAccessManifest.AddScope(directoryPath, values: values, mask: mask);
                        AllowCreateDirectoryForDirectoriesOnPath(directoryPath, processedPaths);

                        directoryOutputIds.Add(directoryPath);
                    }

                    // Directory artifact dependencies are supposed to be immutable. Therefore it is never okay to write, but it is safe to probe for non-existent files
                    // (the set of files is stable). We turn on explicit access reporting so that reads and
                    // failed probes can be used by the scheduler (rather than fingerprinting the precise directory contents).
                    foreach (DirectoryArtifact directory in pip.DirectoryDependencies)
                    {
                        if (directory.IsSharedOpaque)
                        {
                            // All members of the shared opaque need to be added to the manifest explicitly so timestamp faking happens for them
                            AddSharedOpaqueInputContentToManifest(directory, allInputPathsUnderSharedOpaques);
                        }

                        // If this directory dependency is also a directory output, then we don't set any additional policy: we don't want to restrict
                        // writes
                        if (!directoryOutputIds.Contains(directory.Path))
                        {
                            // Directories here represent inputs, we want to apply the timestamp faking logic
                            var mask = ~FileAccessPolicy.AllowRealInputTimestamps;
                            // Allow read accesses and reporting. Reporting is needed since these may be dynamic accesses and we need to cross check them
                            var values = FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess;

                            // In the case of a partial sealed directory under a shared opaque, we don't want to block writes
                            // for the entire cone: some files may be dynamically written in the context of the shared opaque that may fall
                            // under the cone of the partial sealed directory. So for partial sealed directories under a shared opaque we
                            // don't establish any specific policies
                            if (m_directoryArtifactContext.GetSealDirectoryKind(directory) != SealDirectoryKind.Partial || !IsPathUnderASharedOpaqueRoot(directory.Path))
                            {
                                // TODO: Consider an UntrackedScope or UntrackedPath above that has exactly the same path.
                                //       That results in a manifest entry with AllowWrite masked out yet re-added via AllowWrite in the values.
                                //       Maybe the precedence should be (parent | values) & mask instead of (parent & mask) | values.
                                mask &= ~FileAccessPolicy.AllowWrite;
                                AllowCreateDirectoryForDirectoriesOnPath(directory.Path, processedPaths, false);
                            }

                            m_fileAccessManifest.AddScope(directory, mask: mask, values: values);
                        }
                    }
                }

                m_fileAccessManifest.MonitorChildProcesses = !pip.HasUntrackedChildProcesses;

                if (!string.IsNullOrEmpty(m_detoursFailuresFile))
                {
                    m_fileAccessManifest.AddPath(AbsolutePath.Create(m_pathTable, m_detoursFailuresFile), values: FileAccessPolicy.AllowAll, mask: ~FileAccessPolicy.ReportAccess);
                }

                if (m_sandboxConfig.LogFileAccessTables)
                {
                    LogFileAccessTables(pip);
                }
            }
        }

        /// <summary>
        /// If a supplied path is under real/redirected user profile, creates a corresponding path under redirected/real user profile.
        /// If profile redirect is disabled or the path is not under real/redirected user profile, returns AbsolutePath.Invalid.
        /// </summary>
        private AbsolutePath CreatePathForActualRedirectedUserProfilePair(AbsolutePath path, AbsolutePath realUserProfilePath, AbsolutePath redirectedUserProfilePath)
        {
            if (!redirectedUserProfilePath.IsValid)
            {
                return AbsolutePath.Invalid;
            }

            // path is in terms of realUserProfilePath -> create a path in terms of redirectedUserProfilePath
            if (path.IsWithin(m_pathTable, realUserProfilePath))
            {
                return path.Relocate(m_pathTable, realUserProfilePath, redirectedUserProfilePath);
            }

            // path is in terms of m_redirectedUserProfilePath -> create a path in terms of m_realUserProfilePath
            if (path.IsWithin(m_pathTable, redirectedUserProfilePath))
            {
                return path.Relocate(m_pathTable, redirectedUserProfilePath, realUserProfilePath);
            }

            return AbsolutePath.Invalid;
        }

        private void AddSharedOpaqueInputContentToManifest(DirectoryArtifact directory, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            var content = m_directoryArtifactContext.ListSealDirectoryContents(directory);

            foreach (var fileArtifact in content)
            {
                AddDynamicInputFileAndAncestorsToManifest(fileArtifact, allInputPathsUnderSharedOpaques, directory.Path);
            }
        }

        private void AddDynamicInputFileAndAncestorsToManifest(FileArtifact file, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques, AbsolutePath sharedOpaqueRoot)
        {
            // Allow reads, but fake times, since this is an input to the pip
            // We want to report these accesses since they are dynamic and need to be cross checked
            var path = file.Path;
            m_fileAccessManifest.AddPath(
                path,
                values: FileAccessPolicy.AllowRead | FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportAccess,
                // The file dependency may be under the cone of a shared opaque, which will give write access
                // to it. Explicitly block this (no need to check if this is under a shared opaque, since otherwise
                // it didn't have write access to begin with). Observe we already know this is not a rewrite since dynamic rewrites
                // are not allowed by construction under shared opaques.
                mask: ~FileAccessPolicy.AllowRealInputTimestamps & ~FileAccessPolicy.AllowWrite);

            allInputPathsUnderSharedOpaques.Add(path);

            // The containing directories up to the shared opaque root need timestamp faking as well
            AddDirectoryAncestorsToManifest(path, allInputPathsUnderSharedOpaques, sharedOpaqueRoot);
        }

        private void AddDirectoryAncestorsToManifest(AbsolutePath path, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques, AbsolutePath sharedOpaqueRoot)
        {
            // Fake the timestamp of all directories that are ancestors of the path, but inside the shared opaque root
            // Rationale: probes may be performed on those directories (directory probes don't need declarations)
            // so they need to be faked as well
            var currentPath = path.GetParent(m_pathTable);
            while (!allInputPathsUnderSharedOpaques.Contains(currentPath) && currentPath.IsWithin(m_pathTable, sharedOpaqueRoot))
            {
                // We want to set a policy for the directory without affecting the scope for the underlying artifacts
                m_fileAccessManifest.AddPath(
                        currentPath,
                        values: FileAccessPolicy.AllowRead | FileAccessPolicy.AllowReadIfNonexistent, // we don't need access reporting here
                        mask: ~FileAccessPolicy.AllowRealInputTimestamps); // but block real timestamps

                allInputPathsUnderSharedOpaques.Add(currentPath);
                currentPath = currentPath.GetParent(m_pathTable);
            }
        }

        private bool IsPathUnderASharedOpaqueRoot(AbsolutePath path)
        {
            // To determine if a path is under a shared opaque we need just to check if there is a containing shared opaque,
            // so we don't need to look for the outmost containing opaque
            return TryGetContainingSharedOpaqueRoot(path, getOutmostRoot: false, out _);
        }

        private bool TryGetContainingSharedOpaqueRoot(AbsolutePath path, bool getOutmostRoot, out AbsolutePath sharedOpaqueRoot)
        {
            sharedOpaqueRoot = AbsolutePath.Invalid;
            // Let's shortcut the case when there are no shared opaques at all for this pip, so
            // we play conservatively regarding perf wrt existing code paths
            if (m_sharedOpaqueDirectoryRoots.Count == 0)
            {
                return false;
            }

            foreach (var current in m_pathTable.EnumerateHierarchyBottomUp(path.Value))
            {
                var currentAsPath = new AbsolutePath(current);
                if (m_sharedOpaqueDirectoryRoots.Contains(currentAsPath))
                {
                    sharedOpaqueRoot = currentAsPath;
                    // If the outmost root was not requested, we can shortcut the search and return when
                    // we find the first match
                    if (!getOutmostRoot)
                    {
                        return true;
                    }
                }
            }

            return sharedOpaqueRoot != AbsolutePath.Invalid;
        }

        private void AddStaticFileDependenciesToFileAccessManifest(PooledObjectWrapper<HashSet<AbsolutePath>> wrapper, ReadOnlyArray<FileArtifact> fileDependencies, HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            var outputIds = wrapper.Instance;
            foreach (FileArtifact dependency in fileDependencies)
            {
                // Outputs have already been added with read-write access. We should not attempt to add a less permissive entry.
                if (!outputIds.Contains(dependency.Path))
                {
                    AddStaticFileDependencyToFileAccessManifest(dependency, allInputPathsUnderSharedOpaques);
                }
            }
        }

        private void AddStaticFileDependencyToFileAccessManifest(FileArtifact dependency, HashSet<AbsolutePath> allInputPaths)
        {
            // TODO:22476: We allow inputs to not exist. Is that the right thing to do?
            // We mask 'report' here, since inputs are expected read and should never fail observed-access validation (directory dependency, etc.)
            // Note that they would perhaps fail otherwise (simplifies the observed access checks in the first place).

            var path = dependency.Path;

            bool pathIsUnderSharedOpaque = TryGetContainingSharedOpaqueRoot(path, getOutmostRoot: true, out var sharedOpaqueRoot);

            m_fileAccessManifest.AddPath(
                path,
                values: FileAccessPolicy.AllowRead | FileAccessPolicy.AllowReadIfNonexistent,
                // Make sure we fake the input timestamp
                // The file dependency may be under the cone of a shared opaque, which will give write access
                // to it. Explicitly block this, since we want inputs to not be written. Observe we already know 
                // this is not a rewrite.
                mask: m_excludeReportAccessMask &
                      ~FileAccessPolicy.AllowRealInputTimestamps &
                      (pathIsUnderSharedOpaque ? 
                          ~FileAccessPolicy.AllowWrite: 
                          FileAccessPolicy.MaskNothing)); 

            allInputPaths.Add(path);

            // If the file artifact is under the root of a shared opaque we make sure all the directories
            // walking that path upwards get added to the manifest explicitly, so timestamp faking happens for them
            // We need the outmost matching root in case shared opaques are nested within each other: timestamp faking
            // needs to happen for all directories under all shared opaques
            if (pathIsUnderSharedOpaque)
            {
                AddDirectoryAncestorsToManifest(path, allInputPaths, sharedOpaqueRoot);
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1802:FxCopIsBuggy")]
        private static readonly int s_maxTempDirectoryLength = FileUtilities.MaxDirectoryPathLength(); // IsOSVersionGreaterOrEqual(6, 2) ? 260 : 130;

        /// <summary>
        /// Tests to see if the semantic path info is invalid (path was not under a mount) or writable
        /// (path is under a writable mount). Paths not under mounts don't have any enforcement around them
        /// so we allow them to be written.
        /// </summary>
        private static bool IsInvalidOrWritable(in SemanticPathInfo semanticPathInfo)
        {
            return !semanticPathInfo.IsValid || semanticPathInfo.IsWritable;
        }

        /// <summary>
        /// Returns the list of necessary directories to exist before the given pip can be executed
        /// </summary>
        /// <param name="pip">The process pip to analyze</param>
        /// <param name="pathTable">The PathTable</param>
        /// <param name="semanticPathExpander">the semantic expander</param>
        /// <returns>List of directories to be created before a pip can execute</returns>
        public static IEnumerable<string> GetDirectoriesToCreate(Process pip, PathTable pathTable, SemanticPathExpander semanticPathExpander)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (semanticPathExpander == null || IsInvalidOrWritable(semanticPathExpander.GetSemanticPathInfo(pip.WorkingDirectory)))
            {
                // Only write the working directory if its not under read-only root.
                directories.Add(pip.WorkingDirectory.ToString(pathTable));
            }

            // Support for (opaque) output directories.
            foreach (DirectoryArtifact outputDirectory in pip.DirectoryOutputs)
            {
                directories.Add(outputDirectory.Path.ToString(pathTable));
            }

            using (var wrapper = Pools.GetAbsolutePathSet())
            {
                var outputDirectoryIds = wrapper.Instance;
                foreach (FileArtifactWithAttributes output in pip.FileOutputs)
                {
                    outputDirectoryIds.Add(output.Path.GetParent(pathTable));
                }

                foreach (AbsolutePath outputDirectoryId in outputDirectoryIds)
                {
                    directories.Add(outputDirectoryId.ToString(pathTable));
                }
            }

            return directories;
        }

        private bool PrepareWorkingDirectory()
        {
            if (m_semanticPathExpander == null || IsInvalidOrWritable(m_semanticPathExpander.GetSemanticPathInfo(m_pip.WorkingDirectory)))
            {
                var workingDirectoryPath = m_pip.WorkingDirectory.ToString(m_pathTable);
                try
                {
                    FileUtilities.CreateDirectory(workingDirectoryPath);
                }
                catch (BuildXLException ex)
                {
                    LogOutputPreparationFailed(workingDirectoryPath, ex);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates and cleans the Process's temp directory if necessary
        /// </summary>
        /// <param name="environmentVariables">Environment</param>
        private bool PrepareTempDirectory(IBuildParameters environmentVariables)
        {
            Contract.Requires(environmentVariables != null);

            string path = null;

            // If specified, clean the pip specific temp directory.
            if (m_pip.TempDirectory.IsValid)
            {
                if (!CleanTempDirectory(m_pip.TempDirectory))
                {
                    return false;
                }

                Contract.Assert(m_fileAccessManifest != null);

                // Allow creation of symlinks in temp directories.
                m_fileAccessManifest.AddScope(m_pip.TempDirectory, values: FileAccessPolicy.AllowSymlinkCreation, mask: m_excludeReportAccessMask);
            }

            // Clean all specified temp directories.
            foreach (var additionalTempDirectory in m_pip.AdditionalTempDirectories)
            {
                if (!CleanTempDirectory(additionalTempDirectory))
                {
                    return false;
                }

                Contract.Assert(m_fileAccessManifest != null);

                // Allow creation of symlinks in temp directories.
                m_fileAccessManifest.AddScope(additionalTempDirectory, mask: m_excludeReportAccessMask, values: FileAccessPolicy.AllowSymlinkCreation);
            }

            try
            {
                // Many things get angry if temp directories don't exist so ensure they're created regardless of
                // what they're set to.
                // TODO:Bug 75124 - should validate these paths
                foreach (var tmpEnvVar in DisallowedTempVariables)
                {
                    path = environmentVariables[tmpEnvVar];
                    FileUtilities.CreateDirectory(path);
                }
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipTempDirectorySetupError(m_loggingContext, path, ex.Message);
                return false;
            }

            return true;
        }

        private bool CleanTempDirectory(AbsolutePath tempDirectoryPath)
        {
            Contract.Requires(tempDirectoryPath.IsValid);

            string path = tempDirectoryPath.ToString(m_pathTable);

            if (path.Length > s_maxTempDirectoryLength)
            {
                LogTempDirectoryTooLong(path);
            }

            try
            {
                // Temp directories are lazily, best effort cleaned after the pip finished. The previous build may not
                // have finished this work before exiting so we must double check.
                PreparePathForOutputDirectory(path);
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipTempDirectoryCleanupError(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    path,
                    ex.LogEventMessage);

                return false;
            }

            return true;
        }

        private void LogTempDirectoryTooLong(string directory)
        {
            Tracing.Logger.Log.PipProcessTempDirectoryTooLong(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                directory);
        }

        /// <summary>
        /// Each output of this process is either written (output only) or rewritten (input and output).
        /// We delete written outputs (shouldn't be observed) and re-deploy rewritten outputs (should be a private copy).
        /// Rewritten outputs may be initially non-private and so not writable, i.e., hardlinked to other locations.
        /// Note that this function is also responsible for stamping private outputs such that the tool sees
        /// <see cref="WellKnownTimestamps.OldOutputTimestamp"/>.
        /// </summary>
        private async Task<bool> PrepareOutputs()
        {
            using (var preserveOutputWhitelistWrapper = Pools.GetAbsolutePathSet())
            using (var dependenciesWrapper = Pools.GetAbsolutePathSet())
            using (var outputDirectoriesWrapper = Pools.GetAbsolutePathSet())
            {
                var preserveOutputWhitelist = preserveOutputWhitelistWrapper.Instance;
                foreach (AbsolutePath path in m_pip.PreserveOutputWhitelist)
                {
                    preserveOutputWhitelist.Add(path);
                }

                if (!await PrepareDirectoryOutputs(preserveOutputWhitelist))
                {
                    return false;
                }

                if (!PrepareInContainerPaths())
                {
                    return false;
                }

                var dependencies = dependenciesWrapper.Instance;

                foreach (FileArtifact dependency in m_pip.Dependencies)
                {
                    dependencies.Add(dependency.Path);
                }

                var outputDirectories = outputDirectoriesWrapper.Instance;

                foreach (FileArtifactWithAttributes output in m_pip.FileOutputs)
                {
                    // Only subset of all outputs should be deleted, because some times we may want a tool to see its prior outputs
                    if (!output.DeleteBeforeRun())
                    {
                        continue;
                    }

                    try
                    {
                        if (!dependencies.Contains(output.Path))
                        {
                            if (ShouldPreserveDeclaredOutput(output.Path, preserveOutputWhitelist))
                            {
                                Contract.Assume(m_makeOutputPrivate != null);

                                // A process may be configured to allow its prior outputs to be seen by future
                                // invocations. In this case we must make sure the outputs are no longer hardlinked to
                                // the cache to allow them to be writeable.
                                if (!await m_makeOutputPrivate(output.Path.ToString(m_pathTable)))
                                {
                                    // Delete the file if it exists.
                                    PreparePathForOutputFile(output.Path, outputDirectories);
                                }
                            }
                            else
                            {
                                // Delete the file, since we aren't re-writing it. Note that we do not use System.IO.File.Delete here,
                                // since we need to be tolerant to several exotic cases:
                                // - A previous run of the tool may have written a file and marked it as readonly.
                                // - There may be concurrent users of the file being deleted, but via other hardlinks:
                                // This DeleteFile tries several strategies relevant to those circumstances.
                                PreparePathForOutputFile(output.Path, outputDirectories);
                            }
                        }
                        else
                        {
                            Contract.Assume(output.RewriteCount > 0);
                            var inputVersionOfOutput = new FileArtifact(output.Path, output.RewriteCount - 1);
#if DEBUG
                            Contract.Assume(m_pip.Dependencies.Contains(inputVersionOfOutput), "Each rewrite increments the rewrite count by one");
#endif

                            if (m_makeInputPrivate != null)
                            {
                                if (!await m_makeInputPrivate(inputVersionOfOutput))
                                {
                                    throw new BuildXLException("Failed to create a private, writable copy of the file so that it could be re-written.");
                                }
                            }

                            try
                            {
                                FileUtilities.SetFileTimestamps(
                                    output.Path.ToString(m_pathTable),
                                    new FileTimestamps(WellKnownTimestamps.OldOutputTimestamp));
                            }
                            catch (Exception ex)
                            {
                                throw new BuildXLException("Failed to open an output file for writing (it was just made writable and private to be written by a process).", ex);
                            }
                        }
                    }
                    catch (BuildXLException ex)
                    {
                        LogOutputPreparationFailed(output.Path.ToString(m_pathTable), ex);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Makes sure all redirected directories exist and are empty
        /// </summary>
        private bool PrepareInContainerPaths()
        {
            foreach(ExpandedAbsolutePath outputDir in m_containerConfiguration.RedirectedDirectories.Keys)
            {
                try
                {
                    PreparePathForDirectory(outputDir.ExpandedPath, createIfNonExistent: true);
                }
                catch (BuildXLException ex)
                {
                    LogOutputPreparationFailed(outputDir.ExpandedPath, ex);
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> PrepareDirectoryOutputs(HashSet<AbsolutePath> preserveOutputWhitelist)
        {
            foreach (var directoryOutput in m_pip.DirectoryOutputs)
            {
                try
                {
                    string directoryPathStr = directoryOutput.Path.ToString(m_pathTable);
                    bool dirExist = FileUtilities.DirectoryExistsNoFollow(directoryPathStr);

                    if (directoryOutput.IsSharedOpaque)
                    {
                        // Ensure it exists.
                        if (!dirExist)
                        {
                            FileUtilities.CreateDirectory(directoryPathStr);
                        }
                    }
                    else
                    {
                        if (dirExist && ShouldPreserveDeclaredOutput(directoryOutput.Path, preserveOutputWhitelist))
                        {
                            using (var wrapper = Pools.GetStringList())
                            {
                                var filePaths = wrapper.Instance;
                                FileUtilities.EnumerateDirectoryEntries(
                                    directoryPathStr,
                                    recursive: true,
                                    handleEntry: (currentDir, name, attributes) =>
                                    {
                                        if ((attributes & FileAttributes.Directory) == 0)
                                        {
                                            var path = Path.Combine(currentDir, name);
                                            filePaths.Add(path);
                                        }
                                    });

                                foreach (var path in filePaths)
                                {
                                    if (!await m_makeOutputPrivate(path))
                                    {
                                        throw new BuildXLException("Failed to create a private, writeable copy of an output file from a previous invocation.");
                                    }
                                }
                            }
                        }
                        else
                        {
                            PreparePathForOutputDirectory(directoryPathStr, createIfNonExistent: true);
                        }
                    }
                }
                catch (BuildXLException ex)
                {
                    LogOutputPreparationFailed(directoryOutput.Path.ToString(m_pathTable), ex);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// If used, response files must be created before executing pips that consume them
        /// </summary>
        private async Task<bool> PrepareResponseFile()
        {
            if (m_pip.ResponseFile.IsValid)
            {
                Contract.Assume(m_pip.ResponseFileData.IsValid, "ResponseFile path requires having ResponseFile data");
                string destination = m_pip.ResponseFile.Path.ToString(m_context.PathTable);
                string contents = m_pip.ResponseFileData.ToString(m_context.PathTable);

                try
                {
                    string directoryName = ExceptionUtilities.HandleRecoverableIOException(
                        () => Path.GetDirectoryName(destination),
                        ex => { throw new BuildXLException("Cannot get directory name", ex); });

                    PreparePathForOutputFile(m_pip.ResponseFile.Path);
                    FileUtilities.CreateDirectory(directoryName);

                    // The target is always overwritten
                    await FileUtilities.WriteAllTextAsync(destination, contents, Encoding.UTF8);
                }
                catch (BuildXLException ex)
                {
                    Tracing.Logger.Log.PipProcessResponseFileCreationFailed(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        destination,
                        ex.LogEventErrorCode,
                        ex.LogEventMessage);

                    return false;
                }
            }

            return true;
        }

        private enum SpecialProcessKind : byte
        {
            NotSpecial = 0,
            Csc = 1,
            Cvtres = 2,
            Resonexe = 3,
            RC = 4,
            CCCheck = 5,
            CCDocGen = 6,
            CCRefGen = 7,
            CCRewrite = 8,
            WinDbg = 9,
            XAMLWrapper = 11,
            Mt = 12
        }

        private static readonly Dictionary<string, SpecialProcessKind> s_specialTools = new Dictionary<string, SpecialProcessKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["csc"] = SpecialProcessKind.Csc,
            ["csc.exe"] = SpecialProcessKind.Csc,
            ["cvtres"] = SpecialProcessKind.Cvtres,
            ["cvtres.exe"] = SpecialProcessKind.Cvtres,
            ["cvtress.exe"] = SpecialProcessKind.Cvtres, // Legacy.
            ["reson"] = SpecialProcessKind.Resonexe,
            ["resonexe.exe"] = SpecialProcessKind.Resonexe,
            ["rc"] = SpecialProcessKind.RC,
            ["rc.exe"] = SpecialProcessKind.RC,
            ["mt"] = SpecialProcessKind.Mt,
            ["mt.exe"] = SpecialProcessKind.Mt,
            ["windbg"] = SpecialProcessKind.WinDbg,
            ["windbg.exe"] = SpecialProcessKind.WinDbg,
            ["tool.xamlcompilerwrapper"] = SpecialProcessKind.XAMLWrapper,
            ["tool.xamlcompilerwrapper.exe"] = SpecialProcessKind.XAMLWrapper,

            // TODO: deprecate this.
            ["cccheck"] = SpecialProcessKind.CCCheck,
            ["cccheck.exe"] = SpecialProcessKind.CCCheck,
            ["ccdocgen"] = SpecialProcessKind.CCDocGen,
            ["ccdocgen.exe"] = SpecialProcessKind.CCDocGen,
            ["ccrefgen"] = SpecialProcessKind.CCRefGen,
            ["ccrefgen.exe"] = SpecialProcessKind.CCRefGen,
            ["ccrewrite"] = SpecialProcessKind.CCRewrite,
            ["ccrewrite.exe"] = SpecialProcessKind.CCRewrite,
        };

        /// <summary>
        /// Gets the kind of process.
        /// </summary>
        /// <returns>The kind of process based on the process' name.</returns>
        /// <remarks>
        /// If <paramref name="processOverride"/> is invalid, then the process kind is obtained from the executable path specified in the pip.
        /// </remarks>
        [SuppressMessage("Microsoft.Globalization", "CA1309", Justification = "Already using Comparison.OrdinalIgnoreCase - looks like a bug in FxCop rules.")]
        private SpecialProcessKind GetProcessKind(AbsolutePath processOverride)
        {
            AbsolutePath processPath = processOverride.IsValid ? processOverride : m_pip.Executable.Path;
            string toolName = processPath.GetName(m_pathTable).ToString(m_pathTable.StringTable);

            return s_specialTools.TryGetValue(toolName.ToLowerInvariant(), out SpecialProcessKind kind) ? kind : SpecialProcessKind.NotSpecial;
        }

        private static bool StringLooksLikeRCTempFile(string fileName)
        {
            int len = fileName.Length;
            if (len < 9)
            {
                return false;
            }

            char c1 = fileName[len - 9];
            if (c1 != '\\')
            {
                return false;
            }

            char c2 = fileName[len - 8];
            if (c2.ToUpperInvariantFast() != 'R')
            {
                return false;
            }

            char c3 = fileName[len - 7];
            if (c3.ToUpperInvariantFast() != 'C' && c3.ToUpperInvariantFast() != 'D' && c3.ToUpperInvariantFast() != 'F')
            {
                return false;
            }

            return true;
        }

        private static bool StringLooksLikeMtTempFile(string fileName)
        {
            if (!fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int beginCharIndex = fileName.LastIndexOf('\\');

            if (beginCharIndex == -1 || beginCharIndex + 3 >= fileName.Length)
            {
                return false;
            }

            char c1 = fileName[beginCharIndex + 1];
            if (c1 != 'R')
            {
                return false;
            }

            char c2 = fileName[beginCharIndex + 2];
            if (c2 != 'C')
            {
                return false;
            }

            char c3 = fileName[beginCharIndex + 3];
            if (c3 != 'X')
            {
                return false;
            }

            return true;
        }

        private static bool StringLooksLikeBuildExeTraceLog(string fileName)
        {
            // detect filenames of the following form
            // _buildc_dep_out.pass<NUMBER>
            int len = fileName.Length;

            int trailingDigits = 0;
            for (; len > 0 && fileName[len - 1] >= '0' && fileName[len - 1] <= '9'; len--)
            {
                trailingDigits++;
            }

            if (trailingDigits == 0)
            {
                return false;
            }

            return fileName.EndsWith("_buildc_dep_out.pass", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether we should exclude special file access reports from processing
        /// </summary>
        /// <param name="fileAccessPath">The reported file  access path.</param>
        /// <returns>True is the file should be excluded, otherwise false.</returns>
        /// <remarks>
        /// This accounts for accesses when the FileAccessIgnoreCodeCoverage is set. A lot of our tests are running
        /// with this flag set..
        /// </remarks>
        private bool GetSpecialCaseRulesForCoverageAndSpecialDevices(
            AbsolutePath fileAccessPath)
        {
            Contract.Assert(fileAccessPath != AbsolutePath.Invalid);
            Contract.Assert(!string.IsNullOrEmpty(fileAccessPath.ToString(m_pathTable)));

            string accessedPath = fileAccessPath.ToString(m_pathTable);

            // When running test cases with Code Coverage enabled, some more files are loaded that we should ignore
            if (m_sandboxConfig.FileAccessIgnoreCodeCoverage)
            {
                if (accessedPath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                    accessedPath.EndsWith(".nls", StringComparison.OrdinalIgnoreCase) ||
                    accessedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether we should exclude special file access reports from processing
        /// </summary>
        /// <param name="processPath">Path of process that access the file.</param>
        /// <param name="fileAccessPath">The reported file  access path.</param>
        /// <returns>True is the file should be excluded, otherwise false.</returns>
        /// <remarks>
        /// Some perform file accesses, which don't yet fall into any configurable file access manifest category.
        /// These special tools/cases should be whitelisted, but we already have customers deployed specs without
        /// using white lists.
        /// </remarks>
        private bool GetSpecialCaseRulesForSpecialTools(AbsolutePath processPath, AbsolutePath fileAccessPath)
        {
            if (m_pip.PipType != PipType.Process)
            {
                return true;
            }

            string fileName = fileAccessPath.ToString(m_pathTable);

            switch (GetProcessKind(processPath))
            {
                case SpecialProcessKind.Csc:
                case SpecialProcessKind.Cvtres:
                case SpecialProcessKind.Resonexe:
                    // Some tools emit temporary files into the same directory
                    // as the final output file.
                    if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.RC:
                    // The native resource compiler (RC) emits temporary files into the same
                    // directory as the final output file.
                    if (StringLooksLikeRCTempFile(fileName))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.Mt:
                    if (StringLooksLikeMtTempFile(fileName))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.CCCheck:
                case SpecialProcessKind.CCDocGen:
                case SpecialProcessKind.CCRefGen:
                case SpecialProcessKind.CCRewrite:
                    // The cc-line of tools like to find pdb files by using the pdb path embedded in a dll/exe.
                    // If the dll/exe was built with different roots, then this results in somewhat random file accesses.
                    if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.WinDbg:
                case SpecialProcessKind.NotSpecial:
                    // no special treatment
                    break;
            }

            // build.exe and tracelog.dll capture dependency information in temporary files in the object root called _buildc_dep_out.<pass#>
            if (StringLooksLikeBuildExeTraceLog(fileName))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates an <see cref="ObservedFileAccess"/> for each unique path accessed.
        /// The returned array is sorted by the expanded path and additionally contains no duplicates.
        /// </summary>
        /// <remarks>
        /// Additionally, it returns the write accesses that were observed in shared dynamic
        /// directories, which are used later to determine pip ownership.
        /// </remarks>
        private SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> GetObservedFileAccesses(
            SandboxedProcessResult result,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            out List<AbsolutePath> unobservedOutputs,
            out IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedDynamicDirectoryWriteAccesses)
        {
            unobservedOutputs = null;
            if (result.ExplicitlyReportedFileAccesses == null || result.ExplicitlyReportedFileAccesses.Count == 0)
            {
                unobservedOutputs = m_pip.FileOutputs.Where(f => RequireOutputObservation(f)).Select(f => f.Path).ToList();
                sharedDynamicDirectoryWriteAccesses = CollectionUtilities.EmptyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>>();
                return SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<ObservedFileAccess>.Empty,
                        new ObservedFileAccessExpandedPathComparer(m_context.PathTable.ExpandedPathComparer));
            }

            // Note that we are enumerating an unordered set to produce the array of observed paths.
            // As noted in SandboxedProcessPipExecutionResult, the caller must assume no particular order.
            // Since observed accesses contribute to a descriptor value (rather than a hashed key), this is fine; no normalization needed.
            // Since we're projecting many acceses into groups per path into just paths, we need a temporary dictionary.
            // TODO: Allocations ahoy!
            using (PooledObjectWrapper<Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>>> accessesByPathWrapper = ProcessPools.ReportedFileAccessesByPathPool.GetInstance())
            {
                Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>> accessesByPath = accessesByPathWrapper.Instance;
                var excludedToolsAndPaths = new HashSet<(AbsolutePath, AbsolutePath)>();

                foreach (ReportedFileAccess reported in result.ExplicitlyReportedFileAccesses)
                {
                    Contract.Assume(reported.Status == FileAccessStatus.Allowed, "Explicitly reported accesses are defined to be successful");

                    // Enumeration probes have a corresponding Enumeration access (also explicitly reported).
                    // Presently we are interested in capturing the existence of enumerations themselves rather than what was seen
                    // (and for NtQueryDirectoryFile, we can't always report the individual probes anyway).
                    if (reported.RequestedAccess == RequestedAccess.EnumerationProbe)
                    {
                        continue;
                    }

                    AbsolutePath parsedPath;

                    // We want an AbsolutePath for the full access. This may not be parse-able due to the accessed path
                    // being invalid, or a path format we do not understand. Note that TryParseAbsolutePath logs as appropriate
                    // in the latter case.
                    if (!reported.TryParseAbsolutePath(m_context, m_pip, out parsedPath))
                    {
                        continue;
                    }

                    bool shouldExclude = false;

                    // Remove special accesses see Bug: #121875.
                    // Some perform file accesses, which don't yet fall into any configurable file access manifest category.
                    // These special tools/cases should be whitelisted, but we already have customers deployed specs without
                    // using white lists.
                    if (GetSpecialCaseRulesForCoverageAndSpecialDevices(parsedPath))
                    {
                        shouldExclude = true;
                    }
                    else
                    {
                        if (AbsolutePath.TryCreate(m_context.PathTable, reported.Process.Path, out AbsolutePath processPath)
                            && (excludedToolsAndPaths.Contains((processPath, parsedPath))
                                || GetSpecialCaseRulesForSpecialTools(processPath, parsedPath)))
                        {
                            shouldExclude = true;
                            excludedToolsAndPaths.Add((processPath, parsedPath));
                        }
                    }

                    accessesByPath.TryGetValue(parsedPath, out CompactSet<ReportedFileAccess> existingAccessesToPath);
                    accessesByPath[parsedPath] =
                        !shouldExclude
                        ? existingAccessesToPath.Add(reported)
                        : existingAccessesToPath;
                }

                foreach (var output in m_pip.FileOutputs)
                {
                    if (!accessesByPath.ContainsKey(output.Path))
                    {
                        if (RequireOutputObservation(output))
                        {
                            unobservedOutputs = unobservedOutputs ?? new List<AbsolutePath>();
                            unobservedOutputs.Add(output.Path);
                        }
                    }
                    else
                    {
                        accessesByPath.Remove(output.Path);
                    }
                }

                using (PooledObjectWrapper<Dictionary<AbsolutePath, HashSet<AbsolutePath>>> dynamicWriteAccessWrapper =
                    ProcessPools.DynamicWriteAccesses.GetInstance())
                using (PooledObjectWrapper<List<ObservedFileAccess>> accessesUnsortedWrapper =
                    ProcessPools.AccessUnsorted.GetInstance())
                {
                    // Initializes all shared directories in the pip with no accesses
                    var dynamicWriteAccesses = dynamicWriteAccessWrapper.Instance;
                    foreach (var sharedDirectory in m_sharedOpaqueDirectoryRoots)
                    {
                        dynamicWriteAccesses[sharedDirectory] = new HashSet<AbsolutePath>();
                    }

                    // Remove all the special file accesses that need removal.
                    RemoveEmptyOrInjectableFileAccesses(accessesByPath);

                    var accessesUnsorted = accessesUnsortedWrapper.Instance;
                    foreach (KeyValuePair<AbsolutePath, CompactSet<ReportedFileAccess>> entry in accessesByPath)
                    {
                        bool? isDirectoryLocation = null;
                        bool hasEnumeration = false;
                        bool isProbe = true;

                        // There is always at least one access for reported path by construction
                        // Since the set of accesses occur on the same path, the manifest path is
                        // the same for all of them. We only need to query one of them.
                        ReportedFileAccess firstAccess = accessesByPath[entry.Key].First();

                        bool isPathCandidateToBeOwnedByASharedOpaque = false;
                        foreach (var access in entry.Value)
                        {
                            // Detours reports a directory probe with a trailing backslash.
                            if (access.Path != null && access.Path.EndsWith("\\", StringComparison.OrdinalIgnoreCase) &&
                                (isDirectoryLocation == null || isDirectoryLocation.Value))
                            {
                                isDirectoryLocation = true;
                            }
                            else
                            {
                                isDirectoryLocation = false;
                            }

                            // To treat the paths as file probes, all accesses to the path must be the probe access.
                            isProbe &= access.RequestedAccess == RequestedAccess.Probe;

                            // TODO: Remove this when WDG can grog this feature with no flag.
                            if (m_sandboxConfig.UnsafeSandboxConfiguration.ExistingDirectoryProbesAsEnumerations ||
                                access.RequestedAccess == RequestedAccess.Enumerate)
                            {
                                hasEnumeration = true;
                            }

                            // if the access is a write (and not a directory creation), then the path is a candidate to be part of a shared opaque
                            isPathCandidateToBeOwnedByASharedOpaque |= (access.RequestedAccess & RequestedAccess.Write) != RequestedAccess.None &&
                                                                       access.Operation != ReportedFileOperation.CreateDirectory;
                        }

                        // if the path is still a candidate to be part of a shared opaque, that means there was at least a write to that path. If the path is then
                        // in the cone of a shared opaque, then it is a dynamic write access
                        if (isPathCandidateToBeOwnedByASharedOpaque && IsAccessUnderASharedOpaque(
                                firstAccess,
                                dynamicWriteAccesses,
                                out AbsolutePath sharedDynamicDirectoryRoot))
                        {
                            dynamicWriteAccesses[sharedDynamicDirectoryRoot].Add(entry.Key);
                            // This is a known output, so don't store it
                            continue;
                        }

                        // If the access occurred under any of the pip shared opaque outputs, and the access is not happening on any known input paths (neither dynamic nor static)
                        // then we just skip reporting the access. Together with the above step, this means that no accesses under shared opaques that represent outputs are actually
                        // reported as observed accesses. This matches the same behavior that occurs on static outputs.
                        if (!allInputPathsUnderSharedOpaques.Contains(entry.Key) && IsAccessUnderASharedOpaque(firstAccess, dynamicWriteAccesses, out _))
                        {
                            continue;
                        }

                        ObservationFlags observationFlags = ObservationFlags.None;

                        if (isProbe)
                        {
                            observationFlags |= ObservationFlags.FileProbe;
                        }

                        if (isDirectoryLocation != null && isDirectoryLocation.Value)
                        {
                            observationFlags |= ObservationFlags.DirectoryLocation;
                        }

                        if (hasEnumeration)
                        {
                            observationFlags |= ObservationFlags.Enumeration;
                        }

                        accessesUnsorted.Add(new ObservedFileAccess(entry.Key, observationFlags, entry.Value));
                    }

                    sharedDynamicDirectoryWriteAccesses = dynamicWriteAccesses.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (IReadOnlyCollection<AbsolutePath>)kvp.Value.ToReadOnlyArray());

                    return SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>.CloneAndSort(
                        accessesUnsorted,
                        new ObservedFileAccessExpandedPathComparer(m_context.PathTable.ExpandedPathComparer));
                }
            }
        }

        private bool IsAccessUnderASharedOpaque(ReportedFileAccess access, Dictionary<AbsolutePath, HashSet<AbsolutePath>> dynamicWriteAccesses, out AbsolutePath sharedDynamicDirectoryRoot)
        {
            sharedDynamicDirectoryRoot = AbsolutePath.Invalid;

            // Shortcut the search if there are no shared opaques or the manifest path
            // is invalid. For the latter, this can occur when the access happens on a location
            // the path table doesn't know about. But this means the access is not under a shared opaque.
            if (dynamicWriteAccesses.Count == 0 || !access.ManifestPath.IsValid)
            {
                return false;
            }

            // The only construct that defines a scope for detours that we allow under shared opaques is sealed directories
            // (other constructs are allowed, but they don't affect detours manifest)
            // This means we cannot directly use the manifest path to check if is the root of a shared opaque
            // but we can start looking up from the reported manifest path
            // Furthermore, nested shared opaques from the same pip are blocked, so the first shared opaque
            // we find by walking up the path is the one

            var initialNode = access.ManifestPath.Value;

            // TODO: consider adding a cache from manifest paths to containing shared opaques. It is likely
            // that many writes for a given pip happens under the same cones.

            foreach (var currentNode in m_context.PathTable.EnumerateHierarchyBottomUp(initialNode))
            {
                var currentPath = new AbsolutePath(currentNode);
                if (dynamicWriteAccesses.ContainsKey(currentPath))
                {
                    sharedDynamicDirectoryRoot = currentPath;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a path starts with a prefix, given the fact that the path may start with "\??\" or "\\?\".
        /// </summary>
        private static bool PathStartsWith(string path, string prefix)
        {
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith(@"\??\" + prefix, StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith(@"\\?\" + prefix, StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveEmptyOrInjectableFileAccesses(Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>> accessesByPath)
        {
            var accessesToRemove = new List<AbsolutePath>();

            // CollectionUtilities all the file accesses that need to be removed.
            foreach (var absolutePath in accessesByPath.Keys)
            {
                if (!absolutePath.IsValid)
                {
                    continue;
                }

                if (accessesByPath[absolutePath].Count == 0)
                {
                    // Remove empty accesses and don't bother checking the rest.
                    accessesToRemove.Add(absolutePath);
                    continue;
                }

                // Remove only entries that come from unknown or from System, or Invalid mounts.
                bool removeEntry = false;

                if (m_semanticPathExpander != null)
                {
                    SemanticPathInfo semanticPathInfo = m_semanticPathExpander.GetSemanticPathInfo(absolutePath);
                    removeEntry = !semanticPathInfo.IsValid ||
                        semanticPathInfo.IsSystem;
                }

                if (m_semanticPathExpander == null || removeEntry)
                {
                    if (IsRemovableInjectedFileAccess(absolutePath))
                    {
                        accessesToRemove.Add(absolutePath);
                    }
                }
            }

            // Now, remove all the entries that were scheduled for removal.
            foreach (AbsolutePath pathToRemove in accessesToRemove)
            {
                accessesByPath.Remove(pathToRemove);
            }
        }

        private void RemoveInjectableFileAccesses(ISet<ReportedFileAccess> unexpectedAccesses)
        {
            List<ReportedFileAccess> accessesToRemove = new List<ReportedFileAccess>();

            // CollectionUtilities all the file accesses that need to be removed.
            foreach (var reportedAccess in unexpectedAccesses)
            {
                AbsolutePath.TryCreate(m_pathTable, reportedAccess.GetPath(m_pathTable), out AbsolutePath absolutePath);
                if (!absolutePath.IsValid)
                {
                    continue;
                }

                // Remove only entries that come from unknown or from System, or Invalid mounts.
                bool removeEntry = false;

                if (m_semanticPathExpander != null)
                {
                    SemanticPathInfo semanticPathInfo = m_semanticPathExpander.GetSemanticPathInfo(absolutePath);
                    removeEntry = !semanticPathInfo.IsValid ||
                        semanticPathInfo.IsSystem;
                }

                if (m_semanticPathExpander == null || removeEntry)
                {
                    if (IsRemovableInjectedFileAccess(absolutePath))
                    {
                        accessesToRemove.Add(reportedAccess);
                    }
                }
            }

            // Now, remove all the entries that were scheduled for removal.
            foreach (ReportedFileAccess pathToRemove in accessesToRemove)
            {
                unexpectedAccesses.Remove(pathToRemove);
            }
        }

        private bool IsRemovableInjectedFileAccess(AbsolutePath absolutePath)
        {
            string path = absolutePath.ToString(m_pathTable);
            string filename = absolutePath.GetName(m_pathTable).IsValid ? absolutePath.GetName(m_pathTable).ToString(m_pathTable.StringTable) : null;
            string extension = absolutePath.GetExtension(m_pathTable).IsValid ? absolutePath.GetExtension(m_pathTable).ToString(m_pathTable.StringTable) : null;

            // Special case: The VC++ compiler probes %PATH% for c1xx.exe.  This file does not even exist, but
            // VC still looks for it.  We ignore it.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "c1xx.exe"))
            {
                return true;
            }

            // This Microsoft Tablet PC component injects itself into processes.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "tiptsf.dll"))
            {
                return true;
            }

            // This Microsoft Office InfoPath component injects itself into processes.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "MSOXMLMF.DLL"))
            {
                return true;
            }

            // This Bonjour Namespace Provider from Apple component injects itself into processes.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "mdnsNSP.DLL"))
            {
                return true;
            }

            // This ATI Technologies / HydraVision component injects itself into processes.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "HydraDMH.dll") ||
                StringComparer.OrdinalIgnoreCase.Equals(filename, "HydraDMH64.dll") ||
                StringComparer.OrdinalIgnoreCase.Equals(filename, "HydraMDH.dll") ||
                StringComparer.OrdinalIgnoreCase.Equals(filename, "HydraMDH64.dll"))
            {
                return true;
            }

            // Special case: VSTest.Console.exe likes to open these files if Visual Studio is installed. This is benign.
            if (StringComparer.OrdinalIgnoreCase.Equals(filename, "Microsoft.VisualStudio.TeamSystem.Licensing.dll") ||
                StringComparer.OrdinalIgnoreCase.Equals(filename, "Microsoft.VisualStudio.QualityTools.Sqm.dll"))
            {
                return true;
            }

            // Special case: On Windows 8, the CLR keeps ngen logs: http://msdn.microsoft.com/en-us/library/hh691758(v=vs.110).aspx
            // Alternative, one can disable this on one's machine:
            //   From: Mark Miller (CLR)
            //   Sent: Monday, March 24, 2014 10:57 AM
            //   Subject: RE: automatic native image generation
            //   You can disable Auto NGEN activity (creation of logs) by setting:
            //   HKLM\SOFTWARE\Microsoft\.NETFramework\NGen\Policy\[put version here]\OptimizeUsedBinaries = (REG_DWORD)0
            //   Do this in the Wow Hive as well and for each version (v2.0 and v4.0 is the entire set)
            //   Mark
            if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".log") &&
                path.StartsWith(s_appDataLocalMicrosoftClrPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Special case: The NVIDIA driver injects itself into all processes and accesses files under '%ProgamData%\NVIDIA Corporation'.
            // Also attempts to access aurora.dll and petromod_nvidia_profile_identifier.ogl files.
            // Ignore file accesses in that directory or with those file names.
            if (path.StartsWith(s_nvidiaProgramDataPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(s_nvidiaProgramFilesPrefix, StringComparison.OrdinalIgnoreCase)
                || StringComparer.OrdinalIgnoreCase.Equals(filename, "aurora.dll")
                || StringComparer.OrdinalIgnoreCase.Equals(filename, "petromod_nvidia_profile_identifier.ogl"))
            {
                return true;
            }

            // Special case: The Forefront TMG client injects itself into some processes and accesses files under %ProgramFilxX86%\Forefront TMG Client'.
            // The check also considers the fact that the input path can start with prefix, '\\?' or '\??\'.
            if (PathStartsWith(path, s_forefrontTmgClientProgramFilesX86Prefix))
            {
                return true;
            }

            return false;
        }

        private bool RequireOutputObservation(FileArtifactWithAttributes output)
        {
            // Don't check temp & optional, nor stdout/stderr files.
            return output.IsRequiredOutputFile &&
                    (output.Path != m_pip.StandardError.Path) &&
                    (output.Path != m_pip.StandardOutput.Path) &&

                    // Rewritten files are not required to be written by the tool
                    output.RewriteCount <= 1 &&

                    // Preserve output is incompatible with validation of output access.
                    // See Bug #1043533
                    !m_shouldPreserveOutputs &&
                    !m_pip.HasUntrackedChildProcesses &&
                    m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses;
        }

        private void LogFinishedFailed(SandboxedProcessResult result)
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessFinishedFailed(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), result.ExitCode);
        }

        private void LogTookTooLongWarning(TimeSpan timeout, TimeSpan time, TimeSpan warningTimeout)
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessTookTooLongWarning(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                Bound(time.TotalMilliseconds),
                Bound(warningTimeout.TotalMilliseconds),
                Bound(timeout.TotalMilliseconds));
        }

        private void LogTookTooLongError(SandboxedProcessResult result, TimeSpan timeout, TimeSpan time)
        {
            Contract.Assume(result.Killed);
            Analysis.IgnoreArgument(result);
            string dumpString = result.DumpFileDirectory ?? string.Empty;

            BuildXL.Processes.Tracing.Logger.Log.PipProcessTookTooLongError(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                Bound(time.TotalMilliseconds),
                Bound(timeout.TotalMilliseconds),
                dumpString);
        }

        private async Task<bool> TrySaveAndLogStandardError(SandboxedProcessResult result)
        {
            try
            {
                await result.StandardError.SaveAsync();
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(SandboxedProcessFile.StandardError), ex);
                return false;
            }

            string standardErrorPath = result.StandardError.FileName;
            BuildXL.Processes.Tracing.Logger.Log.PipProcessStandardError(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), standardErrorPath);
            return true;
        }

        private async Task<bool> TrySaveAndLogStandardOutput(SandboxedProcessResult result)
        {
            try
            {
                await result.StandardOutput.SaveAsync();
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(SandboxedProcessFile.StandardOutput), ex);
                return false;
            }

            string standardOutputPath = result.StandardOutput.FileName;
            BuildXL.Processes.Tracing.Logger.Log.PipProcessStandardOutput(m_loggingContext, m_pip.SemiStableHash, m_pip.GetDescription(m_context), standardOutputPath);
            return true;
        }

        private void LogChildrenSurvivedKilled()
        {
            BuildXL.Processes.Tracing.Logger.Log.PipProcessChildrenSurvivedKilled(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context));
        }

        private bool CheckExpectedOutputs()
        {
            bool allOutputsPresent = true;

            // The process exited cleanly (though it may have accessed unexpected files), so we should expect that all outputs are
            // present. We don't bother checking or logging this otherwise, since the output of a failed process is unusable anyway.
            // Only required output artifacts should be considered by this method.
            // Optional output files could be or could not be a part of the valid process output.
            using (var stringPool1 = Pools.GetStringList())
            {
                var expectedMissingOutputs = stringPool1.Instance;

                bool fileOutputsAreRedirected = m_pip.NeedsToRunInContainer && m_pip.ContainerIsolationLevel.IsolateOutputFiles();

                foreach (FileArtifactWithAttributes output in m_pip.FileOutputs)
                {
                    if (!output.MustExist())
                    {
                        continue;
                    }

                    var expectedOutput = output.ToFileArtifact();
                    string expectedOutputPath;

                    // If outputs were redirected, they are not in their expected location but it their redirected one
                    if (fileOutputsAreRedirected)
                    {
                        expectedOutputPath = m_processInContainerManager.GetRedirectedDeclaredOutputFile(expectedOutput.Path, m_containerConfiguration).ToString(m_pathTable);
                    }
                    else
                    {
                        expectedOutputPath = expectedOutput.Path.ToString(m_pathTable);
                    }

                    if (!FileExistsNoFollow(expectedOutputPath, fileOutputsAreRedirected) &&
                        expectedOutput != m_pip.StandardOutput &&
                        expectedOutput != m_pip.StandardError)
                    {
                        allOutputsPresent = false;
                        Tracing.Logger.Log.PipProcessMissingExpectedOutputOnCleanExit(
                            m_loggingContext,
                            pipSemiStableHash: m_pip.SemiStableHash,
                            pipDescription: m_pip.GetDescription(m_context),
                            pipSpecPath: m_pip.Provenance.Token.Path.ToString(m_context.PathTable),
                            pipWorkingDirectory: m_pip.WorkingDirectory.ToString(m_context.PathTable),
                            path: expectedOutputPath);
                        expectedMissingOutputs.Add(expectedOutputPath);
                    }
                }

                Func<string[], string> pathAggregator = (paths) =>
                    {
                        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                        return string.Join(Environment.NewLine, paths);
                    };

                if (expectedMissingOutputs.Count > 0)
                {
                    Tracing.Logger.Log.PipProcessExpectedMissingOutputs(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        pathAggregator(expectedMissingOutputs.ToArray()));
                }
            }

            return allOutputsPresent;
        }

        private bool FileExistsNoFollow(string path, bool fileOutputsAreRedirected)
        {
            var maybeResult = FileUtilities.TryProbePathExistence(path, followSymlink: false);
            var existsAsFile = maybeResult.Succeeded && maybeResult.Result == PathExistence.ExistsAsFile;

            // If file outputs are not redirected, this is simply file existence. Otherwise, we have
            // to check that the file is not a WCI tombstone, since this means the file is not really there.
            return existsAsFile && !(fileOutputsAreRedirected && FileUtilities.IsWciTombstoneFile(path));
        }

        // (lubol): TODO: Add handling of the translate paths strings. Add code here to address VSO Task# 989041.
        private TimeSpan GetEffectiveTimeout(TimeSpan? configuredTimeout, int defaultTimeoutMs, double multiplier)
        {
            if (m_pip.IsService)
            {
                // Service pips live for the duration of the build. Don't kill them. NOTE: This doesn't include service shutdown
                // pips which should have a timeout.
                return Process.MaxTimeout;
            }

            TimeSpan timeout = configuredTimeout ?? TimeSpan.FromMilliseconds(defaultTimeoutMs);
            try
            {
                timeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds * multiplier);
            }
            catch (OverflowException)
            {
                return Process.MaxTimeout;
            }

            return timeout > Process.MaxTimeout ? Process.MaxTimeout : timeout;
        }

        private static string ComputePipTimeoutDumpDirectory(ISandboxConfiguration sandboxConfig, Process pip, PathTable pathTable)
        {
            AbsolutePath rootDirectory = sandboxConfig.TimeoutDumpDirectory.IsValid ? sandboxConfig.TimeoutDumpDirectory : pip.UniqueOutputDirectory;
            return rootDirectory.IsValid ? rootDirectory.Combine(pathTable, pip.FormattedSemiStableHash).ToString(pathTable) : null;
        }

        private static void FormatOutputAndPaths(string standardOut, string standardError,
            string standardOutPath, string standardErrorPath,
            out string outputToLog, out string pathsToLog)
        {
            // Only display error/out if it is non-empty. This avoids adding duplicated newlines in the message.
            // Also use the emptiness as a hit for whether to show the path to the file on disk
            bool standardOutEmpty = string.IsNullOrWhiteSpace(standardOut);
            bool standardErrorEmpty = string.IsNullOrWhiteSpace(standardError);

            outputToLog = (standardOutEmpty ? string.Empty : standardOut) +
                (!standardOutEmpty && !standardErrorEmpty ? Environment.NewLine : string.Empty) +
                (standardErrorEmpty ? string.Empty : standardError);
            pathsToLog = (standardOutEmpty ? string.Empty : standardOutPath) +
                (!standardOutEmpty && !standardErrorEmpty ? Environment.NewLine : string.Empty) +
                (standardErrorEmpty ? string.Empty : standardErrorPath);
        }

        private class LogErrorResult
        {
            public LogErrorResult(bool success, bool errorWasTruncated)
            {
                Success = success;
                // ErrorWasTruncated should be forced to false when logging was not successful
                ErrorWasTruncated = success && errorWasTruncated;
            }

            /// <summary>
            /// Whether logging was successful
            /// </summary>
            public readonly bool Success;

            /// <summary>
            /// Whether the Error that was logged was truncated for any reason. This may be due to an error regex or
            /// due to the error being too long
            /// </summary>
            public readonly bool ErrorWasTruncated;
        }

        private async Task<LogErrorResult> TryLogErrorAsync(SandboxedProcessResult result, bool exitedWithSuccessExitCode)
        {
            bool errorWasTruncated = false;
            // Initializing error regex just before it is actually needed to save some cycles.
            if (!await TryInitializeErrorRegexAsync())
            {
                return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
            }

            var exceedsLimit = OutputExceedsLimit(result.StandardOutput) || OutputExceedsLimit(result.StandardError);
            if (!exceedsLimit || m_sandboxConfig.OutputReportingMode == OutputReportingMode.TruncatedOutputOnError)
            {
                string standardError = await TryFilterAsync(result.StandardError, IsError, appendNewLine: true);
                string standardOutput = await TryFilterAsync(result.StandardOutput, IsError, appendNewLine: true);

                if (standardError == null || standardOutput == null)
                {
                    return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
                }

                if (string.IsNullOrEmpty(standardError) && string.IsNullOrEmpty(standardOutput))
                {
                    // Standard error and standard output are empty.
                    // This could be because the filter is too aggressive and the entire output was filtered out.
                    // Rolling back to a non-filtered approach because some output is better than nothing.
                    standardError = await TryFilterAsync(result.StandardError, s => true, appendNewLine: true);
                    standardOutput = await TryFilterAsync(result.StandardOutput, s => true, appendNewLine: true);
                }

                if (standardError.Length != result.StandardError.Length ||
                    standardOutput.Length != result.StandardOutput.Length)
                {
                    errorWasTruncated = true;
                }

                HandleErrorsFromTool(standardError);
                HandleErrorsFromTool(standardOutput);

                LogPipProcessError(result, exitedWithSuccessExitCode, standardError, standardOutput);

                return new LogErrorResult(success: true, errorWasTruncated: errorWasTruncated);
            }

            long stdOutTotalLength = 0;
            long stdErrTotalLength = 0;

            // The output exceeds the limit and the full output has been requested. Emit it in chunks
            if (!await TryEmitFullOutputInChunks(IsError))
            {
                return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
            }

            if (stdOutTotalLength == 0 && stdErrTotalLength == 0)
            {
                // Standard error and standard output are empty.
                // This could be because the filter is too aggressive and the entire output was filtered out.
                // Rolling back to a non-filtered approach because some output is better than nothing.
                errorWasTruncated = false;
                if (!await TryEmitFullOutputInChunks(filterPredicate: null))
                {
                    return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
                }

                string stdOut = await TryFilterAsync(result.StandardOutput, s => true, appendNewLine: true);
                string stdErr = await TryFilterAsync(result.StandardError, s => true, appendNewLine: true);
                LogPipProcessError(result, exitedWithSuccessExitCode, stdErr, stdOut);

                stdOutTotalLength = stdOut.Length;
                stdErrTotalLength = stdErr.Length;
            }

            return new LogErrorResult(success: true, errorWasTruncated: errorWasTruncated);

            async Task<bool> TryEmitFullOutputInChunks(Predicate<string> filterPredicate)
            {
                using (TextReader errorReader = CreateReader(result.StandardError))
                {
                    using (TextReader outReader = CreateReader(result.StandardOutput))
                    {
                        if (errorReader == null || outReader == null)
                        {
                            return false;
                        }

                        while (errorReader.Peek() != -1 || outReader.Peek() != -1)
                        {
                            string stdError = await ReadNextChunk(errorReader, result.StandardError, filterPredicate);
                            string stdOut = await ReadNextChunk(outReader, result.StandardOutput, filterPredicate);

                            if (stdError == null || stdOut == null)
                            {
                                return false;
                            }

                            if (string.IsNullOrEmpty(stdOut) && string.IsNullOrEmpty(stdError))
                            {
                                continue;
                            }

                            stdOutTotalLength += stdOut.Length;
                            stdErrTotalLength += stdError.Length;

                            HandleErrorsFromTool(stdError);
                            HandleErrorsFromTool(stdOut);

                            LogPipProcessError(result, exitedWithSuccessExitCode, stdError, stdOut);
                        }

                        if (stdOutTotalLength != result.StandardOutput.Length || stdErrTotalLength != result.StandardError.Length)
                        {
                            errorWasTruncated = true;
                        }

                        return true;

                    }
                }
            }
        }

        private void LogPipProcessError(SandboxedProcessResult result, bool exitedWithSuccessExitCode, string stdError, string stdOut)
        {
            string outputTolog;
            string outputPathsToLog;
            FormatOutputAndPaths(
                stdOut,
                stdError,
                result.StandardOutput.FileName,
                result.StandardError.FileName,
                out outputTolog,
                out outputPathsToLog);

            BuildXL.Processes.Tracing.Logger.Log.PipProcessError(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                m_pip.Provenance.Token.Path.ToString(m_pathTable),
                m_workingDirectory,
                GetToolName(),
                EnsureToolOutputIsNotEmpty(outputTolog),
                AddTrailingNewLineIfNeeded(outputPathsToLog),
                result.ExitCode,
                // if the process finished successfully (exit code 0) and we entered this method --> some outputs are missing
                exitedWithSuccessExitCode ? BuildXL.Utilities.Tracing.Events.PipProcessErrorMissingOutputsSuffix : string.Empty);
        }

        private void HandleErrorsFromTool(string error)
        {
            if (error == null)
            {
                return;
            }

            var process = GetProcessKind(AbsolutePath.Invalid);

            if (process == SpecialProcessKind.Csc)
            {
                // BUG 1124595
                const string Pattern =
                    @"'(?<ThePath>(?:[a-zA-Z]\:|\\\\[\w\.]+\\[\w.$]+)\\(?:[\w]+\\)*\w([\w.])+)' -- The process cannot access the file because it is being used by another process";
                foreach (Match match in Regex.Matches(error, Pattern, RegexOptions.IgnoreCase))
                {
                    var path = match.Groups["ThePath"].Value;
                    string diagnosticInfo;
                    if (!FileUtilities.TryFindOpenHandlesToFile(path, out diagnosticInfo))
                    {
                        diagnosticInfo = nameof(FileUtilities.TryFindOpenHandlesToFile) + " failed";
                    }

                    Tracing.Logger.Log.PipProcessToolErrorDueToHandleToFileBeingUsed(
                            m_loggingContext,
                            m_pip.SemiStableHash,
                            m_pip.GetDescription(m_context),
                            m_pip.Provenance.Token.Path.ToString(m_pathTable),
                            m_workingDirectory,
                            process.ToString(),
                            path,
                            diagnosticInfo);
                }
            }
        }

        private static bool OutputExceedsLimit(SandboxedProcessOutput output)
        {
            return output.Length > MaxConsoleLength;
        }

        private async Task<bool> TryLogOutputAsync(SandboxedProcessResult result)
        {
            using (TextReader errorReader = CreateReader(result.StandardError))
            {
                using (TextReader outReader = CreateReader(result.StandardOutput))
                {
                    if (errorReader == null || outReader == null)
                    {
                        return false;
                    }

                    while (errorReader.Peek() != -1 || outReader.Peek() != -1)
                    {
                        string stdError = await ReadNextChunk(errorReader, result.StandardError);
                        string stdOut = await ReadNextChunk(outReader, result.StandardOutput);

                        if (stdError == null || stdOut == null)
                        {
                            return false;
                        }

                        // Sometimes stdOut/StdErr contains NUL characters (ASCII code 0). While this does not
                        // create a problem for text editors (they will either display the NUL char or show it
                        // as whitespace), NUL char messes up with ETW logging BuildXL uses. When a string is
                        // passed into ETW, it is treated as null-terminated string => everything after the
                        // the first NUL char is dropped. This causes Bug 1310020.
                        //
                        // Remove all NUL chars before logging the output.
                        if (stdOut.Contains("\0"))
                        {
                            stdOut = stdOut.Replace("\0", string.Empty);
                        }

                        if (stdError.Contains("\0"))
                        {
                            stdError = stdError.Replace("\0", string.Empty);
                        }

                        bool stdOutEmpty = string.IsNullOrWhiteSpace(stdOut);
                        bool stdErrorEmpty = string.IsNullOrWhiteSpace(stdError);

                        string outputToLog = (stdOutEmpty ? string.Empty : stdOut) +
                            (!stdOutEmpty && !stdErrorEmpty ? Environment.NewLine : string.Empty) +
                            (stdErrorEmpty ? string.Empty : stdError);

                        BuildXL.Processes.Tracing.Logger.Log.PipProcessOutput(
                            m_loggingContext,
                            m_pip.SemiStableHash,
                            m_pip.GetDescription(m_context),
                            m_pip.Provenance.Token.Path.ToString(m_pathTable),
                            m_workingDirectory,
                            AddTrailingNewLineIfNeeded(outputToLog));
                    }

                    return true;
                }
            }
        }

        private TextReader CreateReader(SandboxedProcessOutput output)
        {
            try
            {
                return output.CreateReader();
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return null;
            }
        }

        /// <summary>
        /// Reads chunk of output from reader, with optional filtering:
        /// the result will only contain lines, that satisfy provided predicate (if it is non-null).
        /// </summary>
        private async Task<string> ReadNextChunk(TextReader reader, SandboxedProcessOutput output, Predicate<string> filterPredicate = null)
        {
            try
            {
                using (var wrapper = Pools.StringBuilderPool.GetInstance())
                {
                    StringBuilder sb = wrapper.Instance;
                    for (int i = 0; i < OutputChunkInLines; )
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null)
                        {
                            if (sb.Length == 0)
                            {
                                return string.Empty;
                            }

                            break;
                        }

                        if (filterPredicate == null || filterPredicate(line))
                        {
                            sb.AppendLine(line);
                            ++i;
                        }
                    }

                    return sb.ToString();
                }
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return null;
            }
        }

        /// <summary>
        /// Tries to filter the standard error and standard output streams for warnings.
        /// </summary>
        public async Task<bool> TryLogWarningAsync(SandboxedProcessOutput standardError, SandboxedProcessOutput standardOutput)
        {
            string warningsError = standardError == null ? string.Empty : await TryFilterAsync(standardError, IsWarning, appendNewLine: true);
            string warningsOutput = standardOutput == null ? string.Empty : await TryFilterAsync(standardOutput, IsWarning, appendNewLine: true);

            if (warningsError == null ||
                warningsOutput == null)
            {
                return false;
            }

            FormatOutputAndPaths(
                warningsOutput,
                warningsError,
                standardOutput.FileName,
                standardError.FileName,
                out string outputTolog,
                out string outputPathsToLog);

            BuildXL.Processes.Tracing.Logger.Log.PipProcessWarning(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                m_pip.Provenance.Token.Path.ToString(m_pathTable),
                m_workingDirectory,
                GetToolName(),
                EnsureToolOutputIsNotEmpty(outputTolog),
                AddTrailingNewLineIfNeeded(outputPathsToLog));
            return true;
        }

        private static string EnsureToolOutputIsNotEmpty(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "Tool failed without writing any output stream";
            }

            return output;
        }

        private static string AddTrailingNewLineIfNeeded(string message)
        {
            // If empty/whitespace/newline, return empty.
            // If text, return text+newLine.
            // If text+newLine, return text+newLine.
            // if text+newLine+newLine, return text+newLine.

            return string.IsNullOrWhiteSpace(message) ? string.Empty : message.TrimEnd(Environment.NewLine.ToCharArray()) + Environment.NewLine;
        }

        private string GetToolName()
        {
            return m_pip.GetToolName(m_pathTable).ToString(m_pathTable.StringTable);
        }

        // Returns the number of surviving processes that resulted in errors.
        // The caller should avoid throwing errors when this is zero.
        private int ReportSurvivingChildProcesses(SandboxedProcessResult result)
        {
            Contract.Assume(result.Killed);

            var unexpectedSurvivingChildProcesses = result
                .SurvivingChildProcesses
                .Where(pr =>
                    !HasProcessName(pr, "ProcessTreeContextCreator.exe") &&
                    !m_pip.AllowedSurvivingChildProcessNames.Any(procName => HasProcessName(pr, procName.ToString(m_context.StringTable))));

            int numErrors = unexpectedSurvivingChildProcesses.Count();

            if (numErrors == 0)
            {
                int numSurvive = result.SurvivingChildProcesses.Count();

                if (numSurvive > JobObject.InitialProcessIdListLength)
                {
                    // Report for too many surviving child processes.
                    Tracing.Logger.Log.PipProcessChildrenSurvivedTooMany(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        numSurvive,
                        Environment.NewLine + string.Join(Environment.NewLine, result.SurvivingChildProcesses.Select(p => p.Path)));
                }
            }
            else
            {
                Tracing.Logger.Log.PipProcessChildrenSurvivedError(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        numErrors,
                        Environment.NewLine + string.Join(Environment.NewLine, unexpectedSurvivingChildProcesses.Select(p => p.Path)));
            }

            return numErrors;

            bool HasProcessName(ReportedProcess pr, string name)
            {
                return string.Equals(Path.GetFileName(pr.Path), name, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void LogFileAccessTables(Process pip)
        {
            // TODO: This dumps a very low-level table; consider producing a nicer representation
            // Instead of logging each line, consider storing manifest and logging file name
            foreach (string line in m_fileAccessManifest.Describe())
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessFileAccessTableEntry(
                    m_loggingContext,
                    pip.SemiStableHash,
                    pip.GetDescription(m_context),
                    line);
            }
        }

        private static long Bound(double value)
        {
            return value >= long.MaxValue ? long.MaxValue : (long)value;
        }

        private Stream TryOpenStandardInputStream(out bool success)
        {
            success = true;
            if (!m_pip.StandardInput.IsValid)
            {
                return null;
            }

            return m_pip.StandardInput.IsFile
                ? TryOpenStandardInputStreamFromFile(m_pip.StandardInput.File, out success)
                : TryOpenStandardInputStreamFromData(m_pip.StandardInput.Data, out success);
        }

        private Stream TryOpenStandardInputStreamFromFile(FileArtifact file, out bool success)
        {
            Contract.Requires(file.IsValid);

            success = true;
            string standardInputFileName = file.Path.ToString(m_pathTable);

            try
            {
                return FileUtilities.CreateAsyncFileStream(
                    standardInputFileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(standardInputFileName, ex);
                success = false;
                return null;
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposed by its caller")]
        private Stream TryOpenStandardInputStreamFromData(in PipData data, out bool success)
        {
            Contract.Requires(data.IsValid);

            success = true;

            // TODO: Include encoding in the pip data. Task 869176
            return new MemoryStream(CharUtilities.Utf8NoBomNoThrow.GetBytes(data.ToString(m_context.PathTable)));
        }

        private void PreparePathForOutputFile(AbsolutePath filePath, HashSet<AbsolutePath> outputDirectories = null)
        {
            Contract.Requires(filePath.IsValid);

            string expandedFilePath = filePath.ToString(m_pathTable);
            var mayBeDeleted = FileUtilities.TryDeletePathIfExists(expandedFilePath, m_tempDirectoryCleaner);

            if (!mayBeDeleted.Succeeded)
            {
                mayBeDeleted.Failure.Throw();
            }

            AbsolutePath parentDirectory = filePath.GetParent(m_pathTable);

            if (outputDirectories == null || outputDirectories.Add(parentDirectory))
            {
                // Ensure parent directory exists.
                FileUtilities.CreateDirectory(parentDirectory.ToString(m_pathTable));
            }
        }

        private void PreparePathForOutputDirectory(string directoryPathStr, bool createIfNonExistent = false)
        {
            PreparePathForDirectory(directoryPathStr, createIfNonExistent);
        }

        private void PreparePathForDirectory(string expandedDirectoryPath, bool createIfNonExistent)
        {
            bool exists = false;

            if (FileUtilities.DirectoryExistsNoFollow(expandedDirectoryPath))
            {
                FileUtilities.DeleteDirectoryContents(expandedDirectoryPath, deleteRootDirectory: false, tempDirectoryCleaner: m_tempDirectoryCleaner);
                exists = true;
            }
            else if (FileUtilities.FileExistsNoFollow(expandedDirectoryPath))
            {
                // We expect to produce a directory, but a file with the same name exists on disk.
                FileUtilities.DeleteFile(expandedDirectoryPath, tempDirectoryCleaner: m_tempDirectoryCleaner);
            }

            if (!exists && createIfNonExistent)
            {
                FileUtilities.CreateDirectory(expandedDirectoryPath);
            }
        }

        /// <summary>
        /// Whether we should preserve the given declared static file or directory output.
        /// </summary>
        private bool ShouldPreserveDeclaredOutput(AbsolutePath path, HashSet<AbsolutePath> whitelist)
        {
            if (!m_shouldPreserveOutputs)
            {
                // If the pip does not allow preserve outputs, return false
                return false;
            }

            if (whitelist.Count == 0)
            {
                // If the whitelist is empty, every output is preserved
                return true;
            }

            if (whitelist.Contains(path))
            {
                // Only preserve the file or directories that are given in the whitelist.
                return true;
            }

            return false;
        }
    }
}
