// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Plugin;
using BuildXL.Processes;
using BuildXL.Processes.Remoting;
using BuildXL.Processes.VmCommandProxy;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.FileSystem;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using BuildXL.Scheduler.Distribution;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Execution environment suitable for a single pip during testing.
    /// </summary>
    /// <remarks>
    /// This environment assumes that a pip is ready to run (i.e., all outputs are available). Pip execution may fail non-gracefully otherwise.
    /// Unlike the primary execution environment (<see cref="global::BuildXL.Scheduler.Scheduler"/>), this implementation does not know to hash files ahead of time;
    /// instead, files are hashed synchronously just-in-time.
    /// If a suitable real cache is not provided, this provides an in-memory content cache (so aggregate build outputs must all fit in memory).
    /// In all, this is a special-purpose stub implementation suitable mostly just for testing.
    /// </remarks>
    public sealed class DummyPipExecutionEnvironment :
        IPipExecutionEnvironment,
        IDirectoryMembershipFingerprinter,
        IFileContentManagerHost,
        IDisposable
    {
        private readonly ConcurrentDictionary<AbsolutePath, FileArtifact> m_knownSealedArtifacts = new ConcurrentDictionary<AbsolutePath, FileArtifact>();
        private readonly HashSet<AbsolutePath> m_knownSealedSourceDirectoriesAllDirectories = new HashSet<AbsolutePath>();
        private readonly HashSet<AbsolutePath> m_knownDynamicOutputDirectories = new HashSet<AbsolutePath>();
        private readonly HashSet<AbsolutePath> m_knownSealedSourceDirectoriesTopDirectoryOnly = new HashSet<AbsolutePath>();
        private readonly ConcurrentDictionary<DirectoryArtifact, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>> m_knownSealedDirectoryContents = new ConcurrentDictionary<DirectoryArtifact, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>();
        private readonly bool m_allowUnspecifiedSealedDirectories;
        private IReadOnlyDictionary<PipId, IReadOnlyCollection<Pip>> m_servicePipToClientProcesses;
        private readonly IFileMonitoringViolationAnalyzer m_disabledFileMonitoringViolationAnalyzer = new DisabledFileMonitoringViolationAnalyzer();
        private readonly ISandboxConnection m_sandboxConnection;

        public Dictionary<FileArtifact, string> HostMaterializedFileContents = new Dictionary<FileArtifact, string>();

        public Dictionary<FileArtifact, FileArtifact> CopyFileSources = new Dictionary<FileArtifact, FileArtifact>();

        private long m_filesUpToDate;
        private long m_filesProduced;
        private long m_filesDeployedFromCache;
        private int m_pipsWithWarningsFromCache;
        private long m_warningsFromCache;
        private int m_pipsWithWarnings;
        private long m_warnings;
        private long m_tryBringContentToLocalCacheCounts;
        private readonly ConcurrentBigMap<DirectoryArtifact, int[]> m_sealContentsById;
        public readonly LoggingContext LoggingContext;

        private readonly OperationTracker m_operationTracker;

        // Set of pips to inject cache miss for. This is not thread-safe
        // so only modify when not executing pips
        public HashSet<PipId> InjectedCacheMissPips = new HashSet<PipId>();

        public ExecutionLogRecorder ExecutionLogRecorder { get; private set; }

        /// <summary>
        /// Creates an execution environment for a single pip. To run pips incrementally, the <paramref name="fileContentTable"/> and <paramref name="pipCache"/> should be specified.
        /// </summary>
        public DummyPipExecutionEnvironment(
            LoggingContext loggingContext,
            PipExecutionContext context,
            IConfiguration config,
            FileContentTable fileContentTable = null,
            EngineCache pipCache = null,
            SemanticPathExpander semanticPathExpander = null,
            PipContentFingerprinter.PipDataLookup pipDataLookup = null,
            FileAccessAllowlist fileAccessAllowlist = null,
            bool allowUnspecifiedSealedDirectories = false,
            PipTable pipTable = null,
            IIpcProvider ipcProvider = null,
            (string substSource, string substTarget)? subst = default,
            ISandboxConnection sandboxConnection = null)
        {
            Contract.Requires(context != null);
            Contract.Requires(config != null);

            LoggingContext = loggingContext;
            Context = context;

            // Ensure paths visible when debugging
            PathTable.DebugPathTable = Context.PathTable;
            Configuration = config;
            PipTable = pipTable;
            PathExpander = semanticPathExpander ?? SemanticPathExpander.Default;
            ContentFingerprinter = new PipContentFingerprinter(
                Context.PathTable,
                artifact => State.FileContentManager.GetInputContent(artifact).FileContentInfo,
                new ExtraFingerprintSalts(config, fingerprintSalt: null, searchPathToolsHash: null, observationReclassificationRulesHash: null),
                pathExpander: PathExpander,
                pipDataLookup: pipDataLookup);
            PipFragmentRenderer = this.CreatePipFragmentRenderer();
            IpcProvider = ipcProvider ?? IpcFactory.GetProvider();

            FileContentTable = fileContentTable ?? FileContentTable.CreateNew(LoggingContext);
            Cache = pipCache;
            FileAccessAllowlist = fileAccessAllowlist;
            m_allowUnspecifiedSealedDirectories = allowUnspecifiedSealedDirectories;
            m_sandboxConnection = sandboxConnection;

            if (Cache == null)
            {
                Cache = InMemoryCacheFactory.Create();
            }

            var tracker = FileChangeTracker.CreateDisabledTracker(LoggingContext);
            LocalDiskContentStore = new LocalDiskContentStore(loggingContext, context.PathTable, FileContentTable, tracker);
            PipGraphView = new TestPipGraphFilesystemView(Context.PathTable);
            m_operationTracker = new OperationTracker(loggingContext);

            var fileSystemView = new FileSystemView(Context.PathTable, PipGraphView, LocalDiskContentStore);

            var preserveOutputsSalt = UnsafeOptions.PreserveOutputsNotUsed;

            if (config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled)
            {
                preserveOutputsSalt = new PreserveOutputsInfo(ContentHashingUtilities.HashString(Guid.NewGuid().ToString()), config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputsTrustLevel);
            }

            State = new PipExecutionState(
                config,
                loggingContext,
                cache: new PipTwoPhaseCache(loggingContext, Cache, context, PathExpander),
                fileAccessAllowlist: FileAccessAllowlist,
                directoryMembershipFingerprinter: this,
                pathExpander: PathExpander,
                executionLog: ExecutionLogRecorder,
                fileSystemView: fileSystemView,
                fileContentManager: GetFileContentManager(),
                directoryMembershipFinterprinterRuleSet: null,
                unsafeConfiguration: config.Sandbox.UnsafeSandboxConfiguration,
                preserveOutputsSalt: preserveOutputsSalt,
                serviceManager: new DummyServiceManager(),
                sidebandState: null,
                alienFileEnumerationCache: new ConcurrentBigMap<AbsolutePath, IReadOnlyList<(AbsolutePath, string)>>(),
                fileTimestampTracker: new FileTimestampTracker(DateTime.UtcNow, context.PathTable),
                globalReclassificationRules: new ObservationReclassifier());

            m_sealContentsById = new ConcurrentBigMap<DirectoryArtifact, int[]>();
            
            DirectoryTranslator = new DirectoryTranslator();
            foreach (var directoryToTranslate in config.Engine.DirectoriesToTranslate)
            {
                DirectoryTranslator.AddTranslation(directoryToTranslate.FromPath.ToString(context.PathTable), directoryToTranslate.ToPath.ToString(context.PathTable));
            }

            if (subst.HasValue)
            {
                DirectoryTranslator.AddTranslation(subst.Value.substSource, subst.Value.substTarget);
            }

            DirectoryTranslator.Seal();

            PipSpecificPropertiesConfig = new PipSpecificPropertiesConfig(Configuration.Engine.PipSpecificPropertyAndValues);
        }

        internal void RecordExecution()
        {
            ExecutionLogRecorder = new ExecutionLogRecorder();
            State.ExecutionLog = ExecutionLogRecorder;
        }

        /// <summary>
        /// Resets the file content manager
        /// This should be done in between builds where materialization state is not expected to be retained
        /// which use the same execution environment
        /// </summary>
        internal void ResetFileContentManager()
        {
            State.FileContentManager = GetFileContentManager();
        }

        /// <summary>
        /// Between builds, we sometimes change the existence of source paths.
        /// Using a pathexistencecache can cause issues in the builds using  
        /// a shared execution environment.
        /// That's why, we reset the filesystemview.
        /// In the actual builds, pathexistencecache is always cleaned even for 
        /// dev builds using server mode.
        /// </summary>
        internal void ResetFileSystemView()
        {
            State.FileSystemView = GetFileSystemView();
        }

        private FileContentManager GetFileContentManager()
        {
            return new FileContentManager(this, m_operationTracker)
            {
                // Maintain legacy unit test behavior where source files
                // are NOT marked as untracked if not under a valid mount
                // instead they are just hashed normally
                TrackFilesUnderInvalidMountsForTests = true
            };
        }

        private FileSystemView GetFileSystemView()
        {
            return new FileSystemView(Context.PathTable, PipGraphView, LocalDiskContentStore);
        }

        /// <nodoc />
        public void SetServicePipClients(IReadOnlyDictionary<PipId, IReadOnlyCollection<Pip>> servicePipToClientProcesses)
        {
            Contract.Requires(servicePipToClientProcesses != null);

            Contract.Assert(m_servicePipToClientProcesses == null, "cannot set service pip clients more than once");
            m_servicePipToClientProcesses = servicePipToClientProcesses;
        }

        /// <nodoc />
        public void Dispose()
        {
            Cache.Dispose();
        }

        /// <summary>
        /// Associates a directory artifact with its constituent file artifacts. The contents must be set before the directory artifact may
        /// be used by a pip (see <see cref="ListSealedDirectoryContents"/>).
        /// </summary>
        public void SetSealedDirectoryContents(DirectoryArtifact directory, params FileArtifact[] artifactsInDirectory)
        {
            Contract.Requires(directory.IsValid);
            Contract.Requires(artifactsInDirectory != null);

            FileArtifact[] artifacts = artifactsInDirectory.ToArray();
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> sortedArtifacts = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.SortUnsafe(
                artifacts,
                OrdinalFileArtifactComparer.Instance);

            bool added = m_knownSealedDirectoryContents.TryAdd(directory, sortedArtifacts);
            Contract.Assume(added);

            foreach (FileArtifact artifact in artifacts)
            {
                Contract.Assume(artifact.Path.IsWithin(Context.PathTable, directory.Path));
                m_knownSealedArtifacts[artifact.Path] = artifact;
            }
        }

        public void RegisterDynamicOutputDirectory(DirectoryArtifact directory)
        {
            m_knownDynamicOutputDirectories.Add(directory.Path);
        }

        public void SetSealedSourceDirectory(DirectoryArtifact directory, bool allDirectories, params AbsolutePath[] dynamicallyAccessedPathInDirectory)
        {
            var directoryPath = directory.Path;
            var sealedDiretoriesToUse = allDirectories ? m_knownSealedSourceDirectoriesAllDirectories : m_knownSealedSourceDirectoriesTopDirectoryOnly;
            sealedDiretoriesToUse.Add(directoryPath);

            foreach (AbsolutePath path in dynamicallyAccessedPathInDirectory)
            {
                if (allDirectories)
                {
                    Contract.Assume(path.IsWithin(Context.PathTable, directoryPath));
                }
                else
                {
                    Contract.Assume(
                        path.GetParent(Context.PathTable) == directoryPath,
                        "If not recursive all files must be directly under the folder.");
                }

                m_knownSealedArtifacts[path] = FileArtifact.CreateSourceFile(path);
            }
        }

        /// <summary>
        /// The BuildXL context
        /// </summary>
        public PipExecutionContext Context { get; }

        /// <summary>
        /// The pip table
        /// </summary>
        public PipTable PipTable { get; }

        /// <summary>
        /// The pip execution state
        /// </summary>
        public PipExecutionState State { get; }

        /// <summary>
        /// Indicates the number of output files that were already up to date during execution.
        /// </summary>
        public long OutputFilesUpToDate => Volatile.Read(ref m_filesUpToDate);

        /// <summary>
        /// Indicates the number of output files that were produced during execution.
        /// </summary>
        public long OutputFilesProduced => Volatile.Read(ref m_filesProduced);

        /// <summary>
        /// Indicates the number of output files that were deployed from cache.
        /// </summary>
        public long OutputFilesDeployedFromCache => Volatile.Read(ref m_filesDeployedFromCache);

        /// <summary>
        /// Indicates the number of warnings that were replayed from cache.
        /// </summary>
        public long WarningsFromCache => Volatile.Read(ref m_warningsFromCache);

        /// <summary>
        /// Indicates the number of pips that caused warnings that were replayed from cache.
        /// </summary>
        public int PipsWithWarningsFromCache => Volatile.Read(ref m_pipsWithWarningsFromCache);

        /// <summary>
        /// Indicates the number of warnings that were caused by pips that were run.
        /// </summary>
        public long Warnings => Volatile.Read(ref m_warnings);

        /// <summary>
        /// Indicates the number of pips that were run and caused warnings.
        /// </summary>
        public int PipsWithWarnings => Volatile.Read(ref m_pipsWithWarnings);

        /// <summary>
        /// Indicates the number of attempts pips try to bring contents to the local cache.
        /// </summary>
        public long NumberOfTryBringContentToLocalCache => Volatile.Read(ref m_tryBringContentToLocalCacheCounts);

        /// <inheritdoc />
        public FileContentTable FileContentTable { get; }

        /// <inheritdoc />
        public FileAccessAllowlist FileAccessAllowlist { get; }

        /// <summary>
        /// Gets the in memory content cache if applicable
        /// </summary>
        public InMemoryArtifactContentCache InMemoryContentCache => Cache.ArtifactContentCache as InMemoryArtifactContentCache;

        /// <inheritdoc />
        public EngineCache Cache { get; }

        /// <inheritdoc />
        public LocalDiskContentStore LocalDiskContentStore { get; }

        /// <inheritdoc />
        public PipContentFingerprinter ContentFingerprinter { get; }

        /// <inheritdoc />
        public PipFragmentRenderer PipFragmentRenderer { get; }

        /// <inheritdoc />
        public IIpcProvider IpcProvider { get; }

        /// <inheritdoc />
        public SemanticPathExpander PathExpander { get; }

        /// <inheritdoc />
        public IFileMonitoringViolationAnalyzer FileMonitoringViolationAnalyzer => m_disabledFileMonitoringViolationAnalyzer;

        /// <inheritdoc />
        public bool MaterializeOutputsInBackground => false;

        /// <inheritdoc />
        public bool IsTerminating => false;

        /// <inheritdoc />
        public bool IsTerminatingWithInternalError => false;

        /// <inheritdoc />
        public CancellationToken SchedulerCancellationToken => default(CancellationToken);

        /// <inheritdoc />
        public bool InputsLazilyMaterialized => false;

        /// <inheritdoc />
        public DirectoryFingerprint? TryQueryDirectoryFingerprint(AbsolutePath directoryPath)
        {
            try
            {
                string canonicalizedDirectoryListing =
                    string.Join(
                        "|",
                        Directory.EnumerateFileSystemEntries(directoryPath.ToString(Context.PathTable))
                            .Select(fullPath => Path.GetFileName(fullPath).ToCanonicalizedPath())
                            .OrderBy(fileName => fileName, OperatingSystemHelper.PathComparer));
                ContentHash listingHash = ContentHashingUtilities.HashBytes(Encoding.Unicode.GetBytes(canonicalizedDirectoryListing));
                return new DirectoryFingerprint(listingHash);
            }
            catch (UnauthorizedAccessException)
            {
                return null; // Failure to compute fingerprint.
            }
            catch (PathTooLongException)
            {
                return null; // Failure to compute fingerprint.
            }
            catch (DirectoryNotFoundException)
            {
                return DirectoryFingerprint.Zero; // Graceful empty fingerprint; path does not exist.
            }
            catch (IOException)
            {
                return DirectoryFingerprint.Zero; // Graceful empty fingerprint; path is a file not a directory.
            }
        }

        /// <inheritdoc />
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact)
        {
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> artifacts;
            if (!m_knownSealedDirectoryContents.TryGetValue(directoryArtifact, out artifacts))
            {
                if (m_allowUnspecifiedSealedDirectories)
                {
                    FileArtifact[] sourceArtifactsUnderSealRoot =
                        Directory.EnumerateFiles(directoryArtifact.Path.ToString(Context.PathTable), "*", SearchOption.AllDirectories)
                            .Select(p => FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, p)))
                            .ToArray();
                    return SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.SortUnsafe(
                        sourceArtifactsUnderSealRoot,
                        OrdinalFileArtifactComparer.Instance);
                }
                else
                {
                    Contract.Assume(false, "Unknown directory artifact for path " + directoryArtifact.Path.ToString(Context.PathTable));
                }
            }

            return artifacts;
        }

        /// <inheritdoc />
        public bool IsSourceSealedDirectory(DirectoryArtifact directoryArtifact, out bool allDirectories, out ReadOnlyArray<StringId> patterns)
        {
            Contract.Requires(directoryArtifact.IsValid);
            patterns = ReadOnlyArray<StringId>.Empty;
            if (m_knownSealedSourceDirectoriesAllDirectories.Contains(directoryArtifact.Path))
            {
                allDirectories = true;
                return true;
            }

            if (m_knownSealedSourceDirectoriesTopDirectoryOnly.Contains(directoryArtifact.Path))
            {
                allDirectories = false;
                return true;
            }

            allDirectories = false;
            return false;
        }

        public bool TryGetCopySourceFile(FileArtifact artifact, out FileArtifact sourceFile)
        {
            return CopyFileSources.TryGetValue(artifact, out sourceFile);
        }

        /// <inheritdoc />
        public bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact fileArtifact)
        {
            return false;
        }

        /// <inheritdoc />
        public bool IsFileRewritten(in FileArtifact fileArtifact)
        {
            return false;
        }

        /// <inheritdoc />
        public void ReportContent(FileArtifact artifact, in FileMaterializationInfo hash, PipOutputOrigin origin)
        {
            if (!artifact.IsOutputFile)
            {
                return;
            }

            switch (origin)
            {
                case PipOutputOrigin.UpToDate:
                    Interlocked.Increment(ref m_filesUpToDate);
                    break;
                case PipOutputOrigin.Produced:
                    Interlocked.Increment(ref m_filesProduced);
                    break;
                case PipOutputOrigin.DeployedFromCache:
                    Interlocked.Increment(ref m_filesDeployedFromCache);
                    break;
                case PipOutputOrigin.NotMaterialized:
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled PipOutputOrigin");
            }
        }

        /// <inheritdoc />
        public void ReportDynamicOutputFile(FileArtifact path)
        {
            // Do nothing.
        }

        /// <inheritdoc />
        public void ReportMaterializedArtifact(in FileOrDirectoryArtifact artifact)
        {
            // Do nothing.
        }

        /// <inheritdoc />
        public Possible<Unit> ReportFileArtifactPlaced(in FileArtifact artifact, FileMaterializationInfo info)
        {
            // Do nothing.
            return Unit.Void;
        }

        /// <inheritdoc />
        public void ReportWarnings(bool fromCache, int count)
        {
            Contract.Requires(count > 0);
            if (fromCache)
            {
                Interlocked.Increment(ref m_warningsFromCache);
                Interlocked.Add(ref m_pipsWithWarningsFromCache, count);
            }
            else
            {
                Interlocked.Increment(ref m_warnings);
                Interlocked.Add(ref m_pipsWithWarnings, count);
            }
        }

        /// <inheritdoc />
        public bool ShouldHaveArtificialMiss(Pip pip)
        {
            Contract.Requires(pip != null);
            if (InjectedCacheMissPips.Contains(pip.PipId))
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public int GetPipPriority(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return 0;
        }

        /// <inheritdoc />
        public bool AllowArtifactReadOnly(in FileOrDirectoryArtifact artifact)
        {
            return true;
        }

        /// <inheritdoc />
        public DirectoryFingerprint? TryComputeDirectoryFingerprint(
            AbsolutePath directoryPath,
            CacheablePipInfo process,
            Func<EnumerationRequest, PathExistence?> tryEnumerateDirectory,
            bool cacheable,
            DirectoryMembershipFingerprinterRule rule,
            DirectoryMembershipHashedEventData eventData)
        {
            Contract.Requires(directoryPath.IsValid);
            return TryQueryDirectoryFingerprint(directoryPath);
        }

        /// <inheritdoc />
        public void ReportCacheDescriptorHit(string sourceCache)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourceCache));

            // Do Nothing.
        }

        /// <inheritdoc />
        public IEnumerable<Pip> GetServicePipClients(PipId pipId)
        {
            IReadOnlyCollection<Pip> clients;
            return (!pipId.IsValid || m_servicePipToClientProcesses == null || !m_servicePipToClientProcesses.TryGetValue(pipId, out clients))
                ? null
                : clients;
        }

        /// <inheritdoc />
        public DirectoryTranslator DirectoryTranslator { get; }

        /// <inheritdoc />
        public CounterCollection<PipExecutorCounter> Counters { get; } = new CounterCollection<PipExecutorCounter>();

        /// <inheritdoc />
        public IConfiguration Configuration { get; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> RootMappings => new Dictionary<string, string>();

        public IPipGraphFileSystemView PipGraphView { get; set; }

        LoggingContext IFileContentManagerHost.LoggingContext => LoggingContext;

        IArtifactContentCache IFileContentManagerHost.ArtifactContentCache => Cache.ArtifactContentCache;

        IExecutionLogTarget IFileContentManagerHost.ExecutionLog => State.ExecutionLog;

        SemanticPathExpander IFileContentManagerHost.SemanticPathExpander => PathExpander;

        public ISandboxConnection SandboxConnection => m_sandboxConnection;

        public VmInitializer VmInitializer { get; }

        public IRemoteProcessManager RemoteProcessManager { get; }

        public ITempCleaner TempCleaner => new TestMoveDeleteCleaner(Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "moveDeletionTemp"));

        public ReparsePointResolver ReparsePointAccessResolver => null;

        public PluginManager PluginManager { get; }

        public IReadOnlySet<AbsolutePath> TranslatedGlobalUnsafeUntrackedScopes => CollectionUtilities.EmptySet<AbsolutePath>();

        public SchedulerTestHooks SchedulerTestHooks { get; }

        /// <inheritdoc />
        public PipSpecificPropertiesConfig PipSpecificPropertiesConfig { get; set; }

        public bool HasFailed => throw new NotImplementedException();

        public bool IsRamProjectionActive => true;

        public LocalWorker LocalWorker { get; }

        public SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directory)
        {
            if (m_knownSealedSourceDirectoriesAllDirectories.Contains(directory.Path))
            {
                return SealDirectoryKind.SourceAllDirectories;
            }

            if (m_knownSealedSourceDirectoriesTopDirectoryOnly.Contains(directory.Path))
            {
                return SealDirectoryKind.SourceTopDirectoryOnly;
            }

            if (m_knownDynamicOutputDirectories.Contains(directory.Path))
            {
                return SealDirectoryKind.Opaque;
            }

            // TODO: Is this ok?
            return SealDirectoryKind.Partial;
        }

        public bool TryGetSourceSealDirectory(DirectoryArtifact directory, out SourceSealWithPatterns sourceSealWithPatterns)
        {
            sourceSealWithPatterns = default;

            if (IsSourceSealedDirectory(directory, out bool allDirectories, out ReadOnlyArray<StringId> patterns))
            {
                sourceSealWithPatterns = new SourceSealWithPatterns(directory.Path, patterns, !allDirectories);
                return true;
            }

            return false;
        }

        public bool ShouldScrubFullSealDirectory(DirectoryArtifact directory)
        {           
            return false;
        }

        PipId IFileContentManagerHost.TryGetProducerId(in FileOrDirectoryArtifact artifact)
        {
            return PipId.Invalid;
        }

        public Pip GetProducer(in FileOrDirectoryArtifact artifact)
        {
            throw Contract.AssertFailure("GetProducer should not be called on DummyPipExecutionEnvironment");
        }

        public string GetProducerDescription(in FileOrDirectoryArtifact artifact)
        {
            var kind = artifact.IsDirectory ? "directory" : "file";
            return FormattableStringEx.I($"Producer for '{artifact.Path}' ({kind})");
        }

        /// <summary>
        /// Gets the pip description for a consumer of the given file artifact (if any). Otherwise, null.
        /// </summary>
        public string GetConsumerDescription(in FileOrDirectoryArtifact artifact)
        {
            return null;
        }

        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory)
        {
            return ListSealedDirectoryContents(directory);
        }

        public void SetMaxExternalProcessRan()
        {
            // Do Nothing.
        }

        public bool CanMaterializeFile(FileArtifact artifact)
        {
            return HostMaterializedFileContents.ContainsKey(artifact);
        }

        public Task<Possible<ContentMaterializationOrigin>> TryMaterializeFileAsync(FileArtifact artifact, OperationContext operationContext)
        {
            Contract.Requires(HostMaterializedFileContents.ContainsKey(artifact));
            return Task.Run(async () =>
                {
                    File.WriteAllText(artifact.Path.ToString(Context.PathTable), HostMaterializedFileContents[artifact]);
                    var result = await LocalDiskContentStore.TryStoreAsync(Cache.ArtifactContentCache, FileRealizationMode.Copy, artifact.Path, tryFlushPageCacheToFileSystem: false);
                    if (!result.Succeeded)
                    {
                        return result.Failure;
                    }

                    return new Possible<ContentMaterializationOrigin>(ContentMaterializationOrigin.DeployedFromCache);
                });
        }

        public bool IsFileRewritten(FileArtifact file)
        {
            return false;
        }

        public bool ShouldCreateHandleWithSequentialScan(FileArtifact file) => false;

        public bool TryGetProducerPip(in FileOrDirectoryArtifact artifact, out PipId producer)
        {
            throw new NotImplementedException();
        }

        public bool IsReachableFrom(PipId from, PipId to)
        {
            throw new NotImplementedException();
        }

        public Task<Optional<IEnumerable<AbsolutePath>>> GetReadPathsAsync(OperationContext context, Pip pip) => throw new NotImplementedException();

        public void ReportProblematicWorker()
        {
            throw new NotImplementedException();
        }

        public IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact artifact) => CollectionUtilities.EmptySet<FileArtifact>();
    }

    internal sealed class DummyServiceManager : ServiceManager
    {
        public override Task<bool> TryRunServiceDependenciesAsync(IPipExecutionEnvironment environment, PipId pipId, IEnumerable<PipId> servicePipDependencies, LoggingContext loggingContext)
        {
            return BoolTask.True;
        }
    }
}
