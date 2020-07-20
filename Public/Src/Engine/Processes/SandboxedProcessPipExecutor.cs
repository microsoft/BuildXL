// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Pips;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Containers;
using BuildXL.Processes.Internal;
using BuildXL.Processes.Sideband;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.VmCommandProxy;
using static BuildXL.Processes.SandboxedProcessFactory;
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

        /// <summary>
        /// Azure Watson's dead exit code.
        /// </summary>
        /// <remarks>
        /// When running in CloudBuild, Process nondeterministically sometimes exits with 0xDEAD exit code. This is the exit code
        /// returned by Azure Watson dump after catching the process crash.
        /// </remarks>
        private const uint AzureWatsonExitCode = 0xDEAD;

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

        /// <summary>
        /// Indicate whether a intentional retry attempt is initiated for the sake of integration testing.
        /// </summary>
        private static bool s_testRetryOccurred;

        private readonly PipExecutionContext m_context;
        private readonly PathTable m_pathTable;

        private readonly ISandboxConfiguration m_sandboxConfig;

        private readonly IReadOnlyDictionary<string, string> m_rootMappings;

        private readonly FileAccessManifest m_fileAccessManifest;

        private readonly bool m_disableConHostSharing;

        private readonly Action<int> m_processIdListener;

        private readonly PipFragmentRenderer m_pipDataRenderer;

        private readonly FileAccessAllowlist m_fileAccessAllowlist;

        private readonly Process m_pip;

        private readonly string m_pipDescription;

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

        private readonly bool m_isLazySharedOpaqueOutputDeletionEnabled;

        private readonly ITempCleaner m_tempDirectoryCleaner;

        private readonly ReadOnlyHashSet<AbsolutePath> m_sharedOpaqueDirectoryRoots;

        private readonly ProcessInContainerManager m_processInContainerManager;
        private readonly ContainerConfiguration m_containerConfiguration;

        private readonly VmInitializer m_vmInitializer;

        private readonly Dictionary<AbsolutePath, AbsolutePath> m_tempFolderRedirectionForVm = new Dictionary<AbsolutePath, AbsolutePath>();

        /// <summary>
        /// The active sandboxed process (if any)
        /// </summary>
        private ISandboxedProcess m_activeProcess;

        /// <summary>
        /// Fragments of incremental tools.
        /// </summary>
        private readonly IReadOnlyList<string> m_incrementalToolFragments;

        /// <summary>
        /// Inputs affected by file/source changes.
        /// </summary>
        private readonly IReadOnlyList<AbsolutePath> m_changeAffectedInputs;
        
        private readonly IDetoursEventListener m_detoursListener;
        private readonly SymlinkedAccessResolver m_symlinkedAccessResolver;

        /// <summary>
        /// Whether the process invokes an incremental tool with preserveOutputs mode.
        /// </summary>
        private bool IsIncrementalPreserveOutputPip => m_shouldPreserveOutputs && m_pip.IncrementalTool;

        private readonly ObjectCache<string, bool> m_incrementalToolMatchCache = new ObjectCache<string, bool>(37);

        /// <summary>
        /// Whether apply fake timestamp or not.
        /// </summary>
        /// <remarks>
        /// We do not apply fake timestamps for process pips that invoke an incremental tool with preserveOutputs mode.
        /// </remarks>
        private bool NoFakeTimestamp => IsIncrementalPreserveOutputPip;

        private FileAccessPolicy DefaultMask => NoFakeTimestamp ? ~FileAccessPolicy.Deny : ~FileAccessPolicy.AllowRealInputTimestamps;

        private readonly IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> m_staleOutputsUnderSharedOpaqueDirectories;

        /// <summary>
        /// Name of the diretory in Log directory for std output files
        /// </summary>
        public static readonly string StdOutputsDirNameInLog = "StdOutputs";

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
            FileAccessAllowlist allowlist,
            Func<FileArtifact, Task<bool>> makeInputPrivate,
            Func<string, Task<bool>> makeOutputPrivate,
            SemanticPathExpander semanticPathExpander,
            bool disableConHostSharing,
            PipEnvironment pipEnvironment,
            bool validateDistribution,
            IDirectoryArtifactContext directoryArtifactContext,
            ITempCleaner tempDirectoryCleaner,
            bool isLazySharedOpaqueOutputDeletionEnabled,
            ISandboxedProcessLogger logger = null,
            Action<int> processIdListener = null,
            PipFragmentRenderer pipDataRenderer = null,
            AbsolutePath buildEngineDirectory = default,
            DirectoryTranslator directoryTranslator = null,
            int remainingUserRetryCount = 0,
            bool isQbuildIntegrated = false,
            VmInitializer vmInitializer = null,
            SubstituteProcessExecutionInfo shimInfo = null,
            IReadOnlyList<RelativePath> incrementalTools = null,
            IReadOnlyList<AbsolutePath> changeAffectedInputs = null,
            IDetoursEventListener detoursListener = null,
            SymlinkedAccessResolver symlinkedAccessResolver = null,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> staleOutputsUnderSharedOpaqueDirectories = null)
        {
            Contract.Requires(pip != null);
            Contract.Requires(context != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(sandBoxConfig != null);
            Contract.Requires(rootMappings != null);
            Contract.Requires(pipEnvironment != null);
            Contract.Requires(directoryArtifactContext != null);
            Contract.Requires(processInContainerManager != null);
            // The tempDirectoryCleaner must not be null since it is relied upon for robust file deletion
            Contract.Requires(tempDirectoryCleaner != null);

            m_context = context;
            m_loggingContext = loggingContext;
            m_pathTable = context.PathTable;
            m_pip = pip;
            m_pipDescription = m_pip.GetDescription(m_context);
            m_sandboxConfig = sandBoxConfig;
            m_rootMappings = rootMappings;
            m_workingDirectory = pip.WorkingDirectory.ToString(m_pathTable);
            m_fileAccessManifest =
                new FileAccessManifest(
                    m_pathTable, 
                    directoryTranslator, 
                    m_pip.ChildProcessesToBreakawayFromSandbox.Select(process => process.ToString(context.StringTable)).ToReadOnlyArray())
                {
                    MonitorNtCreateFile = sandBoxConfig.UnsafeSandboxConfiguration.MonitorNtCreateFile,
                    MonitorZwCreateOpenQueryFile = sandBoxConfig.UnsafeSandboxConfiguration.MonitorZwCreateOpenQueryFile,
                    ForceReadOnlyForRequestedReadWrite = sandBoxConfig.ForceReadOnlyForRequestedReadWrite,
                    IgnoreReparsePoints = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreReparsePoints,
                    IgnoreFullSymlinkResolving = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreFullSymlinkResolving,
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
                    IgnoreCreateProcessReport = sandBoxConfig.UnsafeSandboxConfiguration.IgnoreCreateProcessReport,
                    ProbeDirectorySymlinkAsDirectory = sandBoxConfig.UnsafeSandboxConfiguration.ProbeDirectorySymlinkAsDirectory,
                    SubstituteProcessExecutionInfo = shimInfo,
                };

            if (!sandBoxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses)
            {
                // If monitoring of file accesses is disabled, make sure a valid
                // manifest is still provided and disable detours for this pip.
                m_fileAccessManifest.DisableDetours = true;
            }

            m_fileAccessAllowlist = allowlist;
            m_makeInputPrivate = makeInputPrivate;
            m_makeOutputPrivate = makeOutputPrivate;
            m_semanticPathExpander = semanticPathExpander;
            m_logger = logger ?? new SandboxedProcessLogger(m_loggingContext, pip, context);
            m_disableConHostSharing = disableConHostSharing;
            m_shouldPreserveOutputs = m_pip.AllowPreserveOutputs && m_sandboxConfig.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled
                                      && m_sandboxConfig.UnsafeSandboxConfiguration.PreserveOutputsTrustLevel <= m_pip.PreserveOutputsTrustLevel;
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
            m_isLazySharedOpaqueOutputDeletionEnabled = isLazySharedOpaqueOutputDeletionEnabled;

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

            if (incrementalTools != null)
            {
                m_incrementalToolFragments = new List<string>(
                    incrementalTools.Select(toolSuffix =>
                        // Append leading separator to ensure suffix only matches valid relative path fragments
                        Path.DirectorySeparatorChar + toolSuffix.ToString(context.StringTable)));
            }
            else
            {
                m_incrementalToolFragments = new List<string>(Enumerable.Empty<string>());
            }

            m_changeAffectedInputs = changeAffectedInputs;
            m_detoursListener = detoursListener;
            m_symlinkedAccessResolver = symlinkedAccessResolver;
            m_staleOutputsUnderSharedOpaqueDirectories = staleOutputsUnderSharedOpaqueDirectories;
        }

        /// <inheritdoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            FileArtifact fileArtifact = file.PipFileArtifact(m_pip);

            string filename;
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
        /// <see cref="SandboxedProcess.GetMemoryCountersSnapshot"/>
        /// </summary>
        public ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot()
        {
            return m_activeProcess?.GetMemoryCountersSnapshot();
        }

        /// <summary>
        /// <see cref="SandboxedProcess.TryEmptyWorkingSet"/>
        /// </summary>
        public EmptyWorkingSetResult TryEmptyWorkingSet(bool isSuspend)
        {
            var result = m_activeProcess?.TryEmptyWorkingSet(isSuspend) ?? EmptyWorkingSetResult.None;

            if (result.HasFlag(EmptyWorkingSetResult.EmptyWorkingSetFailed) &&
                result.HasFlag(EmptyWorkingSetResult.SetMaxWorkingSetFailed) &&
                result.HasFlag(EmptyWorkingSetResult.SuspendFailed))
            {
                var errorCode = Marshal.GetLastWin32Error();
                Tracing.Logger.Log.ResumeOrSuspendProcessError(
                    m_loggingContext,
                    m_pip.FormattedSemiStableHash,
                    result.ToString(),
                    errorCode);
            }

            return result;
        }

        /// <summary>
        /// <see cref="SandboxedProcess.TryResumeProcess"/>
        /// </summary>
        public bool TryResumeProcess()
        {
            var result = m_activeProcess?.TryResumeProcess();

            if (result == false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                Tracing.Logger.Log.ResumeOrSuspendProcessError(
                    m_loggingContext,
                    m_pip.FormattedSemiStableHash,
                    "ResumeProcess",
                    errorCode);
            }

            return result ?? false;
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

        private string GetDetoursInternalErrorFilePath()
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
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pipDescription,
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
        private Task<FilterResult> TryFilterLineByLineAsync(SandboxedProcessOutput output, Predicate<string> filterPredicate, bool appendNewLine = false)
            => TryFilterAsync(output, new OutputFilter(filterPredicate), appendNewLine);

        /// <summary>
        /// Result of filtering the <see cref="SandboxedProcessOutput"/>.
        /// </summary>
        private class FilterResult
        {
            /// <summary>
            /// The output. May or may not be filtered. Empty string means empty output. Null means there was an error processing the output
            /// </summary>
            public string FilteredOutput;

            /// <summary>
            /// Whether the result was filtered
            /// </summary>
            public bool IsFiltered;

            /// <summary>
            /// Whether there was an error processing the output
            /// </summary>
            public bool HasError => FilteredOutput == null;

            /// <summary>
            /// FilterResult to use when there is a filtering error
            /// </summary>
            public static FilterResult ResultForError = new FilterResult() { FilteredOutput = null, IsFiltered = false };
        }

        private async Task<FilterResult> TryFilterAsync(SandboxedProcessOutput output, OutputFilter filter, bool appendNewLine)
        {
            Contract.Assert(filter.LinePredicate != null || filter.Regex != null);
            FilterResult filterResult = new FilterResult();
            filterResult.IsFiltered = false;
            bool isLineByLine = filter.LinePredicate != null;
            try
            {
                using (PooledObjectWrapper<StringBuilder> wrapper = Pools.StringBuilderPool.GetInstance())
                {
                    StringBuilder sb = wrapper.Instance;

                    using (TextReader reader = output.CreateReader())
                    {
                        while (reader.Peek() != -1)
                        {
                            string inputChunk = isLineByLine
                                ? await reader.ReadLineAsync()
                                : await ReadNextChunkAsync(reader, output);

                            if (inputChunk == null)
                            {
                                break;
                            }

                            string outputText = filter.ExtractMatches(inputChunk);

                            if (inputChunk.Replace(Environment.NewLine, string.Empty).Trim().Length > outputText.Replace(Environment.NewLine, string.Empty).Trim().Length)
                            {
                                filterResult.IsFiltered = true;
                            }

                            if (!string.IsNullOrEmpty(outputText))
                            {
                                // only add leading newlines (when needed).
                                // Trailing newlines would cause the message logged to have double newlines
                                if (appendNewLine && sb.Length > 0)
                                {
                                    sb.AppendLine();
                                }

                                sb.Append(outputText);
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
                    }
                    filterResult.FilteredOutput = sb.ToString();
                    return filterResult;
                }
            }
            catch (IOException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return FilterResult.ResultForError;
            }
            catch (AggregateException ex)
            {
                if (TryLogRootIOException(GetFileName(output.File), ex))
                {
                    return FilterResult.ResultForError;
                }

                throw;
            }
            catch (BuildXLException ex)
            {
                PipStandardIOFailed(GetFileName(output.File), ex);
                return FilterResult.ResultForError;
            }
        }

        /// <summary>
        /// Runs the process pip (uncached).
        /// </summary>
        public async Task<SandboxedProcessPipExecutionResult> RunAsync(
            CancellationToken cancellationToken = default, 
            ISandboxConnection sandboxConnection = null,
            SidebandWriter sidebandWriter = null)
        {
            if (!s_testRetryOccurred)
            {
                // For the integration test, we simulate a retryable failure here via ProcessStartFailure.
                if (m_pip.Priority == Process.IntegrationTestPriority &&
                    m_pip.Tags.Any(a => a.ToString(m_context.StringTable) == TagFilter.TriggerWorkerProcessStartFailed))
                {
                    s_testRetryOccurred = true;
                    return SandboxedProcessPipExecutionResult.RetryableFailure(CancellationReason.ProcessStartFailure);
                }
            }

            try
            {
                var sandboxPrepTime = System.Diagnostics.Stopwatch.StartNew();

                

                if (!PrepareWorkingDirectory())
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                var environmentVariables = PrepareEnvironmentVariables();

                if (!PrepareTempDirectory(ref environmentVariables))
                {
                    return SandboxedProcessPipExecutionResult.RetryableFailure(CancellationReason.TempDirectoryCleanupFailure);
                }

                if (!await PrepareResponseFileAsync())
                {
                    return SandboxedProcessPipExecutionResult.PreparationFailure();
                }

                if (!await PrepareChangeAffectedInputListFileAsync(m_changeAffectedInputs))
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
                    // doesn't need to recompute this and it can distinguish between accesses that only pertain to outputs vs inputs
                    // in the scope of a shared opaque
                    HashSet<AbsolutePath> allInputPathsUnderSharedOpaques = allInputPathsUnderSharedOpaquesWrapper.Instance;

                    if (m_sandboxConfig.UnsafeSandboxConfiguration.MonitorFileAccesses && !TryPrepareFileAccessMonitoring(allInputPathsUnderSharedOpaques))
                    {
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    if (!await PrepareOutputsAsync())
                    {
                        return SandboxedProcessPipExecutionResult.PreparationFailure();
                    }

                    string executable = m_pip.Executable.Path.ToString(m_pathTable);
                    string arguments = m_pip.Arguments.ToString(m_pipDataRenderer);
                    m_timeout = GetEffectiveTimeout(m_pip.Timeout, m_sandboxConfig.DefaultTimeout, m_sandboxConfig.TimeoutMultiplier);

                    var info = new SandboxedProcessInfo(
                        m_pathTable,
                        this,
                        executable,
                        m_fileAccessManifest,
                        m_disableConHostSharing,
                        m_containerConfiguration,
                        m_loggingContext,
                        m_pip.TestRetries,
                        sandboxConnection: sandboxConnection,
                        sidebandWriter: sidebandWriter,
                        detoursEventListener: m_detoursListener)
                    {
                        Arguments = arguments,
                        WorkingDirectory = m_workingDirectory,
                        RootMappings = m_rootMappings,
                        EnvironmentVariables = environmentVariables,
                        Timeout = m_timeout,
                        PipSemiStableHash = m_pip.SemiStableHash,
                        PipDescription = m_pipDescription,
                        TimeoutDumpDirectory = ComputePipTimeoutDumpDirectory(m_sandboxConfig, m_pip, m_pathTable),
                        SandboxKind = m_sandboxConfig.UnsafeSandboxConfiguration.SandboxKind,
                        AllowedSurvivingChildProcessNames = m_pip.AllowedSurvivingChildProcessNames.Select(n => n.ToString(m_pathTable.StringTable)).ToArray(),
                        NestedProcessTerminationTimeout = m_pip.NestedProcessTerminationTimeout ?? SandboxedProcessInfo.DefaultNestedProcessTerminationTimeout,
                        DetoursFailureFile = m_detoursFailuresFile
                    };

                    var result = ShouldSandboxedProcessExecuteExternal
                        ? await RunExternalAsync(info, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken)
                        : await RunInternalAsync(info, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
                    if (result.Status == SandboxedProcessPipExecutionStatus.PreparationFailed)
                    {
                        m_processIdListener?.Invoke(0);
                    }

                    // If sideband writer is used and we are executing internally, make sure here that it is flushed to disk.
                    // Without doing this explicitly, if no writes into its SODs were recorded for the pip,
                    // the sideband file will not be automatically saved to disk.  When running externally, the external 
                    // executor process will do this and if we do it here again we'll end up overwriting the sideband file.
                    if (!ShouldSandboxedProcessExecuteExternal)
                    {
                        info.SidebandWriter?.EnsureHeaderWritten();
                    }

                    return result;
                }
            }
            finally
            {
                Contract.Assert(m_fileAccessManifest != null);

                m_fileAccessManifest.UnsetMessageCountSemaphore();
            }
        }

        private bool SandboxedProcessNeedsExecuteExternal =>
            // Execution mode is external
            m_sandboxConfig.AdminRequiredProcessExecutionMode.ExecuteExternal()
            // Only pip that requires admin privilege.
            && m_pip.RequiresAdmin;

        private bool ShouldSandboxedProcessExecuteExternal =>
            SandboxedProcessNeedsExecuteExternal
            // Container is disabled.
            && !m_containerConfiguration.IsIsolationEnabled;

        private bool ShouldSandboxedProcessExecuteInVm =>
            ShouldSandboxedProcessExecuteExternal
            && m_sandboxConfig.AdminRequiredProcessExecutionMode.ExecuteExternalVm()
            // Windows only.
            && !OperatingSystemHelper.IsUnixOS;

        private async Task<SandboxedProcessPipExecutionResult> RunInternalAsync(
            SandboxedProcessInfo info,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            System.Diagnostics.Stopwatch sandboxPrepTime,
            CancellationToken cancellationToken = default)
        {
            if (SandboxedProcessNeedsExecuteExternal)
            {
                Tracing.Logger.Log.PipProcessNeedsExecuteExternalButExecuteInternal(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    m_pip.RequiresAdmin,
                    m_sandboxConfig.AdminRequiredProcessExecutionMode.ToString(),
                    !OperatingSystemHelper.IsUnixOS,
                    m_containerConfiguration.IsIsolationEnabled,
                    m_processIdListener != null);
            }

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
                                    Tracing.Logger.Log.PipSpecifiedToRunInContainerButIsolationIsNotSupported(m_loggingContext, m_pip.SemiStableHash, m_pipDescription);
                                    return SandboxedProcessPipExecutionResult.PreparationFailure(processLaunchRetryCount, (int)SharedLogEventId.PipSpecifiedToRunInContainerButIsolationIsNotSupported, maxDetoursHeapSize: maxDetoursHeapSize);
                                }

                                Tracing.Logger.Log.PipInContainerStarting(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, m_containerConfiguration.ToDisplayString());
                            }

                            process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: false);

                            // If the process started in a container, the setup of it is ready at this point, so we (verbose) log it
                            if (m_containerConfiguration.IsIsolationEnabled)
                            {
                                Tracing.Logger.Log.PipInContainerStarted(m_loggingContext, m_pip.SemiStableHash, m_pipDescription);
                            }
                        }
                        catch (BuildXLException ex)
                        {
                            if (ex.LogEventErrorCode == NativeIOConstants.ErrorFileNotFound)
                            {
                                LocationData location = m_pip.Provenance.Token;
                                string specFile = location.Path.ToString(m_pathTable);

                                Tracing.Logger.Log.PipProcessFileNotFound(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, 2, info.FileName, specFile, location.Position);
                            }
                            else if (ex.LogEventErrorCode == NativeIOConstants.ErrorPartialCopy && (processLaunchRetryCount < ProcessLaunchRetryCountMax))
                            {
                                processLaunchRetryCount++;
                                shouldRelaunchProcess = true;
                                Tracing.Logger.Log.RetryStartPipDueToErrorPartialCopyDuringDetours(
                                    m_loggingContext,
                                    m_pip.SemiStableHash,
                                    m_pipDescription,
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
                                    m_pipDescription,
                                    ex.LogEventErrorCode,
                                    ex.LogEventMessage);
                            }

                            return SandboxedProcessPipExecutionResult.RetryableFailure(CancellationReason.ProcessStartFailure, processLaunchRetryCount, maxDetoursHeapSize);
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

            info.RedirectedTempFolders = m_tempFolderRedirectionForVm.Select(kvp => (kvp.Key.ToString(m_pathTable), kvp.Value.ToString(m_pathTable))).ToArray();

            if (m_sandboxConfig.AdminRequiredProcessExecutionMode.ExecuteExternalVm())
            {
                TranslateHostSharedUncDrive(info);
            }

            ISandboxedProcess process = null;

            try
            {
                var externalSandboxedProcessExecutor = new ExternalToolSandboxedProcessExecutor(Path.Combine(
                    m_layoutConfiguration.BuildEngineDirectory.ToString(m_context.PathTable),
                    ExternalToolSandboxedProcessExecutor.DefaultToolRelativePath));

                foreach (var scope in externalSandboxedProcessExecutor.UntrackedScopes)
                {
                    AddUntrackedScopeToManifest(AbsolutePath.Create(m_pathTable, scope), info.FileAccessManifest);
                }

                // Preparation should be finished.
                sandboxPrepTime.Stop();

                string externalSandboxedProcessDirectory = m_layoutConfiguration.ExternalSandboxedProcessDirectory.ToString(m_pathTable);

                if (m_sandboxConfig.AdminRequiredProcessExecutionMode == AdminRequiredProcessExecutionMode.ExternalTool)
                {
                    Tracing.Logger.Log.PipProcessStartExternalTool(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, externalSandboxedProcessExecutor.ExecutablePath);

                    process = await ExternalSandboxedProcess.StartAsync(
                        info,
                        spi => new ExternalToolSandboxedProcess(spi, externalSandboxedProcessExecutor, externalSandboxedProcessDirectory));
                }
                else
                {
                    Contract.Assert(ShouldSandboxedProcessExecuteInVm);

                    // Initialize VM once.
                    await m_vmInitializer.LazyInitVmAsync.Value;

                    Tracing.Logger.Log.PipProcessStartExternalVm(m_loggingContext, m_pip.SemiStableHash, m_pipDescription);

                    process = await ExternalSandboxedProcess.StartAsync(
                        info,
                        spi => new ExternalVmSandboxedProcess(spi, m_vmInitializer, externalSandboxedProcessExecutor, externalSandboxedProcessDirectory));
                }
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipProcessStartFailed(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    ex.LogEventErrorCode,
                    ex.LogEventMessage);

                return SandboxedProcessPipExecutionResult.RetryableFailure(CancellationReason.ProcessStartFailure);
            }

            return await GetAndProcessResultAsync(process, allInputPathsUnderSharedOpaques, sandboxPrepTime, cancellationToken);
        }

        /// <summary>
        /// Translates VMs' host shared drive.
        /// </summary>
        /// <remarks>
        /// VMs' host net shares the drive where the enlistiment resides, e.g., D, that is net used by the VMs. When the process running in a VM
        /// accesses D:\E\f.txt, the process actually accesses D:\E\f.txt in the host. Thus, the file access manifest constructed in the host
        /// is often sufficient for running pips in VMs. However, some tools, like dotnet.exe, can access the path in UNC format, i.e.,
        /// \\192.168.0.1\D\E\f.txt. In this case, we need to supply a directory translation from that UNC path to the non-UNC path.
        /// </remarks>
        private void TranslateHostSharedUncDrive(SandboxedProcessInfo info)
        {
            DirectoryTranslator newTranslator = info.FileAccessManifest.DirectoryTranslator?.GetUnsealedClone() ?? new DirectoryTranslator();
            newTranslator.AddTranslation($@"\\{VmConstants.Host.IpAddress}\{VmConstants.Host.NetUseDrive}", $@"{VmConstants.Host.NetUseDrive}:");
            newTranslator.Seal();
            info.FileAccessManifest.DirectoryTranslator = newTranslator;
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
                    try
                    {
                        m_processIdListener?.Invoke(process.ProcessId);
                        result = await process.GetResultAsync();
                    }
                    finally
                    {
                        m_processIdListener?.Invoke(-process.ProcessId);
                    }
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
                                m_pipDescription,
                                exitCode,
                                stdOut,
                                stdErr);
                        }
                        else if (process is ExternalVmSandboxedProcess externalVmSandboxedProcess)
                        {
                            Tracing.Logger.Log.PipProcessFinishedExternalVm(
                                m_loggingContext,
                                m_pip.SemiStableHash,
                                m_pipDescription,
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

                // If we trust the statically declared accesses and the pip has processes configured to breakaway
                // then make sure we augment the reported accesses based on the pip static input/output declarations
                if (m_pip.TrustStaticallyDeclaredAccesses && m_pip.ChildProcessesToBreakawayFromSandbox.Length > 0)
                {
                    AugmentWithTrustedAccessesFromDeclaredArtifacts(result, m_pip, m_directoryArtifactContext);
                }

                var start = DateTime.UtcNow;
                SandboxedProcessPipExecutionResult executionResult =
                    await
                        ProcessSandboxedProcessResultAsync(
                            m_loggingContext,
                            result,
                            sandboxPrepTime.ElapsedMilliseconds,
                            cancellationTokenSource.Token,
                            process.GetDetoursMaxHeapSize() + result.DetoursMaxHeapSize,
                            allInputPathsUnderSharedOpaques);
                LogSubPhaseDuration(m_loggingContext, m_pip, SandboxedProcessCounters.SandboxedPipExecutorPhaseProcessingSandboxProcessResult, DateTime.UtcNow.Subtract(start));

                return ValidateDetoursCommunication(
                    executionResult,
                    lastMessageCount,
                    isMessageCountSemaphoreCreated);
            }
        }

        private void AugmentWithTrustedAccessesFromDeclaredArtifacts(SandboxedProcessResult result, Process process, IDirectoryArtifactContext directoryContext)
        {
            // If no ReportedProcess is found it's ok to just create an unnamed one since ReportedProcess is used for descriptive purposes only
            var reportedProcess = result.Processes?.FirstOrDefault() ?? new ReportedProcess(0, string.Empty, string.Empty);

            HashSet<ReportedFileAccess> trustedAccesses = ComputeDeclaredAccesses(process, directoryContext, reportedProcess);

            // All files accesses is an optional field. If present, we augment it with all the trusted ones
            if (result.FileAccesses != null)
            {
                result.FileAccesses.UnionWith(trustedAccesses);
            }

            // From all the trusted accesses, we only augment with the explicit ones
            result.ExplicitlyReportedFileAccesses.UnionWith(trustedAccesses.Where(access => access.ExplicitlyReported));
        }

        /// <summary>
        /// All declared inputs are represented by file reads, all declared outputs by file writes
        /// </summary>
        /// <remarks>
        /// All inputs are used as an over-approximation to stay on the safe side. Only outputs that are actually
        /// present on disk are turned into write accesses, since not all outputs are guaranteed to be produced.
        /// </remarks>
        private HashSet<ReportedFileAccess> ComputeDeclaredAccesses(Process process, IDirectoryArtifactContext directoryContext, ReportedProcess reportedProcess)
        {
            var trustedAccesses = new HashSet<ReportedFileAccess>();

            // Directory outputs are not supported for now. This is enforced by the process builder.
            Contract.Assert(process.DirectoryOutputs.Length == 0);

            foreach (var inputArtifact in process.Dependencies)
            {
                if (TryGetDeclaredAccessForFile(reportedProcess, inputArtifact, isRead: true, isDirectoryMember: false, out var reportedFileAccess))
                {
                    trustedAccesses.Add(reportedFileAccess);
                }
            }

            foreach (var inputDirectory in process.DirectoryDependencies)
            {
                // Source seal directories are not supported for now. Discovering them via enumerations
                // could be a way to do it in the future. This is enforced by the process builder.
                Contract.Assert(!directoryContext.GetSealDirectoryKind(inputDirectory).IsSourceSeal());

                var directoryContent = directoryContext.ListSealDirectoryContents(inputDirectory);

                foreach (var inputArtifact in directoryContent)
                {
                    if (TryGetDeclaredAccessForFile(reportedProcess, inputArtifact, isRead: true, isDirectoryMember: true, out var reportedFileAccess))
                    {
                        trustedAccesses.Add(reportedFileAccess);
                    }
                }
            }

            foreach (var outputArtifact in process.FileOutputs)
            {
                // We only add outputs that were actually produced
                if (TryGetDeclaredAccessForFile(reportedProcess, outputArtifact.Path, isRead: false, isDirectoryMember: false, out var reportedFileAccess) 
                    && FileUtilities.FileExistsNoFollow(outputArtifact.Path.ToString(m_pathTable)))
                {
                    trustedAccesses.Add(reportedFileAccess);
                }
            }

            return trustedAccesses;
        }

        private bool TryGetDeclaredAccessForFile(ReportedProcess process, AbsolutePath path, bool isRead, bool isDirectoryMember, out ReportedFileAccess reportedFileAccess)
        {
            // In some circumstances, to reduce bxl's memory footprint, FileAccessManifest objects are
            // released as soon as they are serialized and sent to the sandbox.  When that is the case,
            // we cannot look up the policy for the path because at this time the FAM is empty, i.e., 
            // we have to decide whether or not this access should be reported explicitly without having
            // the actual policy: only writes and sealed directory reads are reported explicitly.
            var reportExplicitly = !isRead || isDirectoryMember;
            var manifestPath = path;
            if (m_fileAccessManifest.IsFinalized && m_fileAccessManifest.TryFindManifestPathFor(path, out manifestPath, out var nodePolicy))
            {
                reportExplicitly = nodePolicy.HasFlag(FileAccessPolicy.ReportAccess);
            }

            // if the access is flagged to not be reported, and the global manifest flag does not require all accesses
            // to be reported, then we don't create it
            var shouldReport = m_fileAccessManifest.ReportFileAccesses || reportExplicitly;
            if (!shouldReport)
            {
                reportedFileAccess = default(ReportedFileAccess);
                return false;
            }

            reportedFileAccess = new ReportedFileAccess(
                ReportedFileOperation.CreateFile,
                process,
                isRead ? RequestedAccess.Read: RequestedAccess.Write,
                FileAccessStatus.Allowed,
                explicitlyReported: reportExplicitly,
                0,
                Usn.Zero,
                isRead ? DesiredAccess.GENERIC_READ : DesiredAccess.GENERIC_WRITE,
                ShareMode.FILE_SHARE_NONE,
                isRead ? CreationDisposition.OPEN_ALWAYS : CreationDisposition.CREATE_ALWAYS,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                manifestPath,
                path: (path == manifestPath) ? null : path.ToString(m_pathTable),
                enumeratePattern: null,
                FileAccessStatusMethod.TrustedTool);

            return true;
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
                        m_pipDescription,
                        ex.ToStringDemystified());
                    return SandboxedProcessPipExecutionResult.DetouringFailure(result);
                }

                bool fileExists = fi.Exists;
                if (fileExists)
                {
                    Tracing.Logger.Log.LogInternalDetoursErrorFileNotEmpty(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pipDescription,
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
                            m_pipDescription,
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
            Tracing.Logger.Log.PipStandardIOFailed(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                path,
                ex.GetLogEventErrorCode(),
                ex.GetLogEventMessage());
        }

        private void LogOutputPreparationFailed(string path, BuildXLException ex)
        {
            Tracing.Logger.Log.PipProcessOutputPreparationFailed(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                path,
                ex.LogEventErrorCode,
                ex.LogEventMessage,
                ex.ToString());
        }

        private void LogInvalidWarningRegex()
        {
            Tracing.Logger.Log.PipProcessInvalidWarningRegex(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                m_pathTable.StringTable.GetString(m_pip.WarningRegex.Pattern),
                m_pip.WarningRegex.Options.ToString());
        }

        private void LogInvalidErrorRegex()
        {
            Tracing.Logger.Log.PipProcessInvalidErrorRegex(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                m_pathTable.StringTable.GetString(m_pip.ErrorRegex.Pattern),
                m_pip.ErrorRegex.Options.ToString());
        }

        private void LogCommandLineTooLong(SandboxedProcessInfo info)
        {
            Tracing.Logger.Log.PipProcessCommandLineTooLong(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
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

            Dictionary<string, int> pipProperties = null;

            bool allOutputsPresent = false;
            bool loggingSuccess = true;

            ProcessTimes primaryProcessTimes = result.PrimaryProcessTimes;
            JobObject.AccountingInformation? jobAccounting = result.JobAccountingInformation;

            var start = DateTime.UtcNow;
            // If this operation fails, error was logged already
            bool sharedOpaqueProcessingSuccess = TryGetObservedFileAccesses(
                    result,
                    allInputPathsUnderSharedOpaques,
                    out var unobservedOutputs,
                    out var sharedDynamicDirectoryWriteAccesses,
                    out SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observed);

            LogSubPhaseDuration(m_loggingContext, m_pip, SandboxedProcessCounters.SandboxedPipExecutorPhaseGettingObservedFileAccesses, DateTime.UtcNow.Subtract(start), $"(count: {observed.Length})");

            TimeSpan time = primaryProcessTimes.TotalWallClockTime;
            if (result.TimedOut)
            {
                LogTookTooLongError(result, m_timeout, time);

                if (result.DumpCreationException != null)
                {
                    Tracing.Logger.Log.PipFailedToCreateDumpFile(
                        loggingContext,
                        m_pip.SemiStableHash,
                        m_pipDescription,
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
                    Tracing.Logger.Log.PipProcessFinishedDetourFailures(loggingContext, m_pip.SemiStableHash, m_pipDescription);
                }

                if (exitedSuccessfullyAndGracefully)
                {
                    Tracing.Logger.Log.PipProcessFinished(loggingContext, m_pip.SemiStableHash, m_pipDescription, result.ExitCode);
                    allOutputsPresent = CheckExpectedOutputs();
                }
                else
                {
                    if (!canceled)
                    {
                        LogFinishedFailed(result);
                        var propertiesResult = await TrySetPipPropertiesAsync(result);
                        pipProperties = propertiesResult.PipProperties;
                        if (!propertiesResult.Success)
                        {
                            Contract.Assert(loggingContext.ErrorWasLogged, "Error should be logged upon TrySetPipPropertiesAsync failure.");
                            // There was an error logged when extracting properties from the pip. The pip needs to fail and not retry
                            loggingSuccess = false;
                        }
                        else
                        {
                            if (exitedButCanBeRetried)
                            {
                                Tuple<AbsolutePath, Encoding> encodedStandardError = null;
                                Tuple<AbsolutePath, Encoding> encodedStandardOutput = null;

                                if (await TrySaveAndLogStandardOutputAsync(result) && await TrySaveAndLogStandardErrorAsync(result))
                                {
                                    encodedStandardOutput = GetEncodedStandardConsoleStream(result.StandardOutput);
                                    encodedStandardError = GetEncodedStandardConsoleStream(result.StandardError);
                                    return SandboxedProcessPipExecutionResult.RetryProcessDueToUserSpecifiedExitCode(
                                        result.NumberOfProcessLaunchRetries,
                                        result.ExitCode,
                                        primaryProcessTimes,
                                        jobAccounting,
                                        result.DetouringStatuses,
                                        sandboxPrepMs,
                                        sw.ElapsedMilliseconds,
                                        result.ProcessStartTime,
                                        maxDetoursHeapSize,
                                        m_containerConfiguration,
                                        encodedStandardError,
                                        encodedStandardOutput,
                                        pipProperties,
                                        sharedDynamicDirectoryWriteAccesses);
                                }

                                Contract.Assert(loggingContext.ErrorWasLogged, "Error should be logged upon TrySaveAndLogStandardOutput/Error failure.");
                                // There was an error logged when saving stdout or stderror.
                                loggingSuccess = false;
                            }
                            else if (m_sandboxConfig.RetryOnAzureWatsonExitCode && result.Processes.Any(p => p.ExitCode == AzureWatsonExitCode))
                            {
                                // Retry if the exit code is 0xDEAD.
                                var deadProcess = result.Processes.Where(p => p.ExitCode == AzureWatsonExitCode).First();
                                Tracing.Logger.Log.PipRetryDueToExitedWithAzureWatsonExitCode(
                                    m_loggingContext,
                                    m_pip.SemiStableHash,
                                    m_pipDescription,
                                    deadProcess.Path,
                                    deadProcess.ProcessId);

                                return SandboxedProcessPipExecutionResult.RetryProcessDueToAzureWatsonExitCode(
                                    result.NumberOfProcessLaunchRetries,
                                    result.ExitCode,
                                    primaryProcessTimes,
                                    jobAccounting,
                                    result.DetouringStatuses,
                                    sandboxPrepMs,
                                    sw.ElapsedMilliseconds,
                                    result.ProcessStartTime,
                                    maxDetoursHeapSize,
                                    m_containerConfiguration,
                                    pipProperties);
                            }
                        }
                    }
                }
            }

            int numSurvivingChildErrors = 0;
            if (!canceled && result.SurvivingChildProcesses?.Any() == true)
            {
                numSurvivingChildErrors = ReportSurvivingChildProcesses(result);
            }

            start = DateTime.UtcNow;

            var fileAccessReportingContext = new FileAccessReportingContext(
                loggingContext,
                m_context,
                m_sandboxConfig,
                m_pip,
                m_validateDistribution,
                m_fileAccessAllowlist);

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
                    m_pipDescription,
                    canceled,
                    result.ExitCode,
                    result.Killed,
                    numSurvivingChildErrors);
            }

            bool failedDueToWritingToStdErr = m_pip.WritingToStandardErrorFailsExecution && result.StandardError.HasLength;
            if (failedDueToWritingToStdErr)
            {
                Tracing.Logger.Log.PipProcessWroteToStandardErrorOnCleanExit(
                    m_loggingContext,
                    pipSemiStableHash: m_pip.SemiStableHash,
                    pipDescription: m_pipDescription,
                    pipSpecPath: m_pip.Provenance.Token.Path.ToString(m_context.PathTable),
                    pipWorkingDirectory: m_pip.WorkingDirectory.ToString(m_context.PathTable));
            }

            // This is the overall success of the process. At a high level, these are the things that can cause a process pip to fail:
            //      1. The process being killed (built into mainProcessExitedCleanly)
            //      2. The process not exiting with the appropriate exit code (mainProcessExitedCleanly)
            //      3. The process not creating all outputs (allOutputsPresent)
            //      4. The process running in a container, and even though succeeding, its outputs couldn't be moved to their expected locations
            //      5. The process wrote to standard error, and even though it may have exited with a succesfull exit code, WritingToStandardErrorFailsPip 
            //         is set (failedDueToWritingToStdErr)
            bool mainProcessSuccess = mainProcessExitedCleanly && allOutputsPresent && !failedDueToWritingToStdErr;

            bool standardOutHasBeenWrittenToLog = false;
            bool errorOrWarnings = !mainProcessSuccess || m_numWarnings > 0;

            bool shouldPersistStandardOutput = errorOrWarnings || m_pip.StandardOutput.IsValid;
            if (shouldPersistStandardOutput)
            {
                if (!await TrySaveStandardOutputAsync(result))
                {
                    loggingSuccess = false;
                }
            }

            bool shouldPersistStandardError = !canceled && (errorOrWarnings || m_pip.StandardError.IsValid);
            if (shouldPersistStandardError)
            {
                if (!await TrySaveStandardErrorAsync(result))
                {
                    loggingSuccess = false;
                }
            }

            LogSubPhaseDuration(m_loggingContext, m_pip, SandboxedProcessCounters.SandboxedPipExecutorPhaseProcessingStandardOutputs, DateTime.UtcNow.Subtract(start));

            start = DateTime.UtcNow;

            // After standard output and error may been saved, and shared dynamic write accesses were identified, we can merge
            // outputs back to their original locations
            if (!await m_processInContainerManager.MergeOutputsIfNeededAsync(m_pip, m_containerConfiguration, m_context, sharedDynamicDirectoryWriteAccesses))
            {
                // If the merge failed, update the status flags
                mainProcessSuccess = false;
                errorOrWarnings = true;
            }

            bool errorWasTruncated = false;
            // if some outputs are missing or the process wrote to stderr, we are logging this process as a failed one (even if it finished with a success exit code).
            if ((!mainProcessExitedCleanly || !allOutputsPresent || failedDueToWritingToStdErr) && !canceled && loggingSuccess)
            {
                standardOutHasBeenWrittenToLog = true;

                LogErrorResult logErrorResult = await TryLogErrorAsync(result, allOutputsPresent, failedDueToWritingToStdErr);
                errorWasTruncated = logErrorResult.ErrorWasTruncated;
                loggingSuccess = logErrorResult.Success;
            }

            if (m_numWarnings > 0 && loggingSuccess && !canceled)
            {
                if (!await TryLogWarningAsync(result.StandardOutput, result.StandardError))
                {
                    loggingSuccess = false;
                }
            }

            // The full output may be requested based on the result of the pip. If the pip failed, the output may have been reported
            // in TryLogErrorAsync above. Only replicate the output if the error was truncated due to an error regex
            if ((!standardOutHasBeenWrittenToLog || errorWasTruncated) && loggingSuccess && !canceled)
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

            LogSubPhaseDuration(m_loggingContext, m_pip, SandboxedProcessCounters.SandboxedPipExecutorPhaseLoggingOutputs, DateTime.UtcNow.Subtract(start));

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
                Tracing.Logger.Log.ReadWriteFileAccessConvertedToReadWarning(loggingContext, m_pip.SemiStableHash, m_pipDescription);
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
                    m_pipDescription,
                    m_pip.Provenance.Token.Path.ToString(m_pathTable),
                    m_workingDirectory,
                    result.StandardInputException.Message + Environment.NewLine + result.StandardInputException.StackTrace);
            }

            if (hasMessageParsingError)
            {
                Tracing.Logger.Log.PipProcessMessageParsingError(
                    loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
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
            else if (!sharedOpaqueProcessingSuccess)
            {
                status = SandboxedProcessPipExecutionStatus.SharedOpaquePostProcessingFailed;
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
                        Tracing.Logger.Log.PipOutputNotAccessed(
                          m_loggingContext,
                          m_pip.SemiStableHash,
                          m_pipDescription,
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
                containerConfiguration: m_containerConfiguration,
                pipProperties: pipProperties,
                timedOut: result.TimedOut);
        }

        private async Task<(bool Success, Dictionary<string, int> PipProperties)> TrySetPipPropertiesAsync(SandboxedProcessResult result)
        {
            OutputFilter propertyFilter = OutputFilter.GetPipPropertiesFilter(m_pip.EnableMultiLineErrorScanning);

            var filteredErr = await TryFilterAsync(result.StandardError, propertyFilter, appendNewLine: true);
            var filteredOut = await TryFilterAsync(result.StandardOutput, propertyFilter, appendNewLine: true);
            if (filteredErr.HasError || filteredOut.HasError)
            {
                // We have logged an error when processing the standard error or standard output stream. 
                return (Success: false, PipProperties: null);
            }

            string errorMatches = filteredErr.FilteredOutput;
            string outputMatches = filteredOut.FilteredOutput;
            string allMatches = errorMatches + Environment.NewLine + outputMatches;

            if (!string.IsNullOrWhiteSpace(allMatches))
            {
                string[] matchedProperties = allMatches.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                return (Success: true, PipProperties: matchedProperties.ToDictionary(val => val, val => 1));
            }

            return (Success: true, PipProperties: null);
        }

        private Tuple<AbsolutePath, Encoding> GetEncodedStandardConsoleStream(SandboxedProcessOutput output)
        {
            Contract.Requires(output.IsSaved);
            Contract.Requires(!output.HasException);

            return Tuple.Create(AbsolutePath.Create(m_pathTable, output.FileName), output.Encoding);
        }

        private bool TryPrepareFileAccessMonitoring(HashSet<AbsolutePath> allInputPathsUnderSharedOpaques)
        {
            if (!PrepareFileAccessMonitoringCommon())
            {
                return false;
            }

            TryPrepareFileAccessMonitoringForPip(m_pip, allInputPathsUnderSharedOpaques);

            return true;
        }

        private bool PrepareFileAccessMonitoringCommon()
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

            // Untrack the globally untracked paths specified in the configuration
            if (m_pip.RequireGlobalDependencies)
            {
                foreach (var path in m_sandboxConfig.GlobalUnsafeUntrackedScopes)
                {
                    // Translate the path and untrack the translated one
                    if (m_fileAccessManifest.DirectoryTranslator != null)
                    {
                        var pathString = path.ToString(m_pathTable);
                        var translatedPathString = m_fileAccessManifest.DirectoryTranslator.Translate(pathString);
                        var translatedPath = AbsolutePath.Create(m_pathTable, translatedPathString);

                        if (path != translatedPath)
                        {
                            AddUntrackedScopeToManifest(translatedPath);
                        }
                    }

                    // Untrack the original path
                    AddUntrackedScopeToManifest(path);
                }
            }

            // Directories specified in the directory translator can be directory symlinks or junctions that are meant to be directories in normal circumstances.
            if (m_fileAccessManifest.DirectoryTranslator != null) 
            {
                foreach (var translation in m_fileAccessManifest.DirectoryTranslator.Translations)
                {
                    var sourcePath = AbsolutePath.Create(m_pathTable, translation.SourcePath);
                    var targetPath = AbsolutePath.Create(m_pathTable, translation.TargetPath);
                    m_fileAccessManifest.AddPath(
                        sourcePath,
                        mask: FileAccessPolicy.MaskNothing,
                        values: FileAccessPolicy.TreatDirectorySymlinkAsDirectory);
                    m_fileAccessManifest.AddPath(
                        targetPath,
                        mask: FileAccessPolicy.MaskNothing,
                        values: FileAccessPolicy.TreatDirectorySymlinkAsDirectory);
                }
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                var binaryPaths = new BinaryPaths();

                AddUntrackedScopeToManifest(AbsolutePath.Create(m_pathTable, binaryPaths.DllDirectoryX64));
                AddUntrackedScopeToManifest(AbsolutePath.Create(m_pathTable, binaryPaths.DllDirectoryX86));
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
                m_detoursFailuresFile = GetDetoursInternalErrorFilePath();

                // Delete the file
                if (FileUtilities.FileExistsNoFollow(m_detoursFailuresFile))
                {
                    Analysis.IgnoreResult(FileUtilities.TryDeleteFile(m_detoursFailuresFile, tempDirectoryCleaner: m_tempDirectoryCleaner));
                }

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
                        Tracing.Logger.Log.LogMessageCountSemaphoreExists(m_loggingContext, m_pip.SemiStableHash, m_pipDescription);
                        return false;
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

        private void AddUntrackedScopeToManifest(AbsolutePath path, FileAccessManifest manifest = null) => (manifest ?? m_fileAccessManifest).AddScope(
            path,
            mask: m_excludeReportAccessMask,
            values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);

        private void AddUntrackedPathToManifest(AbsolutePath path, FileAccessManifest manifest = null) => (manifest ?? m_fileAccessManifest).AddPath(
            path,
            mask: m_excludeReportAccessMask,
            values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);

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

                var vmAdminProfilePath = ShouldSandboxedProcessExecuteInVm
                    ? AbsolutePath.Create(m_pathTable, VmConstants.UserProfile.Path)
                    : AbsolutePath.Invalid;

                foreach (AbsolutePath path in pip.UntrackedPaths)
                {
                    // We mask Report to simplify handling of explicitly-reported directory-dependency or transitive-dependency accesses
                    // (they should never fail for untracked accesses, which should be invisible).

                    // We allow the real input timestamp to be seen for untracked paths. This is to preserve existing behavior, where the timestamp of untracked stuff is never modified.
                    addUntrackedPath(path, processedPaths);

                    var correspondingPath = CreatePathForActualRedirectedUserProfilePair(path, userProfilePath, redirectedUserProfilePath);
                    addUntrackedPath(correspondingPath, processedPaths);

                    if (ShouldSandboxedProcessExecuteInVm)
                    {
                        var vmPath = CreatePathForVmAdminUserProfile(vmAdminProfilePath, path, userProfilePath, redirectedUserProfilePath);
                        addUntrackedPath(vmPath, processedPaths);
                    }

                    // Untrack real logs directory if the redirected one is untracked.
                    if (m_loggingConfiguration != null && m_loggingConfiguration.RedirectedLogsDirectory.IsValid && m_loggingConfiguration.RedirectedLogsDirectory == path)
                    {
                        addUntrackedPath(m_loggingConfiguration.LogsDirectory, processedPaths);
                    }
                }

                if (ShouldSandboxedProcessExecuteInVm)
                {
                    // User profiles are tricky on the Admin VM since it's potentially running some of the first processes launched
                    // on a newly provisioned VM. When this happens various directories that would usually already be existing may be created.
                    // We assign a generous access policy for CreateDirectory access under the user profile to handle this.
                    m_fileAccessManifest.AddScope(userProfilePath, values: FileAccessPolicy.AllowCreateDirectory, mask: FileAccessPolicy.AllowAll);
                    m_fileAccessManifest.AddScope(redirectedUserProfilePath, values: FileAccessPolicy.AllowCreateDirectory, mask: FileAccessPolicy.AllowAll);
                }

                foreach (AbsolutePath path in pip.UntrackedScopes)
                {
                    // Note that untracked scopes are quite dangerous. We allow writes, reads, and probes for non-existent files.
                    // We mask Report to simplify handling of explicitly-reported directory-dependence or transitive-dependency accesses
                    // (they should never fail for untracked accesses, which should be invisible).

                    // The default mask for untracked scopes is to not report anything.
                    // We block input timestamp faking for untracked scopes. This is to preserve existing behavior, where the timestamp of untracked stuff is never modified.
                    addUntrackedScope(path, processedPaths);

                    var correspondingPath = CreatePathForActualRedirectedUserProfilePair(path, userProfilePath, redirectedUserProfilePath);
                    addUntrackedScope(correspondingPath, processedPaths);

                    if (ShouldSandboxedProcessExecuteInVm)
                    {
                        var vmPath = CreatePathForVmAdminUserProfile(vmAdminProfilePath, path, userProfilePath, redirectedUserProfilePath);
                        addUntrackedScope(vmPath, processedPaths);
                    }

                    // Untrack real logs directory if the redirected one is untracked.
                    if (m_loggingConfiguration != null && m_loggingConfiguration.RedirectedLogsDirectory.IsValid && m_loggingConfiguration.RedirectedLogsDirectory == path)
                    {
                        addUntrackedScope(m_loggingConfiguration.LogsDirectory, processedPaths);
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
                            (directory.IsSharedOpaque ? FileAccessPolicy.ReportAccess : FileAccessPolicy.Deny) |
                            // For shared opaques and if allowed undeclared source reads is enabled, make sure that any file used as an undeclared input under the
                            // shared opaque gets deny write access. Observe that with exclusive opaques they are wiped out before the pip runs, so it is moot to check for inputs
                            // TODO: considering configuring this policy for all shared opaques, and not only when AllowedUndeclaredSourceReads is set. The case of a write on an undeclared
                            // input is more likely to happen when undeclared sources are allowed, but also possible otherwise. For now, this is just a conservative way to try this feature
                            // out for a subset of our scenarios.
                            (m_pip.AllowUndeclaredSourceReads && directory.IsSharedOpaque ? FileAccessPolicy.OverrideAllowWriteForExistingFiles : FileAccessPolicy.Deny);

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
                            var mask = DefaultMask;
                            // Allow read accesses and reporting. Reporting is needed since these may be dynamic accesses and we need to cross check them
                            var values = FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess;

                            // In the case of a writable sealed directory under a produced shared opaque, we don't want to block writes
                            // for the entire cone: some files may be dynamically written in the context of the shared opaque that may fall
                            // under the cone of the sealed directory. So for partial sealed directories and shared opaque directories that
                            // are under a produced shared opaque we don't establish any specific policies.
                            var kind = m_directoryArtifactContext.GetSealDirectoryKind(directory);
                            if (!(IsPathUnderASharedOpaqueRoot(directory.Path)
                                && (kind == SealDirectoryKind.Partial || kind == SealDirectoryKind.SharedOpaque)))
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

                if (OperatingSystemHelper.IsUnixOS)
                {
                    AddUnixSpecificSandboxedProcessFileAccessPolicies();
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

            void addUntrackedPath(AbsolutePath untrackedPath, HashSet<AbsolutePath> processedPaths)
            {
                if (untrackedPath.IsValid)
                {
                    AddUntrackedPathToManifest(untrackedPath);
                    AllowCreateDirectoryForDirectoriesOnPath(untrackedPath, processedPaths);
                }
            }

            void addUntrackedScope(AbsolutePath untrackedScope, HashSet<AbsolutePath> processedPaths)
            {
                if (untrackedScope.IsValid)
                {
                    AddUntrackedScopeToManifest(untrackedScope);
                    AllowCreateDirectoryForDirectoriesOnPath(untrackedScope, processedPaths);
                }
            }
        }

        private void AddUnixSpecificSandboxedProcessFileAccessPolicies()
        {
            Contract.Requires(OperatingSystemHelper.IsUnixOS);

            if (ShouldSandboxedProcessExecuteExternal)
            {
                // When executing the pip using external tool, the file access manifest tree is sealed by
                // serializing it as bytes. Thus, after the external tool deserializes the manifest tree,
                // the manifest cannot be modified further.

                // CODESYNC: SandboxedProcessUnix.cs
                // The sandboxed process for unix modifies the manifest tree. We do the same modification here.
                m_fileAccessManifest.AddPath(
                    AbsolutePath.Create(m_pathTable, SandboxedProcessUnix.ShellExecutable),
                    mask: FileAccessPolicy.MaskNothing,
                    values: FileAccessPolicy.AllowReadAlways);

                AbsolutePath stdInFile = AbsolutePath.Create(
                    m_pathTable,
                    SandboxedProcessUnix.GetStdInFilePath(m_pip.WorkingDirectory.ToString(m_pathTable), m_pip.SemiStableHash));
                
                m_fileAccessManifest.AddPath(
                   stdInFile,
                   mask: FileAccessPolicy.MaskNothing,
                   values: FileAccessPolicy.AllowAll);
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

        private AbsolutePath CreatePathForVmAdminUserProfile(
            AbsolutePath vmAdminProfilePath,
            AbsolutePath path,
            AbsolutePath realUserProfilePath,
            AbsolutePath redirectedUserProfilePath)
        {
            if (!vmAdminProfilePath.IsValid)
            {
                return AbsolutePath.Invalid;
            }

            if (realUserProfilePath.IsValid && path.IsWithin(m_pathTable, realUserProfilePath))
            {
                return path.Relocate(m_pathTable, realUserProfilePath, vmAdminProfilePath);
            }

            if (redirectedUserProfilePath.IsValid && path.IsWithin(m_pathTable, redirectedUserProfilePath))
            {
                return path.Relocate(m_pathTable, redirectedUserProfilePath, vmAdminProfilePath);
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
                // Observe that if double writes are allowed, then we can't just block writes: we need to allow them to happen and then
                // observe the result to figure out if they conform to the double write policy
                mask: DefaultMask & (m_pip.DoubleWritePolicy.ImpliesDoubleWriteAllowed()? FileAccessPolicy.MaskNothing : ~FileAccessPolicy.AllowWrite));

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
            while (currentPath.IsValid && !allInputPathsUnderSharedOpaques.Contains(currentPath) && currentPath.IsWithin(m_pathTable, sharedOpaqueRoot))
            {
                // We want to set a policy for the directory without affecting the scope for the underlying artifacts
                m_fileAccessManifest.AddPath(
                        currentPath,
                        values: FileAccessPolicy.AllowRead | FileAccessPolicy.AllowReadIfNonexistent, // we don't need access reporting here
                        mask: DefaultMask); // but block real timestamps

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
                      DefaultMask &
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

        /// <summary>
        /// Tests to see if the semantic path info is invalid (path was not under a mount) or writable
        /// (path is under a writable mount). Paths not under mounts don't have any enforcement around them
        /// so we allow them to be written.
        /// </summary>
        private static bool IsInvalidOrWritable(in SemanticPathInfo semanticPathInfo)
        {
            return !semanticPathInfo.IsValid || semanticPathInfo.IsWritable;
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

        private IBuildParameters PrepareEnvironmentVariables()
        {
            var environmentVariables = m_pipEnvironment.GetEffectiveEnvironmentVariables(
                m_pip,
                m_pipDataRenderer,
                m_pip.RequireGlobalDependencies ? m_sandboxConfig.GlobalUnsafePassthroughEnvironmentVariables : null);

            if (ShouldSandboxedProcessExecuteInVm)
            {
                var vmSpecificEnvironmentVariables = new[]
                {
                    new KeyValuePair<string, string>(VmSpecialEnvironmentVariables.IsInVm, "1"),
                    new KeyValuePair<string, string>(
                        VmSpecialEnvironmentVariables.HostHasRedirectedUserProfile,
                        m_layoutConfiguration != null && m_layoutConfiguration.RedirectedUserProfileJunctionRoot.IsValid ? "1" : "0"),
                    new KeyValuePair<string, string>(
                        VmSpecialEnvironmentVariables.HostUserProfile,
                        m_layoutConfiguration != null && m_layoutConfiguration.RedirectedUserProfileJunctionRoot.IsValid
                            ? s_possibleRedirectedUserProfilePath
                            : s_userProfilePath)
                };

                // Because the pip is going to be run under a different user (i.e., Admin) in VM, all passthrough environment
                // variables that are related to user profile, like %UserProfile%, %UserName%, %AppData%, %LocalAppData%, etc, need
                // to be overriden with C:\User\Administrator. The original value of user-profile %ENVVAR%, if any, will be preserved
                // in %[BUILDXL]VM_HOST_<ENVVAR>%
                //
                // We cannot delay the values for all passthrough environment variables until the pip is "sent" to the VM because the pip
                // may need the environment variables and only the host has the values for them. For example, some pips need
                // %__CLOUDBUILD_AUTH_HELPER_ROOT__% that only exists in the host.
                var vmPassThroughEnvironmentVariables = m_pip.EnvironmentVariables
                    .Where(e => e.IsPassThrough && VmConstants.UserProfile.Environments.ContainsKey(m_pipDataRenderer.Render(e.Name)))
                    .SelectMany(e =>
                    {
                        string name = m_pipDataRenderer.Render(e.Name);
                        return new[]
                        {
                                new KeyValuePair<string, string>(name, VmConstants.UserProfile.Environments[name].value),
                                new KeyValuePair<string, string>(VmSpecialEnvironmentVariables.HostEnvVarPrefix + name, environmentVariables[name])
                            };
                    });

                environmentVariables = environmentVariables.Override(vmSpecificEnvironmentVariables.Concat(vmPassThroughEnvironmentVariables));
            }

            return environmentVariables;
        }

        /// <summary>
        /// Creates and cleans the Process's temp directory if necessary
        /// </summary>
        /// <param name="environmentVariables">Environment</param>
        private bool PrepareTempDirectory(ref IBuildParameters environmentVariables)
        {
            Contract.Requires(environmentVariables != null);

            string path = null;

            // If specified, clean the pip specific temp directory.
            if (m_pip.TempDirectory.IsValid)
            {
                if (!PreparePath(m_pip.TempDirectory))
                {
                    return false;
                }
            }

            // Clean all specified temp directories.
            foreach (var additionalTempDirectory in m_pip.AdditionalTempDirectories)
            {
                if (!PreparePath(additionalTempDirectory))
                {
                    return false;
                }
            }

            if (!ShouldSandboxedProcessExecuteInVm)
            {
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
                    Tracing.Logger.Log.PipTempDirectorySetupFailure(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pipDescription,
                        path,
                        ex.ToStringDemystified());
                    return false;
                }
            }

            // Override environment variable.
            if (ShouldSandboxedProcessExecuteInVm)
            {
                if (m_pip.TempDirectory.IsValid
                    && m_tempFolderRedirectionForVm.TryGetValue(m_pip.TempDirectory, out AbsolutePath redirectedTempDirectory))
                {
                    // When running in VM, a pip often queries TMP or TEMP to get the path to the temp directory.
                    // For most cases, the original path is sufficient because the path is redirected to the one in VM.
                    // However, a number of operations, like creating/accessing/enumerating junctions, will fail.
                    // Recall that junctions are evaluated locally, so that creating junction using host path is like creating junctions
                    // on the host from the VM.
                    string redirectedTempDirectoryPath = redirectedTempDirectory.ToString(m_pathTable);
                    var overridenEnvVars = DisallowedTempVariables
                        .Select(v => new KeyValuePair<string, string>(v, redirectedTempDirectoryPath))
                        .Concat(new[]
                        {
                            new KeyValuePair<string, string>(VmSpecialEnvironmentVariables.VmTemp, redirectedTempDirectoryPath),
                            new KeyValuePair<string, string>(VmSpecialEnvironmentVariables.VmOriginalTemp, m_pip.TempDirectory.ToString(m_pathTable)),
                        });

                    environmentVariables = environmentVariables.Override(overridenEnvVars);
                }
            }

            return true;

            bool PreparePath(AbsolutePath pathToPrepare)
            {
                return !ShouldSandboxedProcessExecuteInVm ? CleanTempDirectory(pathToPrepare) : PrepareTempDirectoryForVm(pathToPrepare);
            }
        }

        private bool CleanTempDirectory(AbsolutePath tempDirectoryPath)
        {
            Contract.Requires(tempDirectoryPath.IsValid);

            try
            {
                // Temp directories are lazily, best effort cleaned after the pip finished. The previous build may not
                // have finished this work before exiting so we must double check.
                PreparePathForDirectory(
                    tempDirectoryPath.ToString(m_pathTable),
                    createIfNonExistent: m_sandboxConfig.EnsureTempDirectoriesExistenceBeforePipExecution);
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipTempDirectoryCleanupFailure(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    tempDirectoryPath.ToString(m_pathTable),
                    ex.LogEventMessage);

                return false;
            }

            return true;
        }

        private bool PrepareTempDirectoryForVm(AbsolutePath tempDirectoryPath)
        {
            // Suppose that the temp directory is D:\Bxl\Out\Object\Pip123\Temp\t_0.
            string path = tempDirectoryPath.ToString(m_pathTable);

            // Delete any existence of path D:\Bxl\Out\Object\Pip123\Temp\t_0.
            try
            {
                if (FileUtilities.FileExistsNoFollow(path))
                {
                    // Path exists as a file or a symlink (directory/file symlink).
                    FileUtilities.DeleteFile(path, waitUntilDeletionFinished: true, tempDirectoryCleaner: m_tempDirectoryCleaner);
                }

                if (FileUtilities.DirectoryExistsNoFollow(path))
                {
                    // Path exists as a real directory: wipe out that directory.
                    FileUtilities.DeleteDirectoryContents(path, deleteRootDirectory: true, tempDirectoryCleaner: m_tempDirectoryCleaner);
                }
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipTempDirectorySetupFailure(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    path,
                    ex.ToStringDemystified());
                return false;
            }

            // Suppose that the root of temp directory in VM is T:\BxlTemp.
            // Users can specify the root for redirected temp folder. This user-specified root is currently only used by self-host's unit tests because T drive is not
            // guaranteed to exist when running locally on desktop.
            string redirectedTempRoot = m_sandboxConfig.RedirectedTempFolderRootForVmExecution.IsValid
                ? m_sandboxConfig.RedirectedTempFolderRootForVmExecution.ToString(m_pathTable)
                : VmConstants.Temp.Root;

            // Create a target temp folder in VM, e.g., T:\BxlTemp\D__\Bxl\Out\Object\Pip123\Temp\t_0.
            // Note that this folder may not exist yet in VM. The sandboxed process executor that runs in the VM is responsible for
            // creating (or ensuring the existence) of the folder.
            string pathRoot = Path.GetPathRoot(path);
            string pathRootAsDirectoryName = pathRoot
                .Replace(Path.VolumeSeparatorChar, '_')
                .Replace(Path.DirectorySeparatorChar, '_')
                .Replace(Path.AltDirectorySeparatorChar, '_');
            string redirectedPath = Path.Combine(redirectedTempRoot, pathRootAsDirectoryName, path.Substring(pathRoot.Length));

            if (redirectedPath.Length > FileUtilities.MaxDirectoryPathLength())
            {
                // Force short path: T:\BxlTemp\Pip123\0
                redirectedPath = Path.Combine(redirectedTempRoot, m_pip.FormattedSemiStableHash, m_tempFolderRedirectionForVm.Count.ToString());
            }

            AbsolutePath tempRedirectedPath = AbsolutePath.Create(m_pathTable, redirectedPath);

            m_tempFolderRedirectionForVm.Add(tempDirectoryPath, tempRedirectedPath);

            // Create a directory symlink D:\Bxl\Out\Object\Pip123\Temp\t_0 -> T:\BxlTemp\D__\Bxl\Out\Object\Pip123\Temp\t_0.
            // Any access to D:\Bxl\Out\Object\Pip123\Temp\t_0 will be redirected to T:\BxlTemp\D__\Bxl\Out\Object\Pip123\Temp\t_0.
            // To make this access work, one needs to ensure that symlink evaluation behaviors R2R and R2L are enabled in VM.
            // VmCommandProxy is ensuring that such symlink evaluation behaviors are enabled during VM initialization.
            // To create the directory symlink, one also needs to ensure that the parent directory of the directory symlink exists,
            // i.e., D:\Bxl\Out\Object\Pip123\Temp exists.
            FileUtilities.CreateDirectory(Path.GetDirectoryName(path));
            var createDirectorySymlink = FileUtilities.TryCreateSymbolicLink(path, redirectedPath, isTargetFile: false);

            if (!createDirectorySymlink.Succeeded)
            {
                Tracing.Logger.Log.PipTempSymlinkRedirectionError(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    redirectedPath,
                    path,
                    createDirectorySymlink.Failure.Describe());
                return false;
            }

            Contract.Assert(m_fileAccessManifest != null);

            // Ensure that T:\BxlTemp\D__\Bxl\Out\Object\Pip123\Temp\t_0 is untracked. Thus, there is no need for a directory translation.
            m_fileAccessManifest.AddScope(tempRedirectedPath, mask: m_excludeReportAccessMask, values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);

            Tracing.Logger.Log.PipTempSymlinkRedirection(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                redirectedPath,
                path);

            return true;
        }

        /// <summary>
        /// Each output of this process is either written (output only) or rewritten (input and output).
        /// We delete written outputs (shouldn't be observed) and re-deploy rewritten outputs (should be a private copy).
        /// Rewritten outputs may be initially non-private and so not writable, i.e., hardlinked to other locations.
        /// Note that this function is also responsible for stamping private outputs such that the tool sees
        /// <see cref="WellKnownTimestamps.OldOutputTimestamp"/>.
        /// </summary>
        private async Task<bool> PrepareOutputsAsync()
        {
            using (var preserveOutputAllowlistWrapper = Pools.GetAbsolutePathSet())
            using (var dependenciesWrapper = Pools.GetAbsolutePathSet())
            using (var outputDirectoriesWrapper = Pools.GetAbsolutePathSet())
            {
                var preserveOutputAllowlist = preserveOutputAllowlistWrapper.Instance;
                foreach (AbsolutePath path in m_pip.PreserveOutputAllowlist)
                {
                    preserveOutputAllowlist.Add(path);
                }

                if (!await PrepareDirectoryOutputsAsync(preserveOutputAllowlist))
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
                            if (ShouldPreserveDeclaredOutput(output.Path, preserveOutputAllowlist))
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

                if (m_isLazySharedOpaqueOutputDeletionEnabled && !DeleteSharedOpaqueOutputsRecordedInSidebandFile())
                {
                    return false;
                }
            }

            return true;
        }

        private bool DeleteSharedOpaqueOutputsRecordedInSidebandFile()
        {
            var sidebandFile = SidebandWriter.GetSidebandFileForProcess(m_context.PathTable, m_layoutConfiguration.SharedOpaqueSidebandDirectory, m_pip);
            try
            {
                var start = DateTime.UtcNow;
                var sharedOpaqueOutputsToDelete = SidebandReader.ReadSidebandFile(sidebandFile, ignoreChecksum: false);
                var deletionResults = sharedOpaqueOutputsToDelete // TODO: possibly parallelize file deletion 
                    .Where(FileUtilities.FileExistsNoFollow)
                    .Select(path => FileUtilities.TryDeleteFile(path)) // TODO: what about deleting directories?
                    .ToArray();
                LogSubPhaseDuration(m_loggingContext, m_pip, SandboxedProcessCounters.SandboxedPipExecutorPhaseDeletingSharedOpaqueOutputs, DateTime.UtcNow.Subtract(start));

                // select failures
                var failures = deletionResults
                    .Where(maybeDeleted => !maybeDeleted.Succeeded) // select failures only
                    .Where(maybeDeleted => FileUtilities.FileExistsNoFollow(maybeDeleted.Failure.Path)) // double check that the files indeed exist
                    .ToArray();

                // log failures (if any)
                if (failures.Length > 0)
                {
                    var files = string.Join(string.Empty, failures.Select(f => $"{Environment.NewLine}  {f.Failure.Path}"));
                    var firstFailure = failures.First().Failure.DescribeIncludingInnerFailures();
                    Tracing.Logger.Log.CannotDeleteSharedOpaqueOutputFile(m_loggingContext, m_pipDescription, sidebandFile, files, firstFailure);
                    return false;
                }

                // log deleted files
                var deletedFiles = string.Join(string.Empty, deletionResults.Where(r => r.Succeeded).Select(r => $"{Environment.NewLine}  {r.Result}"));
                Tracing.Logger.Log.SharedOpaqueOutputsDeletedLazily(m_loggingContext, m_pip.FormattedSemiStableHash, sidebandFile, deletedFiles);

                // delete the sideband file itself
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(sidebandFile));

                return true;
            }
            catch (Exception e) when (e is IOException || e is BuildXLException)
            {
                Tracing.Logger.Log.CannotReadSidebandFileError(m_loggingContext, sidebandFile, e.Message);
                return false;
            }
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

        private async Task<bool> PrepareDirectoryOutputsAsync(HashSet<AbsolutePath> preserveOutputAllowlist)
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
                        // if the directory is present, check whether there are any known stale outputs
                        else if (m_staleOutputsUnderSharedOpaqueDirectories != null
                            && m_staleOutputsUnderSharedOpaqueDirectories.TryGetValue(directoryOutput.Path, out var staleOutputs))
                        {
                            foreach (var output in staleOutputs)
                            {
                                PreparePathForOutputFile(output.Path);
                            }
                        }
                    }
                    else
                    {
                        if (dirExist && ShouldPreserveDeclaredOutput(directoryOutput.Path, preserveOutputAllowlist))
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

                                string filePathNotPrivate = null;

                                foreach (var path in filePaths)
                                {
                                    if (!await m_makeOutputPrivate(path))
                                    {
                                        filePathNotPrivate = path;
                                        break;
                                    }
                                }

                                if (filePathNotPrivate != null)
                                {
                                    Tracing.Logger.Log.PipProcessPreserveOutputDirectoryFailedToMakeFilePrivate(
                                        m_loggingContext, 
                                        m_pip.SemiStableHash, 
                                        m_pipDescription, 
                                        directoryOutput.Path.ToString(m_pathTable), filePathNotPrivate);

                                    PreparePathForDirectory(directoryPathStr, createIfNonExistent: true);
                                }
                            }
                        }
                        else
                        {
                            PreparePathForDirectory(directoryPathStr, createIfNonExistent: true);
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
        private Task<bool> PrepareResponseFileAsync() =>
            WritePipAuxiliaryFileAsync(
                m_pip.ResponseFile,
                () =>
                {
                    Contract.Assume(m_pip.ResponseFileData.IsValid, "ResponseFile path requires having ResponseFile data");
                    return m_pip.ResponseFileData.ToString(m_pipDataRenderer);
                },
                Tracing.Logger.Log.PipProcessResponseFileCreationFailed);

        /// <summary>
        /// ChangeAffectedInputListFile file is created before executing pips that consume it
        /// </summary>
        private Task<bool> PrepareChangeAffectedInputListFileAsync(IReadOnlyCollection<AbsolutePath> changeAffectedInputs = null) =>
            changeAffectedInputs == null
                ? Task.FromResult(true)
                : WritePipAuxiliaryFileAsync(
                    m_pip.ChangeAffectedInputListWrittenFile,
                    () => string.Join(
                        Environment.NewLine,
                        changeAffectedInputs.Select(i => i.ToString(m_pathTable))),
                    Tracing.Logger.Log.PipProcessChangeAffectedInputsWrittenFileCreationFailed);

        private async Task<bool> WritePipAuxiliaryFileAsync(
            FileArtifact fileArtifact, 
            Func<string> createContent, 
            Action<LoggingContext, long, string, string, int, string> logException)
        {
            if (!fileArtifact.IsValid)
            {
                return true;
            }

            string destination = fileArtifact.Path.ToString(m_context.PathTable);

            try
            {
                string directoryName = ExceptionUtilities.HandleRecoverableIOException(
                        () => Path.GetDirectoryName(destination),
                        ex => { throw new BuildXLException("Cannot get directory name", ex); });
                PreparePathForOutputFile(fileArtifact);
                FileUtilities.CreateDirectory(directoryName);
                await FileUtilities.WriteAllTextAsync(destination, createContent(), Encoding.UTF8);
            }
            catch (BuildXLException ex)
            {
                logException(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pipDescription,
                    destination,
                    ex.LogEventErrorCode,
                    ex.LogEventMessage);

                return false;
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

            char c4 = fileName[len - 4];
            if (c4.ToUpperInvariantFast() == '.')
            {
                // RC's temp files have no extension.
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
            if (c1.ToUpperInvariantFast() != 'R')
            {
                return false;
            }

            char c2 = fileName[beginCharIndex + 2];
            if (c2.ToUpperInvariantFast() != 'C')
            {
                return false;
            }

            char c3 = fileName[beginCharIndex + 3];
            if (c3.ToUpperInvariantFast() != 'X')
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
        /// These special tools/cases should be allowlisted, but we already have customers deployed specs without
        /// using allowlists.
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
        /// Returns true if any symlinks are found on a given path
        /// </summary>
        private bool PathContainsSymlinks(AbsolutePath path)
        {
            Counters.IncrementCounter(SandboxedProcessCounters.DirectorySymlinkPathsCheckedCount);
            if (!path.IsValid)
            {
                return false;
            }

            if (FileUtilities.IsDirectorySymlinkOrJunction(path.ToString(m_context.PathTable)))
            {
                return true;
            }

            return PathContainsSymlinksCached(path.GetParent(m_context.PathTable));
        }

        private readonly Dictionary<AbsolutePath, bool> m_isDirSymlinkCache = new Dictionary<AbsolutePath, bool>();

        /// <summary>
        /// Same as <see cref="PathContainsSymlinks"/> but with caching around it.
        /// </summary>
        private bool PathContainsSymlinksCached(AbsolutePath path)
        {
            Counters.IncrementCounter(SandboxedProcessCounters.DirectorySymlinkPathsQueriedCount);
            return m_isDirSymlinkCache.GetOrAdd(path, PathContainsSymlinks);
        }

        private bool CheckIfPathContainsSymlinks(AbsolutePath path)
        {
            using (Counters.StartStopwatch(SandboxedProcessCounters.DirectorySymlinkCheckingDuration))
            {
                return PathContainsSymlinksCached(path);
            }
        }

        /// <summary>
        /// Creates an <see cref="ObservedFileAccess"/> for each unique path accessed.
        /// The returned array is sorted by the expanded path and additionally contains no duplicates.
        /// </summary>
        /// <remarks>
        /// Additionally, it returns the write accesses that were observed in shared dynamic
        /// directories, which are used later to determine pip ownership.
        /// </remarks>
        /// <returns>Whether the operation succeeded. This operation may fail only in regards to shared dynamic write access processing.</returns>
        private bool TryGetObservedFileAccesses(
            SandboxedProcessResult result,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            out List<AbsolutePath> unobservedOutputs,
            out IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedDynamicDirectoryWriteAccesses,
            out SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observedAccesses)
        {
            unobservedOutputs = null;
            if (result.ExplicitlyReportedFileAccesses == null || result.ExplicitlyReportedFileAccesses.Count == 0)
            {
                unobservedOutputs = m_pip.FileOutputs.Where(f => RequireOutputObservation(f)).Select(f => f.Path).ToList();
                sharedDynamicDirectoryWriteAccesses = CollectionUtilities.EmptyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>>();
                observedAccesses = SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<ObservedFileAccess>.Empty,
                        new ObservedFileAccessExpandedPathComparer(m_context.PathTable.ExpandedPathComparer));
                return true;
            }
            
            bool sharedOutputDirectoriesAreRedirected = m_pip.NeedsToRunInContainer && m_pip.ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories();

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
                        // If it is an incremental tool and the pip allows preserving outputs, do not ignore.
                        if (!IsIncrementalToolAccess(reported))
                        {
                            continue;
                        }
                    }

                    AbsolutePath parsedPath;

                    // We want an AbsolutePath for the full access. This may not be parse-able due to the accessed path
                    // being invalid, or a path format we do not understand. Note that TryParseAbsolutePath logs as appropriate
                    // in the latter case.
                    if (!reported.TryParseAbsolutePath(m_context, m_loggingContext, m_pip, out parsedPath))
                    {
                        continue;
                    }

                    // Let's resolve all intermediate symlink dirs if configured.
                    // TODO: this logic will be eventually replaced by doing the right thing on detours side.
                    // This option is Windows-specific
                    ReportedFileAccess finalReported;
                    if (m_sandboxConfig.UnsafeSandboxConfiguration.ProcessSymlinkedAccesses())
                    {
                        Contract.Assume(m_symlinkedAccessResolver != null);
                        if (m_symlinkedAccessResolver.ResolveDirectorySymlinks(reported, parsedPath, out finalReported, out var finalPath))
                        {
                            // If the final path falls under a configured policy that ignores accesses, then we also ignore it
                            var success = m_fileAccessManifest.TryFindManifestPathFor(finalPath, out _, out var nodePolicy);
                            if (success & (nodePolicy & FileAccessPolicy.ReportAccess) == 0)
                            {
                                continue;
                            }

                            // Let's generate read accesses for the intermediate dir symlinks on the original path, so we avoid
                            // underbuilds if those change
                            m_symlinkedAccessResolver.AddReadsForIntermediateSymlinks(m_fileAccessManifest, reported, parsedPath, accessesByPath);

                            parsedPath = finalPath;
                        }
                    }
                    else
                    {
                        finalReported = reported;
                    }

                    bool shouldExclude = false;

                    // Remove special accesses see Bug: #121875.
                    // Some perform file accesses, which don't yet fall into any configurable file access manifest category.
                    // These special tools/cases should be allowlisted, but we already have customers deployed specs without
                    // using allowlists.
                    if (GetSpecialCaseRulesForCoverageAndSpecialDevices(parsedPath))
                    {
                        shouldExclude = true;
                    }
                    else
                    {
                        if (AbsolutePath.TryCreate(m_context.PathTable, finalReported.Process.Path, out AbsolutePath processPath)
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
                        ? existingAccessesToPath.Add(finalReported)
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
                using (var excludedPathsWrapper = Pools.GetAbsolutePathSet())
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
                        ReportedFileAccess firstAccess = entry.Value.First();

                        // Discard entries that have a single MacLookup report on a path that contains an intermediate directory symlink.
                        // Reason: observed accesses computed here should only contain fully expanded paths to avoid ambiguity;
                        //         on Mac, all access reports except for MacLookup report fully expanded paths, so only MacLookup paths need to be curated
                        if (
                            entry.Value.Count == 1 &&
                            firstAccess.Operation == ReportedFileOperation.MacLookup &&
                            firstAccess.ManifestPath.IsValid &&
                            CheckIfPathContainsSymlinks(firstAccess.ManifestPath.GetParent(m_context.PathTable)))
                        {
                            Counters.IncrementCounter(SandboxedProcessCounters.DirectorySymlinkPathsDiscardedCount);
                            continue;
                        }

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

                            if (access.RequestedAccess == RequestedAccess.Probe
                                && IsIncrementalToolAccess(access))
                            {
                                isProbe = false;
                            }

                            // TODO: Remove this when WDG can grog this feature with no flag.
                            if (m_sandboxConfig.UnsafeSandboxConfiguration.ExistingDirectoryProbesAsEnumerations ||
                                access.RequestedAccess == RequestedAccess.Enumerate)
                            {
                                hasEnumeration = true;
                            }

                            // if the access is a write on a file (that is, not on a directory), then the path is a candidate to be part of a shared opaque
                            isPathCandidateToBeOwnedByASharedOpaque |= 
                                access.RequestedAccess.HasFlag(RequestedAccess.Write) &&
                                !access.FlagsAndAttributes.HasFlag(FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY) &&
                                !access.IsDirectoryCreationOrRemoval();
                        }

                        // if the path is still a candidate to be part of a shared opaque, that means there was at least a write to that path. If the path is then
                        // in the cone of a shared opaque, then it is a dynamic write access
                        bool? isAccessUnderASharedOpaque = null;
                        if (isPathCandidateToBeOwnedByASharedOpaque && IsAccessUnderASharedOpaque(
                                entry.Key,
                                firstAccess,
                                dynamicWriteAccesses,
                                out AbsolutePath sharedDynamicDirectoryRoot))
                        {
                            dynamicWriteAccesses[sharedDynamicDirectoryRoot].Add(entry.Key);
                            isAccessUnderASharedOpaque = true;
                            // This is a known output, so don't store it
                            continue;
                        }

                        // The following two lines need to be removed in order to report file accesses for
                        // undeclared files and sealed directories. But since this is a breaking change, we do
                        // it under an unsafe flag.
                        if (m_sandboxConfig.UnsafeSandboxConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques)
                        {
                            // If the access occurred under any of the pip shared opaque outputs, and the access is not happening on any known input paths (neither dynamic nor static)
                            // then we just skip reporting the access. Together with the above step, this means that no accesses under shared opaques that represent outputs are actually
                            // reported as observed accesses. This matches the same behavior that occurs on static outputs.
                            if (!allInputPathsUnderSharedOpaques.Contains(entry.Key) &&
                                (isAccessUnderASharedOpaque == true || IsAccessUnderASharedOpaque(entry.Key, firstAccess, dynamicWriteAccesses, out _)))
                            {
                                continue;
                            }
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

                    // AccessesUnsorted might include various accesses to directories leading to the files inside of shared opaques,
                    // mainly CreateDirectory and ProbeDirectory. To make strong fingerprint computation more stable, we are excluding such
                    // accesses from the list that is passed into the ObservedInputProcessor (as a result, they will not be a part of the path set).
                    //
                    // Example, given this path: '\sod\dir1\dir2\file.txt', we will exclude accesses to dir1 and dir2 only.
                    var excludedPaths = excludedPathsWrapper.Instance;
                    foreach (var sod in dynamicWriteAccesses)
                    {
                        foreach (var file in sod.Value)
                        {
                            var pathElement = file.GetParent(m_context.PathTable);

                            while (pathElement.IsValid && pathElement != sod.Key && excludedPaths.Add(pathElement))
                            {
                                pathElement = pathElement.GetParent(m_context.PathTable);
                            }
                        }
                    }

                    var filteredAccessesUnsorted = accessesUnsorted.Where(shouldIncludeAccess).ToList();

                    observedAccesses = SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>.CloneAndSort(
                        filteredAccessesUnsorted,
                        new ObservedFileAccessExpandedPathComparer(m_context.PathTable.ExpandedPathComparer));

                    var mutableWriteAccesses = new Dictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>>(dynamicWriteAccesses.Count);

                    // We know that all accesses here were write accesses, but we don't actually know if in the end the corresponding file
                    // still exists or whether the file was replaced with a directory afterwards. E.g.:
                    // * the tool could have created a file but removed it right after
                    // * the tool could have created a file but then removed it and created a directory
                    // We only care about the access if its final shape is not a directory
                    foreach (var kvp in dynamicWriteAccesses)
                    {
                        var fileWrites = new List<FileArtifactWithAttributes>(kvp.Value.Count);
                        mutableWriteAccesses[kvp.Key] = fileWrites;
                        foreach (AbsolutePath writeAccess in kvp.Value)
                        {
                            string outputPath;
                            if (sharedOutputDirectoriesAreRedirected)
                            {
                                outputPath = m_processInContainerManager.GetRedirectedOpaqueFile(writeAccess, kvp.Key, m_containerConfiguration).ToString(m_pathTable);
                            }
                            else
                            {
                                outputPath = writeAccess.ToString(m_pathTable);
                            }
                            
                            var maybeResult = FileUtilities.TryProbePathExistence(outputPath, followSymlink: false);
                            if (!maybeResult.Succeeded)
                            {
                                Tracing.Logger.Log.CannotProbeOutputUnderSharedOpaque(
                                    m_loggingContext,
                                    m_pip.GetDescription(m_context),
                                    writeAccess.ToString(m_pathTable),
                                    maybeResult.Failure.DescribeIncludingInnerFailures());
                                
                                sharedDynamicDirectoryWriteAccesses = CollectionUtilities.EmptyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>>();

                                return false;
                            }

                            switch (maybeResult.Result)
                            {
                                case PathExistence.ExistsAsDirectory:
                                    // Directories are not reported as explicit content, since we don't have the functionality today to persist them in the cache.
                                    continue;
                                case PathExistence.ExistsAsFile:
                                    // If outputs are redirected, we don't want to store a tombstone file
                                    if (!sharedOutputDirectoriesAreRedirected || !FileUtilities.IsWciTombstoneFile(outputPath))
                                    {
                                        fileWrites.Add(FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(writeAccess), FileExistence.Required));
                                    }
                                    break;
                                case PathExistence.Nonexistent:
                                    fileWrites.Add(FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(writeAccess), FileExistence.Temporary));
                                    break;
                            }
                        }
                    }

                    sharedDynamicDirectoryWriteAccesses = mutableWriteAccesses;

                    return true;

                    bool shouldIncludeAccess(ObservedFileAccess access)
                    {
                        // if not in the excludedPaths set --> include
                        if (!excludedPaths.Contains(access.Path))
                        {
                            return true;
                        }

                        // else, include IFF:
                        //   (1) access is a directory enumeration, AND
                        //   (2) the directory was not created by this pip
                        return 
                            access.ObservationFlags.HasFlag(ObservationFlags.Enumeration)
                            && !access.Accesses.Any(rfa => rfa.IsDirectoryCreation());
                    }
                }
            }
        }

        private bool IsAccessUnderASharedOpaque(
            AbsolutePath accessPath,
            ReportedFileAccess access, 
            Dictionary<AbsolutePath, HashSet<AbsolutePath>> dynamicWriteAccesses, 
            out AbsolutePath sharedDynamicDirectoryRoot)
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
            // (other constructs are allowed, but they don't affect detours manifest).
            // This means we cannot directly use the manifest path to check if it is the root of a shared opaque,
            // but we can start looking up from the reported manifest path.
            // Because of bottom-up search, if a pip declares nested shared opaque directories, the innermost directory
            // wins the ownership of a produced file.

            // We need to consider opaque directory exclusions here. So if there are any exclusions configured, we can't start from the manifest
            // path, since the exclusion can be configured to be under a given opaque root

            var initialNode = m_pip.OutputDirectoryExclusions.Length == 0 ? access.ManifestPath.Value : accessPath.Value;

            using var outputDirectoryExclusionSetWrapper = Pools.AbsolutePathSetPool.GetInstance();
            var outputDirectoryExclusionSet = outputDirectoryExclusionSetWrapper.Instance;
            outputDirectoryExclusionSet.AddRange(m_pip.OutputDirectoryExclusions);

            // TODO: consider adding a cache from manifest paths to containing shared opaques. It is likely
            // that many writes for a given pip happens under the same cones.

            foreach (var currentNode in m_context.PathTable.EnumerateHierarchyBottomUp(initialNode))
            {
                var currentPath = new AbsolutePath(currentNode);
                
                // if the path is under an exclusion, then it is not under a shared opaque
                if (outputDirectoryExclusionSet.Contains(currentPath))
                {
                    sharedDynamicDirectoryRoot = AbsolutePath.Invalid;
                    return false;
                }

                // If we haven't found an owner yet, and the current path is a shared opaque root, then we found one
                // Observe we may have found an owner already but we may be still searching upwards to make sure there 
                // are no exclusions that would rule that out
                if (!sharedDynamicDirectoryRoot.IsValid && dynamicWriteAccesses.ContainsKey(currentPath))
                {
                    sharedDynamicDirectoryRoot = currentPath;
                    // If there are no exclusions, then we found an owning root and we can shortcut the search here
                    if (outputDirectoryExclusionSet.Count == 0)
                    {
                        return true;
                    }
                }
            }

            return sharedDynamicDirectoryRoot.IsValid;
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
            Tracing.Logger.Log.PipProcessFinishedFailed(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, result.ExitCode);
        }

        private void LogTookTooLongWarning(TimeSpan timeout, TimeSpan time, TimeSpan warningTimeout)
        {
            Tracing.Logger.Log.PipProcessTookTooLongWarning(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                Bound(time.TotalMilliseconds),
                Bound(warningTimeout.TotalMilliseconds),
                Bound(timeout.TotalMilliseconds));
        }

        private void LogTookTooLongError(SandboxedProcessResult result, TimeSpan timeout, TimeSpan time)
        {
            Contract.Assume(result.Killed);
            Analysis.IgnoreArgument(result);
            string dumpString = result.DumpFileDirectory ?? string.Empty;

            Tracing.Logger.Log.PipProcessTookTooLongError(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                Bound(time.TotalMilliseconds),
                Bound(timeout.TotalMilliseconds),
                dumpString);
        }

        private async Task<bool> TrySaveAndLogStandardErrorAsync(SandboxedProcessResult result)
        {
            if (!await TrySaveStandardErrorAsync(result))
            {
                return false;
            }

            string standardErrorPath = result.StandardError.FileName;
            Tracing.Logger.Log.PipProcessStandardError(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, standardErrorPath);
            return true;
        }

        private async Task<bool> TrySaveStandardErrorAsync(SandboxedProcessResult result)
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
            return true;
        }

        private async Task<bool> TrySaveAndLogStandardOutputAsync(SandboxedProcessResult result)
        {
            if (!await TrySaveStandardOutputAsync(result))
            {
                return false;
            }

            string standardOutputPath = result.StandardOutput.FileName;
            Tracing.Logger.Log.PipProcessStandardOutput(m_loggingContext, m_pip.SemiStableHash, m_pipDescription, standardOutputPath);
            return true;
        }

        private async Task<bool> TrySaveStandardOutputAsync(SandboxedProcessResult result)
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
            return true;
        }

        private void LogChildrenSurvivedKilled()
        {
            Tracing.Logger.Log.PipProcessChildrenSurvivedKilled(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription);
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
                            pipDescription: m_pipDescription,
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
                        m_pipDescription,
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

        private void FormatOutputAndPaths(string standardOut, string standardError,
            string standardOutPath, string standardErrorPath,
            out string outputToLog, out string pathsToLog, out string extraMessage,
            bool messageWasTruncated,
            bool errorWasFiltered)
        {
            // Only display error/out if it is non-empty. This avoids adding duplicated newlines in the message.
            // Also use the emptiness as a hit for whether to show the path to the file on disk
            bool standardOutEmpty = string.IsNullOrWhiteSpace(standardOut);
            bool standardErrorEmpty = string.IsNullOrWhiteSpace(standardError);

            extraMessage = string.Empty;

            if (errorWasFiltered)
            {
                extraMessage = "This message has been filtered by a regex. ";
            }

            var stdInLogDir = false;
            string standardOutPathInLog = string.Empty;
            string standardErrorPathInLog = string.Empty;
            if (messageWasTruncated)
            {
                if (m_sandboxConfig.OutputReportingMode == OutputReportingMode.TruncatedOutputOnError)
                {
                    if (!standardOutEmpty && CopyFileToLogDirectory(standardOutPath, m_pip.FormattedSemiStableHash, out standardOutPathInLog))
                    {
                        stdInLogDir = true;
                    }

                    if (!standardErrorEmpty && CopyFileToLogDirectory(standardErrorPath, m_pip.FormattedSemiStableHash, out standardErrorPathInLog))
                    {
                        stdInLogDir = true;
                    }

                    if (stdInLogDir)
                    {
                        extraMessage += $"Please find the complete stdout/stderr in the following file(s) in the log directory.";
                    }
                }
                else
                {
                    extraMessage += "Please find the complete stdout/stderr in the following file(s) or in the DX0066 event in the log file.";
                }
            }


            outputToLog = (standardOutEmpty ? string.Empty : standardOut) +
                (!standardOutEmpty && !standardErrorEmpty ? Environment.NewLine : string.Empty) +
                (standardErrorEmpty ? string.Empty : standardError);


            var originalStdPaths = (standardOutEmpty ? string.Empty : standardOutPath) +
                (!standardOutEmpty && !standardErrorEmpty ? Environment.NewLine : string.Empty) +
                (standardErrorEmpty ? string.Empty : standardErrorPath);

            var stdPathsInLogDir = (standardOutEmpty ? string.Empty : standardOutPathInLog) +
                (!standardOutEmpty && !standardErrorEmpty ? Environment.NewLine : string.Empty) +
                (standardErrorEmpty ? string.Empty : standardErrorPathInLog);

            pathsToLog = !messageWasTruncated
                ? string.Empty
                : stdInLogDir
                ? stdPathsInLogDir
                : originalStdPaths;
        }

        private bool CopyFileToLogDirectory(string path, string formattedSemiStableHash, out string relativeFilePath)
        {
            if (File.Exists(path) 
                && m_loggingConfiguration != null
                && Directory.Exists(m_loggingConfiguration.LogsDirectory.ToString(m_pathTable)))
            {
                var relativeDirectoryPath = Path.Combine(StdOutputsDirNameInLog, formattedSemiStableHash);
                relativeFilePath = Path.Combine(relativeDirectoryPath, Path.GetFileName(path));
                var directoryWithPipHash = Path.Combine(m_loggingConfiguration.LogsDirectory.ToString(m_pathTable), relativeDirectoryPath);
                // Make sure the file has a unique path
                var destFilePathWithPipHash = GetNextFileName(Path.Combine(directoryWithPipHash, Path.GetFileName(path)));

                Directory.CreateDirectory(directoryWithPipHash);
                File.Copy(path, destFilePathWithPipHash);

                return true;
            }

            relativeFilePath = string.Empty;
            return false;
        }

        private string GetNextFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var basename = fileName.Substring(0, fileName.Length - ext.Length);
            int i = 0;
            while (File.Exists(fileName))
            {
                fileName = $"{basename}.{++i}{ext}";
            }

            return fileName;
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

        private async Task<LogErrorResult> TryLogErrorAsync(SandboxedProcessResult result, bool allOutputsPresent, bool failedDueToWritingToStdErr)
        {
            // Initializing error regex just before it is actually needed to save some cycles.
            if (!await TryInitializeErrorRegexAsync())
            {
                return new LogErrorResult(success: false, errorWasTruncated: false);
            }

            var errorFilter = OutputFilter.GetErrorFilter(m_errorRegex, m_pip.EnableMultiLineErrorScanning);

            bool errorWasTruncated = false;
            var exceedsLimit = OutputExceedsLimit(result.StandardOutput) || OutputExceedsLimit(result.StandardError);
            if (!exceedsLimit || m_sandboxConfig.OutputReportingMode == OutputReportingMode.TruncatedOutputOnError)
            {
                var standardErrorFilterResult = await TryFilterAsync(result.StandardError, errorFilter, appendNewLine: true);
                var standardOutputFilterResult = await TryFilterAsync(result.StandardOutput, errorFilter, appendNewLine: true);
                string standardError = standardErrorFilterResult.FilteredOutput;
                string standardOutput = standardOutputFilterResult.FilteredOutput;

                if (standardError == null || standardOutput == null)
                {
                    return new LogErrorResult(success: false, errorWasTruncated: false);
                }

                if (string.IsNullOrEmpty(standardError) && string.IsNullOrEmpty(standardOutput))
                {
                    // Standard error and standard output are empty.
                    // This could be because the filter is too aggressive and the entire output was filtered out.
                    // Rolling back to a non-filtered approach because some output is better than nothing.
                    standardErrorFilterResult = await TryFilterLineByLineAsync(result.StandardError, s => true, appendNewLine: true);
                    standardOutputFilterResult = await TryFilterLineByLineAsync(result.StandardOutput, s => true, appendNewLine: true);
                    standardError = standardErrorFilterResult.FilteredOutput;
                    standardOutput = standardOutputFilterResult.FilteredOutput;
                }

                // Ignore empty lines
                var standardErrorInResult = await result.StandardError.ReadValueAsync();
                var standardOutputInResult = await result.StandardOutput.ReadValueAsync();
                if (standardError.Replace(Environment.NewLine, string.Empty).Trim().Length != standardErrorInResult.Replace(Environment.NewLine, string.Empty).Trim().Length ||
                    standardOutput.Replace(Environment.NewLine, string.Empty).Trim().Length != standardOutputInResult.Replace(Environment.NewLine, string.Empty).Trim().Length)
                {
                    errorWasTruncated = true;
                }

                HandleErrorsFromTool(standardError);
                HandleErrorsFromTool(standardOutput);

                LogPipProcessError(result, allOutputsPresent, failedDueToWritingToStdErr, standardError, standardOutput, errorWasTruncated, standardErrorFilterResult.IsFiltered || standardOutputFilterResult.IsFiltered);

                return new LogErrorResult(success: true, errorWasTruncated: errorWasTruncated);
            }

            long stdOutTotalLength = 0;
            long stdErrTotalLength = 0;

            // The output exceeds the limit and the full output has been requested. Emit it in chunks
            if (!await TryEmitFullOutputInChunks(errorFilter))
            {
                return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
            }

            if (stdOutTotalLength == 0 && stdErrTotalLength == 0)
            {
                // Standard error and standard output are empty.
                // This could be because the filter is too aggressive and the entire output was filtered out.
                // Rolling back to a non-filtered approach because some output is better than nothing.
                errorWasTruncated = false;
                if (!await TryEmitFullOutputInChunks(filter: null))
                {
                    return new LogErrorResult(success: false, errorWasTruncated: errorWasTruncated);
                }
            }

            return new LogErrorResult(success: true, errorWasTruncated: errorWasTruncated);

            async Task<bool> TryEmitFullOutputInChunks(OutputFilter? filter)
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
                            string stdError = await ReadNextChunkAsync(errorReader, result.StandardError, filter?.LinePredicate);
                            string stdOut = await ReadNextChunkAsync(outReader, result.StandardOutput, filter?.LinePredicate);

                            if (stdError == null || stdOut == null)
                            {
                                return false;
                            }

                            if (filter?.Regex != null)
                            {
                                stdError = filter.Value.ExtractMatches(stdError);
                                stdOut = filter.Value.ExtractMatches(stdOut);
                            }

                            if (string.IsNullOrEmpty(stdOut) && string.IsNullOrEmpty(stdError))
                            {
                                continue;
                            }

                            stdOutTotalLength += stdOut.Length;
                            stdErrTotalLength += stdError.Length;

                            HandleErrorsFromTool(stdError);
                            HandleErrorsFromTool(stdOut);

                            // For the last iteration, check if error was truncated
                            if (errorReader.Peek() == -1 && outReader.Peek() == -1)
                            {
                                if (stdOutTotalLength != result.StandardOutput.Length || stdErrTotalLength != result.StandardError.Length)
                                {
                                    errorWasTruncated = true;
                                }
                            }

                            LogPipProcessError(result, allOutputsPresent, failedDueToWritingToStdErr, stdError, stdOut, errorWasTruncated, false);
                        }

                        return true;
                    }
                }
            }
        }

        private void LogPipProcessError(SandboxedProcessResult result, bool allOutputsPresent, bool failedDueToWritingToStdErr, string stdError, string stdOut, bool errorWasTruncated, bool errorWasFiltered)
        {
            string outputTolog;
            string outputPathsToLog;
            string messageAboutPathsToLog;
            FormatOutputAndPaths(
                stdOut,
                stdError,
                result.StandardOutput.FileName,
                result.StandardError.FileName,
                out outputTolog,
                out outputPathsToLog,
                out messageAboutPathsToLog,
                errorWasTruncated,
                errorWasFiltered);

            string optionalMessage = !allOutputsPresent ? 
                EventConstants.PipProcessErrorMissingOutputsSuffix :
                failedDueToWritingToStdErr?
                    EventConstants.PipProcessErrorWroteToStandardError : 
                    string.Empty;

            Tracing.Logger.Log.PipProcessError(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                m_pip.Provenance.Token.Path.ToString(m_pathTable),
                m_workingDirectory,
                GetToolName(),
                EnsureToolOutputIsNotEmpty(outputTolog),
                messageAboutPathsToLog,
                AddTrailingNewLineIfNeeded(outputPathsToLog),
                result.ExitCode,
                optionalMessage,
                m_pip.GetShortDescription(m_context));
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
                            m_pipDescription,
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
                        string stdError = await ReadNextChunkAsync(errorReader, result.StandardError);
                        string stdOut = await ReadNextChunkAsync(outReader, result.StandardOutput);

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

                        Tracing.Logger.Log.PipProcessOutput(
                            m_loggingContext,
                            m_pip.SemiStableHash,
                            m_pipDescription,
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
        private async Task<string> ReadNextChunkAsync(TextReader reader, SandboxedProcessOutput output, Predicate<string> filterPredicate = null)
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
            var errorFilterResult = await TryFilterLineByLineAsync(standardError, IsWarning, appendNewLine: true);
            var outputFilterResult = await TryFilterLineByLineAsync(standardOutput, IsWarning, appendNewLine: true);

            string warningsError = standardError == null ? string.Empty : errorFilterResult.FilteredOutput;
            string warningsOutput = standardOutput == null ? string.Empty : outputFilterResult.FilteredOutput;

            if (warningsError == null ||
                warningsOutput == null)
            {
                return false;
            }

            bool warningWasTruncated = false;           
            // Ignore empty lines
            var standardErrorInResult = await standardError.ReadValueAsync();
            var standardOutputInResult = await standardOutput.ReadValueAsync();
            if (warningsError.Replace(Environment.NewLine, string.Empty).Trim().Length != standardErrorInResult.Replace(Environment.NewLine, string.Empty).Trim().Length ||
                warningsOutput.Replace(Environment.NewLine, string.Empty).Trim().Length != standardOutputInResult.Replace(Environment.NewLine, string.Empty).Trim().Length)
            {
                warningWasTruncated = true;
            }

            FormatOutputAndPaths(
                warningsOutput,
                warningsError,
                standardOutput.FileName,
                standardError.FileName,
                out string outputTolog,
                out string outputPathsToLog,
                out string messageAboutPathsToLog,
                warningWasTruncated,
                errorFilterResult.IsFiltered || outputFilterResult.IsFiltered);

            Tracing.Logger.Log.PipProcessWarning(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pipDescription,
                m_pip.Provenance.Token.Path.ToString(m_pathTable),
                m_workingDirectory,
                GetToolName(),
                EnsureToolOutputIsNotEmpty(outputTolog),
                messageAboutPathsToLog,
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
                    !hasProcessName(pr, "ProcessTreeContextCreator.exe") &&
                    !m_pip.AllowedSurvivingChildProcessNames.Any(procName => hasProcessName(pr, procName.ToString(m_context.StringTable))));

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
                        m_pipDescription,
                        numSurvive,
                        Environment.NewLine + string.Join(Environment.NewLine, result.SurvivingChildProcesses.Select(p => p.Path)));
                }
            }
            else
            {
                Tracing.Logger.Log.PipProcessChildrenSurvivedError(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pipDescription,
                        numErrors,
                        Environment.NewLine + string.Join(Environment.NewLine, unexpectedSurvivingChildProcesses.Select(p => $"{p.Path} ({p.ProcessId}) {getProcessArgs(p)}")));
            }

            return numErrors;

            static bool hasProcessName(ReportedProcess pr, string name)
            {
                return string.Equals(Path.GetFileName(pr.Path), name, StringComparison.OrdinalIgnoreCase);
            }

            static string getProcessArgs(ReportedProcess pr)
            {
                if (string.IsNullOrWhiteSpace(pr.ProcessArgs))
                {
                    return string.Empty;
                }

                return pr.ProcessArgs.Substring(0, Math.Min(256, pr.ProcessArgs.Length));
            }
        }

        private void LogFileAccessTables(Process pip)
        {
            // TODO: This dumps a very low-level table; consider producing a nicer representation
            // Instead of logging each line, consider storing manifest and logging file name
            foreach (string line in m_fileAccessManifest.Describe())
            {
                Tracing.Logger.Log.PipProcessFileAccessTableEntry(
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
        private bool ShouldPreserveDeclaredOutput(AbsolutePath path, HashSet<AbsolutePath> allowlist)
        {
            if (!m_shouldPreserveOutputs)
            {
                // If the pip does not allow preserve outputs, return false
                return false;
            }

            if (allowlist.Count == 0)
            {
                // If the allowlist is empty, every output is preserved
                return true;
            }

            if (allowlist.Contains(path))
            {
                // Only preserve the file or directories that are given in the allowlist.
                return true;
            }

            return false;
        }

        private bool IsIncrementalToolAccess(ReportedFileAccess access)
        {
            if (!IsIncrementalPreserveOutputPip)
            {
                return false;
            }

            if (m_incrementalToolFragments.Count == 0)
            {
                return false;
            }

            string toolPath = access.Process.Path;

            bool result;
            if (m_incrementalToolMatchCache.TryGetValue(toolPath, out result))
            {
                return result;
            }

            result = false;
            foreach (var incrementalToolSuffix in m_incrementalToolFragments)
            {
                if (toolPath.EndsWith(incrementalToolSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    break;
                }
            }

            // Cache the result
            m_incrementalToolMatchCache.AddItem(toolPath, result);
            return result;
        }
    }
}
