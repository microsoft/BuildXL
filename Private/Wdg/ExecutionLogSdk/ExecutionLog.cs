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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Loads and exposes all data from a BuildXL execution log
    /// </summary>
    public sealed class ExecutionLog : ExecutionLogTargetBase, IExecutionLogTarget, IDisposable
    {
        #region Private properties
        // The following collections are to always remain empty. They are for
        // reducing memory consumption. Any object that needs to expose a
        // collection but in certain load option configurations will never have
        // that collection filled can simply reference the appropriate empty
        // collection here and avoid allocating its own collection.
        private readonly static ConcurrentHashSet<PipDescriptor> emptyConcurrentHashSetOfPipDescriptor = new ConcurrentHashSet<PipDescriptor>();
        private readonly static ConcurrentHashSet<DirectoryDescriptor> emptyConcurrentHashSetOfDirectoryDescriptor = new ConcurrentHashSet<DirectoryDescriptor>();
        private readonly static ConcurrentHashSet<FileDescriptor> emptyConcurrentHashSetOfFileDescriptor = new ConcurrentHashSet<FileDescriptor>();
        private readonly static ConcurrentHashSet<ProcessInstanceDescriptor> emptyConcurrentHashSetOfReportedProcesses = new ConcurrentHashSet<ProcessInstanceDescriptor>();
        private readonly static StringIdEnvVarDictionary emptyStringIDEnvVarDictionary = new StringIdEnvVarDictionary(null, 0);
        private readonly static AbsolutePathConcurrentHashSet emptyAbsolutePathConcurrentHashSet = new AbsolutePathConcurrentHashSet(null);

        /// <summary>
        /// Private dictionary that maps file names to file descriptor objects
        /// </summary>
        private AbsolutePathConcurrentDictionary<FileDescriptor> m_fileDescriptorDictionary = new AbsolutePathConcurrentDictionary<FileDescriptor>(null);

        /// <summary>
        /// Private dictionary that maps directory paths to directory descriptor objects
        /// </summary>
        private AbsolutePathConcurrentDictionary<DirectoryDescriptor> m_directoryDescriptorDictionary = new AbsolutePathConcurrentDictionary<DirectoryDescriptor>(null);

        /// <summary>
        /// Private dictionary that maps process executable names to process descriptor objects
        /// </summary>
        private readonly Tool.ExecutionLogSdk.ConcurrentDictionary<string, ProcessDescriptor> m_reportedProcessesDictionary = new Tool.ExecutionLogSdk.ConcurrentDictionary<string, ProcessDescriptor>();

        /// <summary>
        /// Private dictionary that maps reported file accesses to file names
        /// </summary>
        private AbsolutePathConcurrentDictionary<IReadOnlyCollection<ReportedFileAccessDescriptor>> m_reportedFileAccessesDictionary =
            new AbsolutePathConcurrentDictionary<IReadOnlyCollection<ReportedFileAccessDescriptor>>(null);

        /// <summary>
        /// Private dictionary that maps environment variable names to environment variable descriptor objects
        /// </summary>
        private StringIdConcurrentDictionary<EnvironmentVariableDescriptor> m_environmentVariablesDictionary = new StringIdConcurrentDictionary<EnvironmentVariableDescriptor>(null);

        /// When the execution log events are replayed, all events are added to lists. Event replay cannot be done in parallel and storing then events in a list
        /// allows us to get through the execution log quickly and later allows us to process the deserialized events in parallel.
        /// <summary>
        /// Private list that stores the ProcessExecutionMonitoringReportedEventData objects that have been replayed from the execution log
        /// </summary>
        private List<ProcessExecutionMonitoringReportedEventData> m_listExecutionMonitoringReportedEvents = new List<ProcessExecutionMonitoringReportedEventData>();

        /// <summary>
        /// Private dictionary that stores the ObservedInputsEventData objects that have been replayed from the execution log.
        /// The dictionary stores data from the last ObservedInputs event for each pip.
        /// </summary>
        private ConcurrentDictionary<uint, ObservedInputsEventData> m_dictObservedInputsEvents = new ConcurrentDictionary<uint, ObservedInputsEventData>();

        /// <summary>
        /// Private list that stores the ProcessFingerprintComputationEventData objects that have been replayed from the execution log
        /// </summary>
        private List<ProcessFingerprintComputationEventData> m_listProcessFingerprintComputationEvents = new List<ProcessFingerprintComputationEventData>();

        /// <summary>
        /// Private dictionary that maps pip Ids to PipExecutionPerformanceEventData objects that have been replayed from the execution log
        /// This dictionary is used to determine if a specific pip ran during the build.
        /// </summary>
        private ConcurrentDictionary<uint, PipExecutionPerformanceEventData> m_dictPipExecutionPerformanceEvents = new ConcurrentDictionary<uint, PipExecutionPerformanceEventData>();

        /// <summary>
        /// Private dictionary that stores the FileArtifactContentDecidedEventData objects that have been replayed from the execution log
        /// </summary>
        private ConcurrentDictionary<FileArtifact, FileArtifactContentDecidedEventData> m_dictFileArtifactContentDecidedEvents = new ConcurrentDictionary<FileArtifact, FileArtifactContentDecidedEventData>();

        /// <summary>
        /// DirectoryMembershipHashedEventData objects are handled a little bit differently compared to other execution log events.
        /// Handling DirectoryMembershipHashedEventData event is very time consuming and it requires updating thousands of file and  directory descriptor objects.
        /// In addition, directory descriptors will not be used very often and when they do get used, we want to make sure that they are started to be processed as soon as possible
        /// For this reason, when the execution log events are being replayed we will not add the DirectoryMembershipHashedEventData objects to a list and process them later.
        /// Instead we will start a new task for each DirectoryMembershipHashedEventData object and will wait for these tasks to complete at the very end of the execution log loading process.
        /// This approach seems to be producing some improvement in the execution log loading time. On the other hand, processing other events along with DirectoryMembershipHashedEventData
        /// objects will degrade performance.
        /// </summary>
        private List<Task> m_listDirectoryMembershipHashedEventProcessingTasks = new List<Task>();

        /// <summary>
        /// Static build graph loaded from the execution log
        /// </summary>
        private CachedGraph m_buildGraph;

        /// <summary>
        /// The execution context for the graph
        /// </summary>
        private PipExecutionContext m_buildExecutionContext;

        /// <summary>
        /// Concurrency settings used with Parrallel.ForEach constructs
        /// </summary>
        private ParallelOptions m_parallelOptions = new ParallelOptions();

        /// <summary>
        /// Dictionary used to "remember" the nearest process pip children of non process pips in the build graph.
        /// This collection is used to speed up the filtering of the non process pips from the build graph.
        /// </summary>
        private ConcurrentDictionary<uint, ConcurrentHashSet<NodeId>> m_memoizationChildrensOfNonProcessPips = new ConcurrentDictionary<uint, ConcurrentHashSet<NodeId>>();

        /// <summary>
        /// Private collection that is used to store pip descriptors and provide dictionaries that allow locating pip descriptors based on pip Ids and pip names
        /// </summary>
        private PipStore m_pips = null;

        /// <summary>
        /// Module table used to map module descriptors to module Ids
        /// </summary>
        private Dictionary<ModuleId, ModuleDescriptor> m_dictModuleTable = new Dictionary<ModuleId, ModuleDescriptor>();

        /// <summary>
        /// Specifies what to load from the execution log
        /// </summary>
        private ExecutionLogLoadOptions LoadOptions => m_loadContext.LoadOptions;

        /// <summary>
        /// Stores ExecutionLogLoadOptions along with any filters
        /// </summary>
        private LoadContext m_loadContext;

        /// <summary>
        /// Every process pip id that is filtered out will be stored here
        /// </summary>
        private ConcurrentHashSet<uint> m_pipsToSkip = new ConcurrentHashSet<uint>();

        /// <summary>
        /// Every process pip that passed the pip filter will be stored here
        /// </summary>
        private ConcurrentDictionary<uint, Process> m_pipsToLoad = new ConcurrentDictionary<uint, Process>();

        /// <summary>
        /// Every module id that is filtered out will be stored here
        /// </summary>
        private ConcurrentHashSet<int> m_modulesToSkip = new ConcurrentHashSet<int>();
        #endregion

        #region Public properties

        /// <summary>
        /// Maps pip Ids (unique numeric values assigned to pips by BuildXL) to pip descriptor objects.
        /// The dictionary contains descriptors for all Process pips.
        /// </summary>
        public IReadOnlyDictionary<uint, PipDescriptor> PipIdDictionary { get { return m_pips.PipIdDictionary; } }

        /// <summary>
        /// Maps pip names to pip descriptor objects.
        /// Pip names are not guaranteed to be unique and there may be multiple matching pips for a pip name.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<PipDescriptor>> PipNameDictionary { get { return m_pips.PipNameDictionary; } }

        /// <summary>
        /// Maps file names (with full path) to file descriptor objects.
        /// The dictionary contains descriptors for all files that have been accessed during the build.
        /// </summary>
        public IReadOnlyDictionary<string, FileDescriptor> FileDescriptorDictionary { get { return m_fileDescriptorDictionary; } }

        /// <summary>
        /// Maps directory paths to directory descriptor objects.
        /// The dictionary contains descriptors for all directories that have been accessed during the build.
        /// </summary>
        public IReadOnlyDictionary<string, DirectoryDescriptor> DirectoryDescriptorDictionary { get { return m_directoryDescriptorDictionary; } }

        /// <summary>
        /// Maps process executables (with full path) to process descriptor objects.
        /// The dictionary contains descriptors for all executables that have been launched as part of the build.
        /// </summary>
        public IReadOnlyDictionary<string, ProcessDescriptor> ReportedProcessesDictionary { get { return m_reportedProcessesDictionary; } }

        /// <summary>
        /// Maps file names (with full path) to reported file access descriptor objects.
        /// The dictionary contains descriptors for all files accesses that have been reported during the build.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<ReportedFileAccessDescriptor>> ReportedFileAccessesDictionary { get { return m_reportedFileAccessesDictionary; } }

        /// <summary>
        /// Maps environment variable names to environment variable descriptor objects.
        /// The dictionary contains descriptors for all environment variables that were present in the running environments of at least one pips.
        /// </summary>
        public IReadOnlyDictionary<string, EnvironmentVariableDescriptor> EnvironmentVariablesDictionary { get { return m_environmentVariablesDictionary; } }
        #endregion

        #region Constructor

        /// <summary>
        /// Private constructor. Use ExecutionLog.LoadExecutionLog to instantiate objects.
        /// </summary>
        /// <param name="loadOptions">Specifies what to load from the execution log</param>
        private ExecutionLog(ExecutionLogLoadOptions loadOptions = ExecutionLogLoadOptions.LoadPipDataBuildGraphAndPipPerformanceData)
        {
            ContentHashingUtilities.SetDefaultHashType();
            this.m_loadContext = new LoadContext(loadOptions);

            // Set the max concurrency level to the current processor count (used by Parallel.ForEach constructs).
            m_parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;
        }

        /// <summary>
        /// Private constructor. Use ExecutionLog.LoadExecutionLog to instantiate objects.
        /// </summary>
        /// <param name="loadContext">Specifies what to load from the execution log as well as any filters to use</param>
        private ExecutionLog(LoadContext loadContext)
        {
            ContentHashingUtilities.SetDefaultHashType();
            this.m_loadContext = loadContext;

            // Set the max concurrency level to the current processor count (used by Parallel.ForEach constructs).
            m_parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Loads an execution log and returns a new instance of an ExecutionLog object containing the loaded data.
        /// </summary>
        /// <param name="executionLogFile">The name of the execution log file with full path (i.e. c:\BuildXLLogPath\buildxl.1.xlg)</param>
        /// <param name="loadOptions">Specifies what to load from the execution log</param>
        /// <exception cref="ArgumentException">The specified XLG execution log file does not exist
        /// or the matching folder containing the build graph does not exist</exception>
        /// <exception cref="InvalidDataException">The specified XLG file is invalid or it contains unsupported data</exception>
        /// <returns>ExecutionLog log object containing the loaded data</returns>
        /// <remarks>
        /// ExecutionLogLoadOptions.LoadBuildGraph:
        ///     When not enabled in loadOptions the following object properties will not get loaded:
        ///         PipDescriptor::TransitiveDependentPips
        ///         PipDescriptor::CriticalPathLength
        ///         PipDescriptor::CriticalPath
        ///         PipDescriptor::AdjacentInNodes
        ///         PipDescriptor::AdjacentOutNodes
        ///         PipDescriptor::CriticalPathBasedOnNumberOfPipsProducedFromCache
        ///         PipDescriptor::NumberOfFilesProducedFromCacheOnCriticalPath
        /// ExecutionLogLoadOptions.LoadPipExecutionPerformanceData:
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         PipDescriptor::PipExecutionPerformance
        ///         PipDescriptor::WasExecuted
        ///         PipDescriptor::WasItDeployedFromCache
        ///         PipDescriptor::WasItProducedDuringTheBuild
        ///         PipDescriptor::HasFailed
        ///         PipDescriptor::WasItUpToDate
        ///         PipDescriptor::WasItBuilt
        ///         PipDescriptor::CriticalPathBasedOnNumberOfPipsProducedFromCache
        ///         PipDescriptor::NumberOfFilesProducedFromCacheOnCriticalPath
        ///         PipDescriptor::CriticalPathLength
        ///         PipDescriptor::CriticalPath
        /// ExecutionLogLoadOptions.LoadFileHashValues:
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         FileDescriptor::ContentHash
        ///         FileDescriptor::ContentLength
        ///         FileDescriptor::RewriteCount
        /// ExecutionLogLoadOptions.LoadProcessFingerprintComputations:
        ///         PipDescriptor::Fingerprint
        ///         PipDescriptor::FingerprintKind
        ///         PipDescriptor::StrongFingerprintComputations
        /// ExecutionLogLoadOptions.LoadObservedInputs:
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         ExecutionLog::FileDescriptorDictionary (will not include files with observed file accesses)
        ///         FileDescriptor::IsObservedInputFile
        ///         FileDescriptor::ContentHash
        ///         FileDescriptor::DependentPips (will not include files with observed file accesses)
        ///         PipDescriptor::ObservedInputs
        /// ExecutionLogLoadOptions.LoadProcessMonitoringData:
        ///     BuildXL only logs these events when the /logProcesses flag is specified during the build.
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         ProcessDescriptor::PipsThatExecuteTheProcessDictionary
        ///         ExecutionLog::ReportedProcessesDictionary
        ///         PipDescriptor::ReportedProcesses
        /// ExecutionLogLoadOptions.LoadDirectoryMemberships:
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         ExecutionLog::DirectoryDescritporDictionary will not include any directories
        ///         ExecutionLog::FileDescriptorDictionary will only include files that are loaded from static pip data, from observed file inputs or had file hashes reported through FileArtifactContentDecided events.
        ///         DirectoryDescriptor::Files
        ///         FileDescriptor::DirectoriesThatContainThisFile
        /// ExecutionLogLoadOptions.LoadDirectoryMembershipsForUnusedFiles:
        ///     This flag affects the same properties as loadDirectoryMemberships.
        /// ExecutionLogLoadOptions.LoadReportedFileAccesses
        ///     When not enabled in loadOptions, the following object properties will not get loaded:
        ///         ExecutionLog::ReportedFileAccessesDictionary
        /// ExecutionLogLoadOptions.LoadExecutedPipsOnly
        ///     Will only load data for pips that ran during the build.
        /// ExecutionLogLoadOptions.DoNotLoadRarelyUsedPipProperties
        ///     When specified, the following PipDescriptor properties will not be loaded:
        ///          PipDescriptor::Usage
        ///          PipDescriptor::SpecFile
        ///          PipDescriptor::Arguments
        ///          PipDescriptor::StandardInputFile
        ///          PipDescriptor::StandardInputData
        ///          PipDescriptor::StandardError
        ///          PipDescriptor::StandardDirectory
        ///          PipDescriptor::HasUntrackedChildProcesses
        ///          PipDescriptor::WarningTimeout
        ///          PipDescriptor::Timeout
        ///          PipDescriptor::ResponseFile
        ///          PipDescriptor::ResponseFileData
        ///          PipDescriptor::WarningRegexPattern
        ///          PipDescriptor::WarningRegexOptions
        ///          PipDescriptor::SuccessExitCodesHashset
        ///          PipDescriptor::ToolName
        ///          PipDescriptor::ToolDescription
        ///          PipDescriptor::UntrackedPathsHashset
        ///          PipDescriptor::UntrackedScopesHashset
        ///          PipDescriptor::EnvironmentVariables
        /// ExecutionLogLoadOptions.DoNotLoadSourceFiles
        ///     When set, there will be no FileDescriptor object created and added to ExecutionLog::FileDescriptorDictionary for source files.
        ///     In addition, the following objects will NOT include references to source files.
        ///          PipDescriptor::DependentFiles
        ///          PipDescriptor::ObservedInputs
        ///          DirectoryDescriptor::Files
        ///          PipDescriptor::ProbedFiles
        /// ExecutionLogLoadOptions.DoNotLoadOutputFiles
        ///     When set, there will be no FileDescriptor object created and added to ExecutionLog::FileDescriptorDictionary for output files.
        ///     In addition, the following objects will NOT include references to output files.
        ///          PipDescriptor::OutputFiles
        ///          DirectoryDescriptor::Files
        ///
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We are returning executionLogObject - cannot Dispose it.")]
        public static ExecutionLog LoadExecutionLog(
            string executionLogFile,
            ExecutionLogLoadOptions loadOptions = ExecutionLogLoadOptions.LoadPipDataBuildGraphAndPipPerformanceData)
        {
            // BuildXL requires a full path
            executionLogFile = MakeFullPath(executionLogFile);

            // instantiate new ExecutionLog object
            ExecutionLog executionLogObject = new ExecutionLog(loadOptions);

            // Load the execution log and populate the ExecutionLog object with the data from the execution log
            executionLogObject.Initialize(executionLogFile);

            // return ExecutionLog object
            return executionLogObject;
        }

        /// <summary>
        /// Loads an execution log and returns a new instance of an ExecutionLog object containing the loaded data.
        /// </summary>
        /// <param name="executionLogFile">The execution log to load with full path</param>
        /// <param name="loadOptions">Specifies what to load from the execution log</param>
        /// <param name="fileFilters">List of file prefixes used to specify which files to load.</param>
        /// <param name="excludeFilter">When true, the file filters are exclude filters and files with names that start with any of the prefixes that
        /// are listed in fileFilters will not be loaded. When false, the file filters are include filters and only files that start with at least
        /// one prefix listed will be loaded
        /// </param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We are returning executionLogObject - cannot Dispose it.")]
        public static ExecutionLog LoadExecutionLog(
            string executionLogFile,
            ExecutionLogLoadOptions loadOptions,
            IEnumerable<string> fileFilters,
            bool excludeFilter)
        {
            // BuildXL requires a full path
            executionLogFile = MakeFullPath(executionLogFile);

            // instantiate new ExecutionLog object
            LoadContext loadContext = new LoadContext(loadOptions);
            loadContext.AddFileFilter(new FileFilter(fileFilters, excludeFilter));
            ExecutionLog executionLogObject = new ExecutionLog(loadContext);

            // Load the execution log and populate the ExecutionLog object with the data from the execution log
            executionLogObject.Initialize(executionLogFile);

            // return ExecutionLog object
            return executionLogObject;
        }

        /// <summary>
        /// Loads an execution log and returns a new instance of an ExecutionLog object containing the loaded data.
        /// </summary>
        /// <param name="executionLogFile">The execution log to load with full path</param>
        /// <param name="loadContext">Specifies which data will get loaded along with any filters to use</param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We are returning executionLogObject - cannot Dispose it.")]
        public static ExecutionLog LoadExecutionLog(
            string executionLogFile,
            LoadContext loadContext)
        {
            // BuildXL requires a full path
            executionLogFile = MakeFullPath(executionLogFile);

            // instantiate new ExecutionLog object
            ExecutionLog executionLogObject = new ExecutionLog(loadContext);

            // Load the execution log and populate the ExecutionLog object with the data from the execution log
            executionLogObject.Initialize(executionLogFile);

            // return ExecutionLog object
            return executionLogObject;
        }

        /// <summary>
        /// Converts an AbsolutePath to a full path string
        /// </summary>
        /// <param name="path">The AbsolutPath to convert</param>
        /// <returns>A string containing the full path that the specified AbsolutePath object represents.</returns>
        public string AbsolutePathToPathString(AbsolutePath path)
        {
            return m_buildExecutionContext.PathTable.AbsolutePathToString(path);
        }

        /// <summary>
        /// This method is a "back door" to allow unit tests to instantiate ExecutionLog objects without loading an actual execution log.
        /// </summary>
        /// <param name="pipGraph">Pip graph object containing pips to load</param>
        /// <param name="context">BuildXL context containing path, string and symbols</param>
        /// <param name="executionLogFileStream">Stream that contains the execution log events</param>
        /// <param name="loadOptions">Specifies what to load from the execution log</param>
        /// <exception cref="InvalidDataException">The specified XLG file is invalid or it contains unsupported data</exception>
        /// <returns>New ExecutionLog object instance</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We are returning executionLogObject - cannot Dispose it.")]
        internal static ExecutionLog Create(PipGraph pipGraph, BuildXLContext context, string executionLogFilename,
                                            ExecutionLogLoadOptions loadOptions = ExecutionLogLoadOptions.LoadPipDataBuildGraphAndPipPerformanceData,
                                            IEnumerable<string> fileFilters = null,
                                            bool excludeFilter = true)
        {
            // instantiate new ExecutionLog object
            LoadContext loadContext = new LoadContext(loadOptions);
            if (fileFilters != null)
            {
                IFileFilter fileFilter = new FileFilter(fileFilters, excludeFilter);
                loadContext.AddFileFilter(fileFilter);
            }

            ExecutionLog executionLogObject = new ExecutionLog(loadContext);

            // initialize ExecutionLog object
            executionLogObject.Initialize(pipGraph, context, executionLogFilename);

            // return ExecutionLog object
            return executionLogObject;
        }

        /// <summary>
        /// This method is a "back door" to allow unit tests to instantiate ExecutionLog objects without loading an actual execution log.
        /// </summary>
        /// <param name="pipGraph">Pip graph object containing pips to load</param>
        /// <param name="context">BuildXL context containing path, string and symbols</param>
        /// <param name="executionLogFileStream">Stream that contains the execution log events</param>
        /// <param name="loadContext">Specifies what to load from the execution log along with any filters to use</param>
        /// <exception cref="InvalidDataException">The specified XLG file is invalid or it contains unsupported data</exception>
        /// <returns>New ExecutionLog object instance</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We are returning executionLogObject - cannot Dispose it.")]
        internal static ExecutionLog Create(PipGraph pipGraph, BuildXLContext context, string executionLogFilename,
                                            LoadContext loadContext)
        {
            // instantiate new ExecutionLog object
            ExecutionLog executionLogObject = new ExecutionLog(loadContext);

            // initialize ExecutionLog object
            executionLogObject.Initialize(pipGraph, context, executionLogFilename);

            // return ExecutionLog object
            return executionLogObject;
        }
        #endregion

        #region Initialization

        /// <summary>
        /// Initializes a new ExecutionLog object with data from the specified BuildXL execution log file
        /// </summary>
        /// <param name="executionLogPath">The name of the execution log file with full path (i.e. c:\BuildXLLogPath\buildxl.1.xlg) or the name of a folder that contains the build graph and pip data</param>
        /// <exception cref="ArgumentException">The specified XLG execution log file does not exist
        /// or the matching folder containing the build graph does not exist</exception>
        /// <exception cref="InvalidDataException">The specified XLG file is invalid or it contains unsupported data</exception>
        /// <exception cref="InvalidOperationException">The execution log folder does not contain a valid build grap and pip table</exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "ExecutionLogStream is Disposed by LoadExecutionLogData called by LoadAllExecutionLogData.")]
        private void Initialize(string executionLogPath)
        {
            Contract.Requires(Path.IsPathRooted(executionLogPath));

            string executionLogFolderPath;

            if (!executionLogPath.EndsWith(".xlg", StringComparison.OrdinalIgnoreCase) &&
                                (
                                    (LoadOptions & (
                                                       ExecutionLogLoadOptions.LoadPipExecutionPerformanceData |
                                                       ExecutionLogLoadOptions.LoadFileHashValues |
                                                       ExecutionLogLoadOptions.LoadObservedInputs |
                                                       ExecutionLogLoadOptions.LoadProcessMonitoringData |
                                                       ExecutionLogLoadOptions.LoadDirectoryMemberships |
                                                       ExecutionLogLoadOptions.LoadExecutedPipsOnly)) == 0))
            {
                // there is no need to load the XLG file. We should only require that a valid folder is passed in containing the pip data and build graph instead of a path to an XLG
                // this option is needed in order to support the bxl.exe "/phase:schedule" option that does not generate an XLG.
                executionLogFolderPath = executionLogPath;

                // Validate the execution log folder name
                if (!Directory.Exists(executionLogFolderPath))
                {
                    throw new ArgumentException("Execution log folder '" + executionLogFolderPath + "' cannot be found.");
                }
            }
            else
            {
                // A valid XLG filename is required with the current load options. If executionLogPath is a valid folder, we should fail.
                if (Directory.Exists(executionLogPath))
                {
                    throw new ArgumentException("The current load options do not allow loading an execution log folder. Please specify an XLG file name instead.");
                }

                // Validate input execution log filename
                if (!File.Exists(executionLogPath))
                {
                    throw new ArgumentException("Execution log file '" + executionLogPath + "' cannot be found.");
                }

                // Each execution log contains an XLG file that contains the runtime execution events collected during the build and also a folder that contains
                // the build graph and the pip data. The name of the folder is the same as the name of the XLG file without the extension.
                executionLogFolderPath = Path.Combine(Path.GetDirectoryName(executionLogPath), Path.GetFileNameWithoutExtension(executionLogPath));

                // Validate the execution log folder name
                if (!Directory.Exists(executionLogFolderPath))
                {
                    throw new ArgumentException("Execution log folder '" + executionLogFolderPath + "' cannot be found.");
                }
            }

            // Create logging context required by CachedGraph.LoadAsync
            LoggingContext loggingContext = new LoggingContext("Tools.ExecutionLog.Sdk");

            // First we have to load the build graph and other static execution log data. There is nothing else we can do at this point, therefore
            // we will wait for the results here without launching any other tasks.
            m_buildGraph =
                CachedGraph.LoadAsync(executionLogFolderPath, loggingContext, preferLoadingEngineCacheInMemory: true)
                    .GetAwaiter()
                    .GetResult();

            if (m_buildGraph == null)
            {
                throw new InvalidOperationException(
                    "Invalid execution log data: Failed to load the build graph and the pip table. Make sure that the version of the Execution Log SDK binaries that are being used to load the execution log match the version of the binaries that have been used to create the execution log.");
            }

            m_buildExecutionContext = m_buildGraph.Context;

            m_fileDescriptorDictionary = new AbsolutePathConcurrentDictionary<FileDescriptor>(m_buildGraph.Context.PathTable);
            m_directoryDescriptorDictionary = new AbsolutePathConcurrentDictionary<DirectoryDescriptor>(m_buildGraph.Context.PathTable);
            m_reportedFileAccessesDictionary = new AbsolutePathConcurrentDictionary<IReadOnlyCollection<ReportedFileAccessDescriptor>>(m_buildGraph.Context.PathTable);
            m_environmentVariablesDictionary = new StringIdConcurrentDictionary<EnvironmentVariableDescriptor>(m_buildGraph.Context.StringTable);
            m_pips = new PipStore(m_buildGraph.Context.SymbolTable);

            if ((LoadOptions & (
                      ExecutionLogLoadOptions.LoadPipExecutionPerformanceData |
                      ExecutionLogLoadOptions.LoadFileHashValues |
                      ExecutionLogLoadOptions.LoadObservedInputs |
                      ExecutionLogLoadOptions.LoadProcessMonitoringData |
                      ExecutionLogLoadOptions.LoadDirectoryMemberships |
                      ExecutionLogLoadOptions.LoadExecutedPipsOnly)) != 0)
            {
                LoadAllExecutionLogData(executionLogPath);
            }
            else
            {
                // we do not need to load the XLG. The method that loads the xlg can handle null
                LoadAllExecutionLogData(null);
            }
        }

        /// <summary>
        /// Initializes a new ExecutionLog object with data from the specified BuildXL execution log stream.
        /// </summary>
        /// <param name="executionLogStream">The execution log stream to load execution log events from</param>
        /// <exception cref="ArgumentException">The specified XLG execution log file does not exist
        /// or the matching folder containing the build graph does not exist</exception>
        /// <exception cref="InvalidDataException">The specified XLG file is invalid or it contains unsupported data</exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "ExecutionLogStream is Disposed by LoadExecutionLogData.")]
        private void LoadAllExecutionLogData(string executionLogPath)
        {
            // stopwatch objects to measure execution times of various initialization steps
            System.Diagnostics.Stopwatch swLogInitialize = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swLogReaderLoadData = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swExecutionEventReplay = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swFileArtifactContentDecidedEventReplay =
                new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swLoadPipData = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swProcessExecutionMonitoringEventReplay =
                new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swObservedInputsEventReplay = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swPipExecutionPerformanceEventReplay = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swDirectoryMembershipEventReplay = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch swlProcessFingerprintComputationEventReplay = new System.Diagnostics.Stopwatch();

            PipQueryContext pipQueryContext = default(PipQueryContext);

            try
            {
                // Start timer to measure the total initialization time
                swLogInitialize.Start();

                // Build module table
                foreach (var pipId in m_buildGraph.PipTable.Keys.Where(p => m_buildGraph.PipTable.GetPipType(p) == PipType.Module))
                {
                    // In order to add it we need the pip name and for that we have to hydrate the pip.
                    ModulePip fullPip = (ModulePip)m_buildGraph.PipTable.HydratePip(pipId, pipQueryContext);

                    if (m_loadContext.ShouldLoadModule(fullPip, m_buildGraph))
                    {
                        // Add the module pip to the module table
                        if (fullPip.Module.IsValid && !m_dictModuleTable.ContainsKey(fullPip.Module))
                        {
                            if (fullPip.Identity.IsValid && fullPip.Location.IsValid)
                            {
                                m_dictModuleTable.Add(
                                    fullPip.Module,
                                    new ModuleDescriptor(
                                        fullPip.Module.Value.Value,
                                        fullPip.Identity,
                                        fullPip.Location.Path,
                                        m_buildGraph.Context));
                            }
                        }
                    }
                    else
                    {
                        m_modulesToSkip.Add(fullPip.Module.Value.Value);
                    }
                }

                if (m_loadContext.HasModuleFilters || m_loadContext.HasPipFilters)
                {
                    // Hydrate all the process pips so we can decide which data we actually need to load
                    foreach (var pipId in m_buildGraph.PipTable.Keys.Where(p => m_buildGraph.PipTable.GetPipType(p) == PipType.Process))
                    {
                        Process fullPip = (Process)m_buildGraph.PipTable.HydratePip(pipId, pipQueryContext);

                        if (m_modulesToSkip.Contains(fullPip.Provenance.ModuleId.Value.Value))
                        {
                            m_pipsToSkip.Add(pipId.Value);
                        }
                        else if (m_loadContext.ShouldLoadPip(fullPip, m_buildGraph))
                        {
                            m_pipsToLoad.TryAdd(pipId.Value, fullPip);
                        }
                        else
                        {
                            m_pipsToSkip.Add(pipId.Value);
                        }
                    }
                }

                // Load the execution log events
                swExecutionEventReplay.Start();

                // The processing of directory membership events is being done while the execution log events are being replayed, therefore we have to start the timer here.
                swDirectoryMembershipEventReplay.Start();

                // LoadExecutionLogData can throw InvalidDataException exceptions
                LoadExecutionLogData(executionLogPath).Wait();
                swExecutionEventReplay.Stop();

                if ((LoadOptions & ExecutionLogLoadOptions.LoadFileHashValues) != 0)
                {
                    // Process all FileArtifactContentDecidedEvents events
                    swFileArtifactContentDecidedEventReplay.Start();
                    System.Diagnostics.Trace.WriteLine(
                        "==== ProcessFileArtifactContentDecidedEvents: " +
                        m_dictFileArtifactContentDecidedEvents.Count.ToString(CultureInfo.InvariantCulture));
                    ProcessFileArtifactContentDecidedEvents().Wait();
                    swFileArtifactContentDecidedEventReplay.Stop();
                }

                // Process pip execution performance events
                // When LoadExecutedPipsOnly is enabled these event have to be loaded while the execution log events are being replayed
                swPipExecutionPerformanceEventReplay.Start();
                if ((LoadOptions & ExecutionLogLoadOptions.LoadPipExecutionPerformanceData) != 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "==== PipExecutionPerformanceEvents:  " +
                        m_dictFileArtifactContentDecidedEvents.Count.ToString(CultureInfo.InvariantCulture));
                    ProcessPipExecutionPerformanceEvents().Wait();
                }

                swPipExecutionPerformanceEventReplay.Stop();

                // Handle process execution monitoring events
                swProcessExecutionMonitoringEventReplay.Start();
                if ((LoadOptions & ExecutionLogLoadOptions.LoadProcessMonitoringData) != 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "==== ProcessProcessExecutionMonitoringReportedEvents: " +
                        m_listExecutionMonitoringReportedEvents.Count.ToString(CultureInfo.InvariantCulture));
                    ProcessProcessExecutionMonitoringReportedEvents().Wait();
                }

                swProcessExecutionMonitoringEventReplay.Stop();

                // Process observed inputs events
                swObservedInputsEventReplay.Start();
                if ((LoadOptions & ExecutionLogLoadOptions.LoadObservedInputs) != 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "==== ObservedInputsEventsEvents:  " +
                        m_dictObservedInputsEvents.Count.ToString(CultureInfo.InvariantCulture));
                    ProcessAllObservedInputsEvents().Wait();
                }

                swObservedInputsEventReplay.Stop();

                // Process fingerprint computation events
                swlProcessFingerprintComputationEventReplay.Start();
                if ((LoadOptions & ExecutionLogLoadOptions.LoadProcessFingerprintComputations) != 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "==== FingerprintComputationEvents:  " +
                        m_listProcessFingerprintComputationEvents.Count.ToString(CultureInfo.InvariantCulture));
                    ProcessAllFingerprintComputationEvents().Wait();
                }

                swlProcessFingerprintComputationEventReplay.Stop();

                // Now we can load the pip data
                swLoadPipData.Start();
                LoadPipData(m_buildGraph.PipTable, m_buildGraph.DataflowGraph).Wait();
                swLoadPipData.Stop();

                // Wait for the directory membership tasks that we launched when we replayed the execution log events to finish.
                if ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "==== AllDirectoryMembershipHashedEvents:  " +
                        m_listDirectoryMembershipHashedEventProcessingTasks.Count
                            .ToString(CultureInfo.InvariantCulture));
                    Task.WaitAll(m_listDirectoryMembershipHashedEventProcessingTasks.ToArray());
                }

                swDirectoryMembershipEventReplay.Stop();

                swLogInitialize.Stop();
            }
            catch (AggregateException ex)
            {
                // Flatten aggregate exceptions and re-throw each InnerException one by one.
                // This will simplify error handling for the User
                foreach (var e in ex.Flatten().InnerExceptions)
                {
                    throw e;
                }
            }
            finally
            {
                // We can clear all objects that we used to load data from
                m_listExecutionMonitoringReportedEvents.Clear();
                m_dictObservedInputsEvents.Clear();
                m_listProcessFingerprintComputationEvents.Clear();
                m_listDirectoryMembershipHashedEventProcessingTasks.Clear();
                m_dictPipExecutionPerformanceEvents.Clear();
                m_dictFileArtifactContentDecidedEvents.Clear();
                m_memoizationChildrensOfNonProcessPips.Clear();
                m_pipsToSkip.Clear();
                m_pipsToLoad.Clear();
                m_modulesToSkip.Clear();

                m_listExecutionMonitoringReportedEvents = null;
                m_dictObservedInputsEvents = null;
                m_listProcessFingerprintComputationEvents = null;
                m_listDirectoryMembershipHashedEventProcessingTasks = null;
                m_dictPipExecutionPerformanceEvents = null;
                m_dictFileArtifactContentDecidedEvents = null;
                m_memoizationChildrensOfNonProcessPips = null;
                m_pipsToSkip = null;
                m_pipsToLoad = null;
                m_modulesToSkip = null;

                m_buildGraph = null;

                // Dump measured execution times
                System.Diagnostics.Trace.WriteLine("===================================================================================================");
                System.Diagnostics.Trace.WriteLine("==== Time to load execution log data: " + swLogReaderLoadData.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to load pip data: " + swLoadPipData.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to replay execution events: " + ((executionLogPath != null) ? swExecutionEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) : "0") + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to process FileArtifactContentDecided events: " + swFileArtifactContentDecidedEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to process ProcessExecutionMonitoringReported events: " + swProcessExecutionMonitoringEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to process ObservedInputsHashset events: " + swObservedInputsEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to process DirectoryMembershipHashed events: " + (((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0) ? swDirectoryMembershipEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) : "0") + "ms");
                System.Diagnostics.Trace.WriteLine("==== Time to process PipExecutionPerformance events: " + swPipExecutionPerformanceEventReplay.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("==== Total initialization time: " + swLogInitialize.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
                System.Diagnostics.Trace.WriteLine("===================================================================================================");
                System.Diagnostics.Trace.WriteLine("==== Pip count: " + m_pips.Count.ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("==== File count: " + m_fileDescriptorDictionary.Count.ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("==== Directory count: " + m_directoryDescriptorDictionary.Count.ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("==== Process count: " + m_reportedProcessesDictionary.SelectMany(p => p.Value.PipsThatExecuteTheProcess).SelectMany(p => p.Value).Count().ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("==== Reported file accesses count: " + m_reportedFileAccessesDictionary.SelectMany(rfa => rfa.Value).Count().ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("==== Environment variable count: " + m_environmentVariablesDictionary.Count.ToString(CultureInfo.InvariantCulture));
                System.Diagnostics.Trace.WriteLine("===================================================================================================");
            }
        }

        /// <summary>
        /// This method is a "backdoor" to allow unit tests to instantiate ExecutionLog objects without loading an actual execution log.
        /// DO NOT USE THIS METHOD!
        /// </summary>
        /// <param name="pipGraph">Pip graph object containing pips to load</param>
        /// <param name="context">BuildXL context containing path, string and symbols</param>
        /// <param name="executionLogFileStream">Stream that contains the execution log events</param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "ExecutionLogStream is Disposed by LoadExecutionLogData called by LoadAllExecutionLogData.")]
        private void Initialize(PipGraph pipGraph, BuildXLContext context, string executionLogFilename)
        {
            Contract.Requires(pipGraph != null, "pipGraph parameter is null");
            Contract.Requires(context != null, "context parameter is null");
            Contract.Requires(executionLogFilename != null, "executionLogFilename is null");

            // Load build graph
            m_buildGraph = new CachedGraph(pipGraph, pipGraph.DataflowGraph, context, new MountPathExpander(context.PathTable));
            m_buildExecutionContext = m_buildGraph.Context;
            m_fileDescriptorDictionary = new AbsolutePathConcurrentDictionary<FileDescriptor>(m_buildGraph.Context.PathTable);
            m_directoryDescriptorDictionary = new AbsolutePathConcurrentDictionary<DirectoryDescriptor>(m_buildGraph.Context.PathTable);
            m_reportedFileAccessesDictionary = new AbsolutePathConcurrentDictionary<IReadOnlyCollection<ReportedFileAccessDescriptor>>(m_buildGraph.Context.PathTable);
            m_environmentVariablesDictionary = new StringIdConcurrentDictionary<EnvironmentVariableDescriptor>(m_buildGraph.Context.StringTable);
            m_pips = new PipStore(m_buildGraph.Context.SymbolTable);

            LoadAllExecutionLogData(executionLogFilename);
        }
        #endregion

        #region Private methods

        /// <summary>
        /// BuildXL requires that the path specified for either the xlg file or
        /// directory be a full path. This method returns the full path version
        /// of the provided path.
        /// </summary>
        /// <param name="relativeOrFullPath">The path to make full</param>
        /// <returns>Full path version of input argument</returns>
        private static string MakeFullPath(string relativeOrFullPath)
        {
            try
            {
                if (!Path.IsPathRooted(relativeOrFullPath))
                {
                    return Path.GetFullPath(relativeOrFullPath);
                }
                else
                {
                    return relativeOrFullPath;
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(string.Format(
                    CultureInfo.InvariantCulture,
                    "There was a problem with the provided path '{0}': {1}", relativeOrFullPath, e.GetLogEventMessage()));
            }
        }
        #endregion

        #region Execution log event loading

        /// <summary>
        /// Replays all events in an execution log (XLG) file
        /// </summary>
        /// <param name="executionLogStream">Stream to read the execution log data from (null is allowed. When null the method will not load anything)</param>
        /// <exception cref="InvalidDataException">Thrown when the execution log does not contain
        /// a valid LogId or when the LogId does not match the Graph Id from the build graph.</exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "ExecutionLogStream is Disposed by FilteredExecutionLogFileReader.")]
        [SuppressMessage("Microsoft.Naming", "CA2204: Correct the spelling of the unrecognized token",
            Justification = "LogId and GraphId are known tokens")]
        private Task LoadExecutionLogData(string executionLogPath)
        {
            Contract.Requires((executionLogPath == null) || !string.IsNullOrEmpty(executionLogPath), "Invalid execution log file name.");

            return Task.Run(() =>
            {
                if ((executionLogPath != null) && ((LoadOptions & (ExecutionLogLoadOptions.LoadPipExecutionPerformanceData |
                                      ExecutionLogLoadOptions.LoadFileHashValues |
                                      ExecutionLogLoadOptions.LoadObservedInputs |
                                      ExecutionLogLoadOptions.LoadProcessMonitoringData |
                                      ExecutionLogLoadOptions.LoadDirectoryMemberships) |
                                      ExecutionLogLoadOptions.LoadExecutedPipsOnly) != 0))
                {
                    // Create filtered execution log reader instance
                    using (var reader = new FilteredExecutionLogFileReader(
                        executionLogPath,
                        m_buildGraph.Context,
                        this,
                        LoadOptions))
                    {
                        // Make sure that the execution log reader can handle the data in the current execution log
                        if (!reader.LogId.HasValue || (reader.LogId.Value != m_buildGraph.PipGraph.GraphId))
                        {
                            throw new InvalidDataException(
                                "Invalid execution log file: The specified file is not a valid BuildXL execution log.");
                        }

                        // Read all events sequentially, return result
                        if (!reader.ReadAllEvents())
                        {
                            throw new InvalidDataException(
                                "Invalid execution log data: Failed to replay all execution log data events. Make sure that the version of the Execution Log SDK binaries that are being used to load the execution log match the version of the binaries that have been used to create the execution log.");
                        }
                    }
                }
            });
        }
        #endregion

        #region Pip data loading

        /// <summary>
        /// Enumerates all process pips and instantiates a pip descriptor for each of them
        /// </summary>
        /// <param name="pipTable">The pip table containing all the pips</param>
        /// <param name="dataflowGraph">Build graph that describes the dependencies between the pips</param>
        /// <returns>Returns a Task that nnumerates all process pips and instantiates a pip descriptor for each of them</returns>
        private Task LoadPipData(PipTable pipTable, DirectedGraph dataflowGraph)
        {
            // we verify the input arguments inside LoadPip
            return Task.Run(() =>
            {
                // Enumerate all Process pips in parallel. Use a partitioner to improve performance.
                Parallel.ForEach(Partitioner.Create(pipTable.Keys.Where(p => pipTable.GetPipType(p) == PipType.Process)), m_parallelOptions, pipId =>
                {
                    // Create a PipDescriptor and populate it with data from the current pip
                    LoadPip(pipTable, dataflowGraph, pipId);
                });

                // When LoadBuildGraph is set, connect the pip to neighboring pips within the build graph
                // We do this in a separate loop after every pip has been initialized because ConnectNeighbourPips may call LoadPip
                // to initialize pips that are not in the pip store yet. A thread that supposed to load a single pip
                // may load several pips and we will end up doing other thread's work. As a result some threads will
                // be launched to load a pip, but will do nothing because other threads already loaded that pip.
                if ((LoadOptions & ExecutionLogLoadOptions.LoadBuildGraph) != 0)
                {
                    // Enumerate all Process pips in parallel. Use a partitioner to improve performance.
                    // At this point all the pips that had to be loaded are stored in m_pips.
                    // We will only process the pips from the pip table that are already in m_Pips.
                    Parallel.ForEach(
                    Partitioner.Create(pipTable.Keys.Where(p => (pipTable.GetPipType(p) == PipType.Process) && m_pips.ContainsPipId(p.Value))),
                    m_parallelOptions,
                    pipId =>
                    {
                        // Create a PipDescriptor and populate it with data from the current pip
                        PipDescriptor pip = m_pips[pipId.Value];

                        ConnectNeighbourPips(pipTable, dataflowGraph, pip);
                    });
                }
            });
        }

        /// <summary>
        /// Checks if a pip should be loaded or not.
        /// Pips can be filtered out with the LoadExecutedPipsOnly load option or with module and pip filters
        /// </summary>
        /// <param name="pipId">The pipId of the pip to check</param>
        /// <returns>true if the pip should be loaded, false otherwise</returns>
        private bool ShouldPipBeLoaded(uint pipId)
        {
            // If the user supplied a filter that filtered this pip out
            if (m_pipsToSkip.Contains(pipId))
            {
                return false;
            }

            // A pip is filtered out when LoadExecutedPipsOnly is set and the pip does not have a m_dictPipExecutionPerformanceEvents.
            if (((LoadOptions & ExecutionLogLoadOptions.LoadExecutedPipsOnly) == ExecutionLogLoadOptions.LoadExecutedPipsOnly) &&
                    !m_dictPipExecutionPerformanceEvents.ContainsKey(pipId))
            {
                // Pip is filtered out because it did not run during the build.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Instantiates a new PipDescriptor and populates the object with data from the pip that is identified by a pip Id
        /// </summary>
        /// <param name="pipTable">Pip table containing all BuildXL pips</param>
        /// <param name="dataflowGraph">Build graph that links various pips</param>
        /// <param name="pipId">The pip Id of the pip that the method will load</param>
        /// <returns>PipDescriptor object containing the loaded pip data</returns>
        private PipDescriptor LoadPip(PipTable pipTable, DirectedGraph dataflowGraph, PipId pipId)
        {
            // TODO: This method is a little bit "spaghetti" like, consider breaking it up.
            Contract.Requires(pipTable != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires((pipId != null) && pipId.IsValid);

            // Check if this pip should be loaded or not. Return null if the pip will not be loaded.
            if (!ShouldPipBeLoaded(pipId.Value))
            {
                return null;
            }

            // Create pip query context. This is needed when "hydrating" the pip
            PipQueryContext pipQueryContext = default(PipQueryContext);

            // Object to store the "hydrated" pip data.
            Process fullPip = null;
            PipDescriptor pip;

            // Check if we already have this pip in the pip store
            if (!m_pips.SynchronizedTryGetValue(pipId.Value, out pip))
            {
                // We do not have this pip in the pip store, lets add it
                // The pip should have been hydrated previously. If not, we hydrate it
                if (!m_pipsToLoad.TryGetValue(pipId.Value, out fullPip))
                {
                    fullPip = (Process)pipTable.HydratePip(pipId, pipQueryContext);
                }

                if (!m_loadContext.ShouldLoadPip(fullPip, m_buildGraph))
                {
                    m_pipsToSkip.Add(pipId.Value);
                    return null;
                }

                // Add the new pip descriptor to the pip store
                pip = m_pips.SynchronizedGetOrAdd(
                    fullPip,
                    m_buildGraph,
                    LoadOptions,
                    emptyConcurrentHashSetOfFileDescriptor,
                    emptyConcurrentHashSetOfPipDescriptor,
                    emptyConcurrentHashSetOfReportedProcesses,
                    emptyStringIDEnvVarDictionary,
                    emptyAbsolutePathConcurrentHashSet);
            }

            // Check if the pip has already been initialized. This method is called from multiple threads and it may happen that two threads may add the same pip at the same time
            if (Interlocked.Exchange(ref pip.IsInitializedFlag, 1) == 0)
            {
                // There will be a single thread that ever gets here for a single pip descriptor. We do not need to synchronize any property updates
                // Check if we already hydrated the pip. If not, hydrate it
                if (fullPip == null)
                {
                    fullPip = (Process)pipTable.HydratePip(pipId, pipQueryContext);
                }

                if (fullPip.Provenance.ModuleId.IsValid && m_dictModuleTable.ContainsKey(fullPip.Provenance.ModuleId))
                {
                    pip.Module = m_dictModuleTable[fullPip.Provenance.ModuleId];
                }

                if ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadOutputFiles) == 0)
                {
                    // Load pip outputs
                    foreach (var f in fullPip.FileOutputs)
                    {
                        if (f.IsValid && f.Path.IsValid)
                        {
                            LinkPipToFileDescriptor(f.ToFileArtifact(), pip, true, false, false);
                        }
                    }
                }

                if ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) == 0)
                {
                    // Load pip dependencies
                    foreach (var f in fullPip.Dependencies)
                    {
                        if (f.IsValid && f.Path.IsValid)
                        {
                            LinkPipToFileDescriptor(f, pip, false, true, false);
                        }
                    }

                    // Load pip Executable
                    LinkPipToFileDescriptor(fullPip.Executable, pip, false, false, true);
                }

                if ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0)
                {
                    // Load pip outputs
                    foreach (var d in fullPip.DirectoryOutputs)
                    {
                        if (d.IsValid && d.Path.IsValid)
                        {
                            LinkPipToDirectoryDescriptor(d.Path, pip, true, false);
                        }
                    }

                    // Load pip DirectoryDependencies
                    foreach (var d in fullPip.DirectoryDependencies)
                    {
                        if (d.IsValid && d.Path.IsValid)
                        {
                            LinkPipToDirectoryDescriptor(d.Path, pip, false, true);
                        }
                    }
                }

                if ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadRarelyUsedPipProperties) == 0)
                {
                    // Load pip EnvironmentVariables
                    foreach (var e in fullPip.EnvironmentVariables)
                    {
                        if (e.IsPassThrough)
                        {
                            EnvironmentVariableDescriptor environmentVariableDescriptor =
                                m_environmentVariablesDictionary.GetOrAdd(
                                    e.Name,
                                    (key) =>
                                        new EnvironmentVariableDescriptor(e.Name, m_buildGraph.Context.StringTable));

                            environmentVariableDescriptor.IsPassThroughEnvironmentVariable = true;

                            // Passthrough variables never have values. Since we still want to signal that the given pip had this variable in its environment,
                            // we will add the current pip to ReferencingPipsHashset and will set its value to string.Empty
                            environmentVariableDescriptor.ReferencingPipsHashset.Add(pip);
                            if (pip.EnvironmentVariablesDictionary.ContainsKey(environmentVariableDescriptor.NameId))
                            {
                                pip.EnvironmentVariablesDictionary.Update(environmentVariableDescriptor.NameId, e);
                            }
                            else
                            {
                                pip.EnvironmentVariablesDictionary.Add(environmentVariableDescriptor.NameId, e);
                            }
                        }
                        else if (e.Value.IsValid && (e.Value.FragmentCount > 0))
                        {
                            EnvironmentVariableDescriptor environmentVariableDescriptor =
                                m_environmentVariablesDictionary.GetOrAdd(
                                    e.Name,
                                    (key) =>
                                        new EnvironmentVariableDescriptor(e.Name, m_buildGraph.Context.StringTable));

                            environmentVariableDescriptor.ReferencingPipsHashset.Add(pip);

                            if (pip.EnvironmentVariablesDictionary.ContainsKey(environmentVariableDescriptor.Name))
                            {
                                pip.EnvironmentVariablesDictionary.Update(environmentVariableDescriptor.NameId, e);
                            }
                            else
                            {
                                pip.EnvironmentVariablesDictionary.Add(environmentVariableDescriptor.NameId, e);
                            }
                        }
                    }
                }
            }

            return pip;
        }

        /// <summary>
        /// Checks if a file should be loaded and generates a file descriptor object for it
        /// </summary>
        /// <param name="file">File artifact to generate a file descriptor for</param>
        /// <returns>FileDescriptor object, or null when the file should not be loaded</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        private FileDescriptor GetFileDescriptor(FileArtifact file)
        {
            // Generate filename
            string fileName = m_buildGraph.Context.PathTable.AbsolutePathToString(file);

            // Check if the file should be loaded or not. If not, return null.
            if (!m_loadContext.ShouldLoadFile(fileName))
            {
                return null;
            }

            // Return file descriptor object
            return m_fileDescriptorDictionary.GetOrAdd(
                        file.Path,
                        (f) => new FileDescriptor(file, m_buildGraph.Context.PathTable,
                            directoriesThatContainThisFileHashset: ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0) ? null : emptyConcurrentHashSetOfDirectoryDescriptor));
        }

        /// <summary>
        /// Utility method that connects files to pip descriptors
        /// </summary>
        /// <param name="fileObject">File artifact to be linked to the pip descriptor</param>
        /// <param name="pipDescriptor">The pip descriptor to link to</param>
        /// <param name="output">true, when the file is an output file for the pip</param>
        /// <param name="dependency">true, when the file is a dependency for the pip</param>
        /// <param name="tool">true, when the file is a tool that is executed when the pip is running</param>
        private void LinkPipToFileDescriptor(FileArtifact fileObject, PipDescriptor pipDescriptor, bool output, bool dependency, bool tool)
        {
            Contract.Requires((fileObject != null) && fileObject.IsValid);
            Contract.Requires(pipDescriptor != null);
            Contract.Requires(output || dependency || tool);

            // Retrieve file descriptor that corresponds to the file artifact.
            FileDescriptor fileDescriptor = GetFileDescriptor(fileObject);
            if (fileDescriptor == null)
            {
                return;
            }

            // Link file as an output
            if (output)
            {
                fileDescriptor.ProducingPipsHashset.Add(pipDescriptor);
                pipDescriptor.OutputFilesHashset.Add(fileDescriptor);
            }

            // Link file as a a dependency
            if (dependency)
            {
                fileDescriptor.DependentPipsHashset.Add(pipDescriptor);
                pipDescriptor.DependentFilesHashset.Add(fileDescriptor);
            }

            // Link file as a tool
            if (tool)
            {
                // make sure we are not overwriting and existing Executable that has already been set
                Contract.Assert(pipDescriptor.Executable == null);

                fileDescriptor.PipsThatExecuteThisFileHashset.Add(pipDescriptor);

                // we do not need to lock here because this method is called from LoadPip and there will only be one thread that ever updates the same pip
                pipDescriptor.Executable = fileDescriptor;
            }
        }

        /// <summary>
        /// Utility method that connects directories to pip descriptors
        /// </summary>
        /// <param name="directoryName">Directory path to be linked to the pip descriptor</param>
        /// <param name="pipDescriptor">The pip descriptor to link to</param>
        /// <param name="output">true, when the file is an output file for the pip</param>
        /// <param name="dependency">true, when the file is a dependency for the pip</param>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        private void LinkPipToDirectoryDescriptor(AbsolutePath directory, PipDescriptor pipDescriptor, bool output, bool dependency)
        {
            Contract.Requires(pipDescriptor != null);
            Contract.Requires(output || dependency);

            // Retrieve or create directory descriptor
            DirectoryDescriptor directoryDescriptor = m_directoryDescriptorDictionary.GetOrAdd(directory, (d) =>
            {
                return new DirectoryDescriptor(directory, m_buildGraph.Context.PathTable,
                    filesHashset: ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0) ? null : emptyConcurrentHashSetOfFileDescriptor);
            });

            // Link directory to the pip as an output directory
            if (output)
            {
                directoryDescriptor.ProducingPipsHashset.Add(pipDescriptor);
                pipDescriptor.DirectoryOutputsHashset.Add(directoryDescriptor);
            }

            // Link directory to the pip as a dependent directory
            if (dependency)
            {
                directoryDescriptor.DependentPipsHashset.Add(pipDescriptor);
                pipDescriptor.DirectoryDependenciesHashset.Add(directoryDescriptor);
            }
        }
        #endregion

        #region Pip graph loading

        /// <summary>
        /// Connects pip descriptors to neighboring pip descriptors based on their position in the build graph
        /// </summary>
        /// <param name="pipTable">Pip table containing all the pips in the pip graph</param>
        /// <param name="dataflowGraph">Dataflow graph that contains the links between the pips</param>
        /// <param name="sourcePip">The pip descriptor that represents the pip that we want to connect to its neighbors</param>
        /// <remarks>The PipTable contains all the pips in the build graph, no only the process pips. This library only loads process pips and therefore
        /// the non process pips have to be filtered out. When a neighboring pip is not a process pip, the code will step over it and will locate its closest Process pip neighbors.</remarks>
        private void ConnectNeighbourPips(PipTable pipTable, DirectedGraph dataflowGraph, PipDescriptor sourcePip)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires(sourcePip != null);

            // Enumerate all neighboring pips and link them to the source pip descriptor.
            Parallel.ForEach(Partitioner.Create(dataflowGraph.GetOutgoingEdges(sourcePip.NodeId)), m_parallelOptions, edge =>
            {
                LinkPipToSubgraph(pipTable, dataflowGraph, sourcePip, edge.OtherNode);
            });
        }

        /// <summary>
        /// Finds all neighboring Process pips for a pip. When a neighbor is not a Process pip, will skip over it until it finds a Process pip.
        /// </summary>
        /// <param name="pipTable">Pip table containing all the pips in the pip graph</param>
        /// <param name="dataflowGraph">Dataflow graph that contains the links between the pips</param>
        /// <param name="nodeToFind">The pip to check identified by a node Id</param>
        private ConcurrentHashSet<NodeId> FindNonProcessPipChildren(PipTable pipTable, DirectedGraph dataflowGraph, NodeId nodeToFind)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires(nodeToFind.IsValid);

            ConcurrentHashSet<NodeId> result;

            // Check memoization dictionary and see if we already processed this pip
            if (!m_memoizationChildrensOfNonProcessPips.TryGetValue(nodeToFind.Value, out result))
            {
                // No hits in the memoization dictionary, we have to find all the neighboring Process pips and return them as a collection
                result = new ConcurrentHashSet<NodeId>();
                FillChildrenList(pipTable, dataflowGraph, result, nodeToFind);

                // Add results to the memoization dictionary so we do not have to do this again
                result = m_memoizationChildrensOfNonProcessPips.GetOrAdd(nodeToFind.Value, result);
            }

            return result;
        }

        /// <summary>
        /// Finds all neighboring Process pips for a pip. When a neighbor is not a Process pip, will skip over it until it finds a Process pip.
        /// </summary>
        /// <param name="pipTable">Pip table containing all the pips in the pip graph</param>
        /// <param name="dataflowGraph">Dataflow graph that contains the links between the pips</param>
        /// <param name="result">Collection that will store the node Ids of the neighboring pips</param>
        /// <param name="nodeToFind">The pip to check identified by a node Id</param>
        private void FillChildrenList(PipTable pipTable, DirectedGraph dataflowGraph, ConcurrentHashSet<NodeId> result, NodeId nodeToFind)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires(result != null);
            Contract.Requires(nodeToFind.IsValid);

            // we will process all neighboring pips in parallel
            Parallel.ForEach(Partitioner.Create(dataflowGraph.GetOutgoingEdges(nodeToFind)), m_parallelOptions, edge =>
            {
                // Get pip Id
                PipId targetPipId = NodeIdExtensions.ToPipId(edge.OtherNode);

                // Get pip type
                PipType targetPipType = pipTable.GetPipType(targetPipId);

                // Check if we have a process pip
                if (!targetPipId.IsValid || !(targetPipType == PipType.Process))
                {
                    // It is not a process pip. We have to repeat the process recursively until we find a Process pip.
                    ConcurrentHashSet<NodeId> childrenToAdd = FindNonProcessPipChildren(pipTable, dataflowGraph, edge.OtherNode);

                    // add node Ids to the result
                    result.AddRange(childrenToAdd);
                }
                else
                {
                    // We have a Process pip. Add it to the result collection and stop.
                    result.Add(edge.OtherNode);
                }
            });
        }

        /// <summary>
        /// Connects the source pip to a neighboring pip identified by a node Id
        /// </summary>
        /// <param name="pipTable">Pip table containing all the pips in the pip graph</param>
        /// <param name="dataflowGraph">Dataflow graph that contains the links between the pips</param>
        /// <param name="sourcePip">The pip descriptor that represents the pip that we want to connect to its neighbor</param>
        /// <param name="targetNode">The pip to link to identified by a node Id</param>
        private void LinkPipToSubgraph(PipTable pipTable, DirectedGraph dataflowGraph, PipDescriptor sourcePip, NodeId targetNode)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires(sourcePip != null);
            Contract.Requires(targetNode.IsValid);

            // Retrieve the pip Id of the target pip
            PipId targetPipId = NodeIdExtensions.ToPipId(targetNode);

            // Retrieve the pip type of the target pip
            PipType targetPipType = pipTable.GetPipType(targetPipId);

            // We need a hash set to store the neighboring pips that we have to link to.
            // When the pip pointed to by targetNode is a Process pip, then this collection will only include a single pip - the one pointed to by targetNode.
            // When the pip pointed to by targetNode is NOT a Process pip, or the pip is filtered out, then the collection will include the Process pips that directly depend on targetNote.
            // When some of targetNode's dependencies are non Process pips, the same process is repeated, until a Process pip is found and then stop.
            ConcurrentHashSet<NodeId> children;
            if (!targetPipId.IsValid || !(targetPipType == PipType.Process) || !ShouldPipBeLoaded(targetPipId.Value))
            {
                // targetNode is NOT a process pip,or it has been filtered out ->  we have to look at its neighbors
                children = FindNonProcessPipChildren(pipTable, dataflowGraph, targetNode);
            }
            else
            {
                // targetNode is a process pip, we can link it to source pip
                children = new ConcurrentHashSet<NodeId>();
                children.Add(targetNode);
            }

            // process all children pips in parallel
            Parallel.ForEach(Partitioner.Create(children), m_parallelOptions, childNode =>
            {
                // Get pip descriptor for the target pip. Load pip either returns an existing pip descriptor (or creates a new one).
                PipDescriptor targetPip = LoadPip(pipTable, dataflowGraph, NodeIdExtensions.ToPipId(childNode));
                if (targetPip != null)
                {
                    // Hook up adjacent out node for the source pip
                    sourcePip.AdjacentOutNodesHashset.Add(targetPip);

                    // Hook up adjacent in nodes for the target pip too
                    targetPip.AdjacentInNodesHashset.Add(sourcePip);
                }
            });
        }
        #endregion

        #region Execution log event processing tasks

        /// <summary>
        /// Processes all the FileArtifactContentDecidedEventData objects that have been stored in m_listFileArtifactContentDecidedEvents while replaying the execution log events
        /// </summary>
        /// <returns>Returns a task that processes all the FileArtifactContenDecidedEventData objects</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        private Task ProcessFileArtifactContentDecidedEvents()
        {
            return Task.Run(() =>
            {
                // Process objects in parallel and use a partitioner to speed things up
                Parallel.ForEach(Partitioner.Create(m_dictFileArtifactContentDecidedEvents.Values), m_parallelOptions, data =>
                {
                    if (data.FileArtifact.IsValid)
                    {
                        if (!((((LoadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) != 0) && data.FileArtifact.IsSourceFile) ||
                              (((LoadOptions & ExecutionLogLoadOptions.DoNotLoadOutputFiles) != 0) && data.FileArtifact.IsOutputFile)))
                        {
                            // Create/retrieve file descriptor that matches the file from the event
                            FileDescriptor fileDescriptor = GetFileDescriptor(data.FileArtifact);
                            if (fileDescriptor != null)
                            {
                                // we need to lock the file descriptor
                                lock (fileDescriptor)
                                {
                                    // update file descriptor properties
                                    if (data.FileContentInfo.HasKnownLength)
                                    {
                                        fileDescriptor.AddFileRewriteData(data.FileArtifact.RewriteCount, data.FileContentInfo.Hash, data.FileContentInfo.Length);
                                    }
                                    else
                                    {
                                        fileDescriptor.AddFileRewriteData(data.FileArtifact.RewriteCount, data.FileContentInfo.Hash);
                                    }
                                }
                            }
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Processes one PipExecutionPerformanceEventData object
        /// </summary>
        private void ProcessPipExecutionPerformanceEvent(PipExecutionPerformanceEventData data)
        {
            // we only handle Process pips
            if (data.PipId.IsValid && (m_buildGraph.PipTable.GetPipType(data.PipId) == PipType.Process))
            {
                // Retrieve pip descriptor
                PipDescriptor pip = LoadPip(m_buildGraph.PipTable, m_buildGraph.DataflowGraph, data.PipId);

                if (pip != null)
                {
                    // there is no need to lock here. There will only be one event that contains execution performance data for this pip,
                    // and we will only update this property once for this pip.
                    pip.PipExecutionPerformance = (ProcessPipExecutionPerformance)data.ExecutionPerformance;
                }
            }
        }

        /// <summary>
        /// Processes all the PipExecutionPerformanceEventData objects that have been stored in m_dictPipExecutionPerformanceEvents while replaying the execution log events
        /// </summary>
        /// <returns>Returns a task that processes all the PipExecutionPerformanceEventData objects</returns>
        private Task ProcessPipExecutionPerformanceEvents()
        {
            return Task.Run(() =>
            {
                // Process objects in parallel and use a partitioner to speed things up
                Parallel.ForEach(m_dictPipExecutionPerformanceEvents, m_parallelOptions, data =>
                {
                    ProcessPipExecutionPerformanceEvent(data.Value);
                });
            });
        }

        /// <summary>
        /// Processes one DirectoryMembershipHashedEventData object
        /// </summary>
        /// <returns>Returns a task that processes the DirectoryMembershipHashedEventData object</returns>
        private Task ProcessDirectoryMembershipHashedEvent(DirectoryMembershipHashedEventData data)
        {
            return Task.Run(() =>
            {
                // Retrieve/create directory descriptor
                DirectoryDescriptor directoryDescriptor =
                    m_directoryDescriptorDictionary.GetOrAdd(
                        data.Directory,
                        (key) => new DirectoryDescriptor(data.Directory, m_buildGraph.Context.PathTable,
                            filesHashset: ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0) ? null : emptyConcurrentHashSetOfFileDescriptor));

                // Process member files in parallel and use a partitioner to speed things up
                Parallel.ForEach(Partitioner.Create(data.Members, true), m_parallelOptions,
                file =>
                {
                    FileDescriptor fileDescriptor;
                    if (((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMembershipsForUnusedFiles) != 0) ||
                            m_fileDescriptorDictionary.TryGetValue(
                                file,
                                out fileDescriptor))
                    {
                        // These events do not tell us if a file is a source file or output file.
                        // We will NOT load any files here if both DoNotLoadSourceFiles and DoNotLoadOutputFiles ist set.
                        if (((LoadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) == 0) &&
                                ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadOutputFiles) == 0))
                        {
                            // We have to load every file from the directory, even when it is not used during the build
                            fileDescriptor = GetFileDescriptor(FileArtifact.CreateSourceFile(file));
                            if (fileDescriptor != null)
                            {
                                directoryDescriptor.FilesHashset.Add(fileDescriptor);
                                fileDescriptor.DirectoriesThatContainThisFileHashset.Add(directoryDescriptor);
                            }
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Processes all the ObservedInputsEventData objects that have been stored in m_dictObservedInputsEvents while replaying the execution log events
        /// </summary>
        /// <returns>Returns a task that processes all the PipExecutionPerformanceEventData objects</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        private Task ProcessAllObservedInputsEvents()
        {
            return Task.Run(() =>
            {
                // Process objects in parallel and use a partitioner to speed things up
                Parallel.ForEach(Partitioner.Create(m_dictObservedInputsEvents.Values, EnumerablePartitionerOptions.None), m_parallelOptions,
                data =>
                {
                    Parallel.ForEach(Partitioner.Create(data.ObservedInputs), m_parallelOptions, input =>
                    {
                        if (input.Type == ObservedInputType.FileContentRead)
                        {
                            if ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) == 0)
                            {
                                // Retrieve file descriptor for the observed file
                                FileDescriptor fileDescriptor = GetFileDescriptor(FileArtifact.CreateSourceFile(input.Path));
                                if (fileDescriptor != null)
                                {
                                    // Unfortunately we can have multiple events for the same file, we need to lock here
                                    lock (fileDescriptor)
                                    {
                                        // Observed inputs always have a rewrite count of zero
                                        fileDescriptor.AddFileRewriteData(0, input.Hash);
                                        fileDescriptor.IsObservedInputFile = true;
                                    }
                                    if (m_buildGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
                                    {
                                        // Retrieve pip descriptor so we can link the file to it as a dependency
                                        PipDescriptor pip = LoadPip(
                                            m_buildGraph.PipTable,
                                            m_buildGraph.DataflowGraph,
                                            data.PipId);
                                        if (pip != null)
                                        {
                                            pip.ObservedInputsHashset.Add(fileDescriptor);

                                            fileDescriptor.DependentPipsHashset.Add(pip);
                                        }
                                    }
                                }
                            }
                        }
                        else if (input.Type == ObservedInputType.DirectoryEnumeration)
                        {
                            if ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0)
                            {
                                if (m_buildGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
                                {
                                    // Retrieve pip descriptor so we can link the directory to it as a dependency
                                    PipDescriptor pip = LoadPip(
                                        m_buildGraph.PipTable,
                                        m_buildGraph.DataflowGraph,
                                        data.PipId);
                                    if (pip != null)
                                    {
                                        // Retrieve directory descriptor for the observed input
                                        DirectoryDescriptor directoryDescriptor =
                                            m_directoryDescriptorDictionary.GetOrAdd(
                                                input.Path,
                                                (f) => new DirectoryDescriptor(input.Path, m_buildGraph.Context.PathTable,
                                                    filesHashset: ((LoadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) != 0) ? null : emptyConcurrentHashSetOfFileDescriptor));

                                        // Unfortunately we can have multiple events for the same input, we need to lock here
                                        lock (directoryDescriptor)
                                        {
                                            directoryDescriptor.ContentHashDictionary[pip] = input.Hash;
                                            directoryDescriptor.IsObservedInput = true;
                                        }

                                        directoryDescriptor.DependentPipsHashset.Add(pip);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if ((LoadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) == 0)
                            {
                                // ObservedInputType.AbsentPathProbe
                                // Retrieve file descriptor for the observed file
                                FileDescriptor fileDescriptor = GetFileDescriptor(FileArtifact.CreateSourceFile(input.Path));
                                if (fileDescriptor != null)
                                {
                                    lock (fileDescriptor)
                                    {
                                        fileDescriptor.WasFileProbed = true;
                                    }
                                    if (m_buildGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
                                    {
                                        // Retrieve pip descriptor so we can link the file to it as a dependency
                                        PipDescriptor pip = LoadPip(
                                            m_buildGraph.PipTable,
                                            m_buildGraph.DataflowGraph,
                                            data.PipId);
                                        if (pip != null)
                                        {
                                            pip.ProbedFilesHashset.Add(fileDescriptor);
                                            fileDescriptor.DependentPipsHashset.Add(pip);
                                        }
                                    }
                                }
                            }
                        }
                    });
                });
            });
        }

        /// <summary>
        /// Processes all the ProcessProcessExecutionMonitoringReportedEventData objects that have been stored in m_listExecutionMonitoringReportedEvents while replaying the execution log events
        /// </summary>
        /// <returns>Returns a task that processes all the ProcessProcessExecutionMonitoringReportedEventData objects</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        private Task ProcessProcessExecutionMonitoringReportedEvents()
        {
            return Task.Run(() =>
            {
                // Process objects in parallel and use a partitioner to speed things up
                Parallel.ForEach(Partitioner.Create(m_listExecutionMonitoringReportedEvents, true), m_parallelOptions, data =>
                {
                    if (data.PipId.IsValid)
                    {
                        // Retrieve pip descriptor to link the process instance to
                        PipDescriptor pip = LoadPip(m_buildGraph.PipTable, m_buildGraph.DataflowGraph, data.PipId);
                        if (pip != null)
                        {
                            ICollection<ReportedProcess> reportedProcesses = (ICollection<ReportedProcess>)data.ReportedProcesses;

                            // Process ReportedProcesses
                            if (reportedProcesses != null)
                            {
                                // Process each process instance in parallel
                                Parallel.ForEach(
                                    Partitioner.Create(reportedProcesses),
                                    m_parallelOptions,
                                    p =>
                                    {
                                        // Retrieve process descriptor
                                        ProcessDescriptor process = m_reportedProcessesDictionary.GetOrAdd(
                                            p.Path.ToLowerInvariant(),
                                            (key) => new ProcessDescriptor(p.Path));

                                        // Assign process instances to file descriptors that the process has accessed
                                        process.AddPip(
                                            pip,
                                            p.ProcessId,
                                            p.ProcessArgs,
                                            p.KernelTime,
                                            p.UserTime,
                                            p.IOCounters,
                                            p.CreationTime,
                                            p.ExitTime,
                                            p.ExitCode,
                                            p.ParentProcessId);
                                    });
                            }

                            // Process ReportedFileAccesses
                            if ((data.ReportedFileAccesses != null) &&
                                ((LoadOptions & ExecutionLogLoadOptions.LoadReportedFileAccesses) == ExecutionLogLoadOptions.LoadReportedFileAccesses))
                            {
                                Parallel.ForEach(
                                    Partitioner.Create(data.ReportedFileAccesses),
                                    m_parallelOptions,
                                    rfa =>
                                    {
                                        AbsolutePath absPath = AbsolutePath.Invalid;
                                        if (!string.IsNullOrEmpty(rfa.Path))
                                        {
                                            // Try to get a matching AbsolutePath. Create a new one if it does not exist.
                                            if (!AbsolutePath.TryCreate(m_buildGraph.Context.PathTable, rfa.Path, out absPath))
                                            {
                                                // This happens with path names like "b:" and "\\.\CONOUT$"
                                                // For some of these paths (i.e. b:), rfa.ManifestPath contains the correct AbsolutePath (b:\)
                                                // For others, rfa.ManifestPath is Invalid.
                                                // Since we have no other choice, we will attempt to use rfa.ManifestPath.
                                                // The next if statement will handle the case when it is invalid.
                                                // We do not want to fail loading the XLG just because we encountered an invalid path.
                                                // It is better to return as much data as we can, instead of failing here and returning nothing.
                                                absPath = rfa.ManifestPath;
                                            }
                                        }
                                        else
                                        {
                                            absPath = rfa.ManifestPath;
                                        }

                                        if (absPath.IsValid && m_loadContext.ShouldLoadFile(absPath.ToString(m_buildGraph.Context.PathTable)))
                                        {
                                            ConcurrentHashSet<ReportedFileAccessDescriptor> items =
                                                (ConcurrentHashSet<ReportedFileAccessDescriptor>)m_reportedFileAccessesDictionary.GetOrAdd(
                                                    absPath,
                                                    (key) => new ConcurrentHashSet<ReportedFileAccessDescriptor>());

                                            // Add file access record to dictionary
                                            items.Add(new ReportedFileAccessDescriptor(pip, ref rfa, m_buildGraph.Context.PathTable));
                                        }
                                    });
                            }
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Processes one DirectoryMembershipHashedEventData object
        /// </summary>
        /// <returns>Returns a task that processes the DirectoryMembershipHashedEventData object</returns>
        private Task ProcessAllFingerprintComputationEvents()
        {
            return Task.Run(() =>
            {
                // Process objects in parallel and use a partitioner to speed things up
                Parallel.ForEach(Partitioner.Create(m_listProcessFingerprintComputationEvents, true), m_parallelOptions, data =>
                {
                    if (data.PipId.IsValid)
                    {
                        // Retrieve pip descriptor to link the process instance to
                        PipDescriptor pip = LoadPip(m_buildGraph.PipTable, m_buildGraph.DataflowGraph, data.PipId);
                        if (pip != null)
                        {
                            pip.Fingerprint = new ContentHash(SHA1HashInfo.Instance.HashType, data.WeakFingerprint.ToGenericFingerprint().Hash.ToByteArray());
                            pip.FingerprintKind = data.Kind;
                            pip.StrongFingerprintComputations = data.StrongFingerprintComputations;
                        }
                    }
                });
            });
        }
        #endregion

        #region IExecutionLogTarget implementation

        /// <summary>
        /// IExecutionLogTarget override that handles PipExecutionPerformanceEvents
        /// </summary>
        /// <param name="data">Event parameter</param>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.ExecutionPerformance is ProcessPipExecutionPerformance
                && data.PipId.IsValid
                && !m_pipsToSkip.Contains(data.PipId.Value))
            {
                // Add event data to the list to be processes later
                m_dictPipExecutionPerformanceEvents.TryAdd(data.PipId.Value, data);
            }
        }

        /// <summary>
        /// IExecutionLogTarget override that handles FileArtifactContentDecidedEvents
        /// </summary>
        /// <param name="data">Event parameter</param>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            // Add event data to the list to be processes later
            m_dictFileArtifactContentDecidedEvents.TryAdd(data.FileArtifact, data);
        }

        /// <summary>
        /// IExecutionLogTarget override that handles DirectoryMembershipHashedEvents
        /// </summary>
        /// <param name="data">Event parameter</param>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            // When the pip id is invalid, it means that the event is not tied
            // to a particular pip, the event could apply to one or more pips
            if (data.Directory.IsValid
                && ((data.PipId.IsValid && !m_pipsToSkip.Contains(data.PipId.Value)) || !data.PipId.IsValid))
            {
                // Start task that processes this event
                lock (m_listDirectoryMembershipHashedEventProcessingTasks)
                {
                    m_listDirectoryMembershipHashedEventProcessingTasks.Add(ProcessDirectoryMembershipHashedEvent(data));
                }
            }
        }

        /// <summary>
        /// IExecutionLogTarget override that handles ProcessExecutionMonitoringReportedEvens
        /// </summary>
        /// <param name="data">Event parameter</param>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            // Add event data to the list to be processes later
            if (data.PipId.IsValid && !m_pipsToSkip.Contains(data.PipId.Value))
            {
                lock (m_listExecutionMonitoringReportedEvents)
                {
                    m_listExecutionMonitoringReportedEvents.Add(data);
                }
            }
        }

        /// <summary>
        /// Information about computation of a strong fingerprint for a process
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.PipId.IsValid && !m_pipsToSkip.Contains(data.PipId.Value))
            {
                lock (m_listProcessFingerprintComputationEvents)
                {
                    m_listProcessFingerprintComputationEvents.Add(data);
                }

                if ((data.StrongFingerprintComputations != null) && (data.StrongFingerprintComputations.Count > 0))
                {
                    for (int index = data.StrongFingerprintComputations.Count - 1; index >= 0; --index)
                    {
                        if (data.StrongFingerprintComputations[index].Succeeded)
                        {
                            m_dictObservedInputsEvents[data.PipId.Value] = new ObservedInputsEventData()
                            {
                                PipId = data.PipId,
                                ObservedInputs = data.StrongFingerprintComputations[index].ObservedInputs,
                            };
                            break;
                        }
                    }
                }
            }
        }
        #endregion

        #region IDisposable implementation

        /// <summary>
        /// IDisposable implementation of Dispose
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();
        }
        #endregion
    }
}
