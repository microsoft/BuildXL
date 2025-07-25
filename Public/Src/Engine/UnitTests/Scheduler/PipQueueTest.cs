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
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Scheduler.Distribution;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipQueueTest : TemporaryStorageTestBase
    {
        public PipQueueTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void Empty()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var pipTable = new PipTable(
                context.PathTable,
                context.SymbolTable,
                initialBufferSize: 1024,
                maxDegreeOfParallelism: (Environment.ProcessorCount + 2) / 3,
                debug: false))
            {
                using (var pipQueue = new PipQueue(LoggingContext, new ConfigurationImpl()))
                {
                    pipQueue.SetAsFinalized();
                    pipQueue.DrainQueues();
                    XAssert.IsTrue(pipQueue.IsFinished);
                }
            }
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task Stress()
        {
            const int N = 5;
            const int M = N * N;
            var context = BuildXLContext.CreateInstanceForTesting();
            var loggingContext = CreateLoggingContextForTest();
            var pathTable = context.PathTable;
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var config = ConfigHelpers.CreateDefault(pathTable, tempFiles.GetUniqueFileName(), tempFiles);

                using (var pipTable = new PipTable(
                    context.PathTable,
                    context.SymbolTable,
                    initialBufferSize: 1024,
                    maxDegreeOfParallelism: (Environment.ProcessorCount + 2) / 3,
                    debug: false))
                {
                    var executionEnvironment = new PipQueueTestExecutionEnvironment(
                        context,
                        config,
                        pipTable,
                        Path.Combine(TestOutputDirectory, "temp"),
                        TryGetSubstSourceAndTarget(out string substSource, out string substTarget) ? (substSource, substTarget) : default((string, string)?),
                        GetEBPFAwareSandboxConnection());

                    Func<RunnablePip, Task<PipResult>> taskFactory = async (runnablePip) =>
                        {
                            PipResult result;
                            var operationTracker = new OperationTracker(runnablePip.LoggingContext);
                            var pip = runnablePip.Pip;
                            using (var operationContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, pip.PipId, pip.PipType, runnablePip.LoggingContext))
                            {
                                result = await TestPipExecutor.ExecuteAsync(operationContext, executionEnvironment, pip);
                            }

                            executionEnvironment.MarkExecuted(pip);
                            return result;
                        };

                    string executable = CmdHelper.OsShellExe;
                    FileArtifact executableArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                    // This is the only file artifact we reference without a producer. Rather than scheduling a hashing pip, let's just invent one (so fingerprinting can succeed).
                    executionEnvironment.AddWellKnownFile(executableArtifact, WellKnownContentHashes.UntrackedFile);

                    using (var phase1PipQueue = new PipQueue(LoggingContext, executionEnvironment.Configuration))
                    {
                        // phase 1: create some files
                        var baseFileArtifacts = new List<FileArtifact>();
                        for (int i = 0; i < N; i++)
                        {
                            string destination = tempFiles.GetUniqueFileName();
                            AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);
                            FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();
                            baseFileArtifacts.Add(destinationArtifact);

                            PipData contents = PipDataBuilder.CreatePipData(
                                context.StringTable,
                                " ",
                                PipDataFragmentEscaping.CRuntimeArgumentRules,
                                i.ToString(CultureInfo.InvariantCulture));

                            var writeFile = new WriteFile(destinationArtifact, contents, WriteFileEncoding.Utf8, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(context));
                            var pipId = pipTable.Add((uint)(i + 1), writeFile);

                            var contentHash = ContentHashingUtilities.HashString(contents.ToString(pathTable));
                            executionEnvironment.AddExpectedWrite(writeFile, destinationArtifact, contentHash);

                            var runnable = RunnablePip.Create(loggingContext, executionEnvironment, pipId, pipTable.GetPipType(pipId), 0, taskFactory, 0);
                            runnable.Start(new OperationTracker(loggingContext), loggingContext);
                            runnable.SetDispatcherKind(DispatcherKind.IO);
                            phase1PipQueue.Enqueue(runnable);
                        }

                        phase1PipQueue.SetAsFinalized();
                        phase1PipQueue.DrainQueues();
                        await Task.WhenAll(
                            Enumerable.Range(0, 2).Select(
                                async range =>
                                {
                                    using (var phase2PipQueue = new PipQueue(LoggingContext, executionEnvironment.Configuration))
                                    {
                                        // phase 2: do some more with those files
                                        var pips = new ConcurrentDictionary<PipId, Tuple<string, int>>();
                                        var checkerTasks = new ConcurrentQueue<Task>();
                                        Action<PipId, Task<PipResult>> callback =
                                            (id, task) =>
                                            {
                                                XAssert.IsTrue(task.Status == TaskStatus.RanToCompletion);
                                                XAssert.IsFalse(task.Result.Status.IndicatesFailure());
                                                Tuple<string, int> t;
                                                if (!pips.TryRemove(id, out t))
                                                {
                                                    XAssert.Fail();
                                                }

                                                checkerTasks.Enqueue(
                                                    Task.Run(
                                                        () =>
                                                        {
                                                            string actual = File.ReadAllText(t.Item1).Trim();

                                                            // TODO: Make this async
                                                            XAssert.AreEqual(actual, t.Item2.ToString());
                                                        }));
                                            };
                                        var r = new Random(0);
                                        for (int i = 0; i < M; i++)
                                        {
                                            int sourceIndex = r.Next(baseFileArtifacts.Count);
                                            FileArtifact sourceArtifact = baseFileArtifacts[sourceIndex];

                                            string destination = tempFiles.GetUniqueFileName();
                                            AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);
                                            FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();
                                            Pip pip;

                                            DispatcherKind queueKind;
                                            switch (r.Next(2))
                                            {
                                                case 0:
                                                    pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(context));
                                                    queueKind = DispatcherKind.IO;
                                                    executionEnvironment.AddExpectedWrite(pip, destinationArtifact, executionEnvironment.GetExpectedContent(sourceArtifact));
                                                    break;
                                                case 1:
                                                    string workingDirectory = 
                                                        OperatingSystemHelper.IsUnixOS ? "/tmp" :
                                                        Environment.GetFolderPath( Environment.SpecialFolder.Windows);
                                                        
                                                    AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                                                    var pipData = OperatingSystemHelper.IsUnixOS ? 
                                                        PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.NoEscaping, "-c", "\"", "cp", sourceArtifact, destinationArtifact, "\"") :
                                                        PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "/d", "/c", "copy", "/B", sourceArtifact, destinationArtifact);

                                                    queueKind = DispatcherKind.CPU;
                                                    pip = new Process(
                                                        executableArtifact,
                                                        workingDirectoryAbsolutePath,
                                                        pipData,
                                                        FileArtifact.Invalid,
                                                        PipData.Invalid,
                                                        ReadOnlyArray<EnvironmentVariable>.Empty,
                                                        FileArtifact.Invalid,
                                                        FileArtifact.Invalid,
                                                        FileArtifact.Invalid,
                                                        tempFiles.GetUniqueDirectory(pathTable),
                                                        null,
                                                        null,
                                                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableArtifact, sourceArtifact),
                                                        ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationArtifact.WithAttributes()),
                                                        ReadOnlyArray<DirectoryArtifact>.Empty,
                                                        ReadOnlyArray<DirectoryArtifact>.Empty,
                                                        ReadOnlyArray<PipId>.Empty,
                                                        ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                                                        ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                                                        ReadOnlyArray<StringId>.Empty,
                                                        ReadOnlyArray<int>.Empty,
                                                        ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                                                        provenance: PipProvenance.CreateDummy(context),
                                                        toolDescription: StringId.Invalid,
                                                        additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);
                                                    executionEnvironment.AddExpectedWrite(pip, destinationArtifact, executionEnvironment.GetExpectedContent(sourceArtifact));
                                                    break;
                                                default:
                                                    Contract.Assert(false);
                                                    continue;
                                            }

                                            var pipId = pipTable.Add((uint)((range * M) + N + i + 1), pip);

                                            Func<RunnablePip, Task> taskFactoryWithCallback = async (runnablePip) =>
                                                {
                                                    var task = taskFactory(runnablePip);
                                                    var pipResult = await task;
                                                    callback(pipId, task);
                                                };

                                            var runnable = RunnablePip.Create(loggingContext, executionEnvironment, pipId, pipTable.GetPipType(pipId), 0, taskFactoryWithCallback, 0);
                                            runnable.Start(new OperationTracker(loggingContext), loggingContext);
                                            runnable.SetDispatcherKind(queueKind);
                                            phase2PipQueue.Enqueue(runnable);

                                            if (!pips.TryAdd(pipId, Tuple.Create(destination, sourceIndex)))
                                            {
                                                Contract.Assert(false);
                                            }
                                        }

                                        phase2PipQueue.SetAsFinalized();
                                        phase2PipQueue.DrainQueues();
                                        XAssert.AreEqual(0, pips.Count);
                                        await Task.WhenAll(checkerTasks);
                                    }
                                }));
                    }
                }
            }
        }

        /// <summary>
        /// Execution environment which allows setting expectations for reported outputs.
        /// </summary>
        private sealed class PipQueueTestExecutionEnvironment : IPipExecutionEnvironment, IDirectoryMembershipFingerprinter, IFileContentManagerHost
        {
            // Some pip implementations may query for the hashes of their input files.
            private readonly ConcurrentDictionary<FileArtifact, ContentHash> m_expectedWrittenContent;
            private readonly ConcurrentDictionary<FileArtifact, Pip> m_producers;
            private readonly ConcurrentDictionary<Pip, Unit> m_executed = new ConcurrentDictionary<Pip, Unit>();

            private readonly TestPipGraphFilesystemView m_filesystemView;

            public PipQueueTestExecutionEnvironment(
                BuildXLContext context,
                IConfiguration configuration,
                PipTable pipTable,
                string tempDirectory,
                (string substSource, string substTarget)? subst = default,
                ISandboxConnection sandboxConnection = null)
            {
                Contract.Requires(context != null);
                Contract.Requires(configuration != null);

                Context = context;
                LoggingContext = CreateLoggingContextForTest();
                Configuration = configuration;
                FileContentTable = FileContentTable.CreateNew(LoggingContext);
                ContentFingerprinter = new PipContentFingerprinter(
                    context.PathTable,
                    artifact => State.FileContentManager.GetInputContent(artifact).FileContentInfo,
                    ExtraFingerprintSalts.Default(),
                    pathExpander: PathExpander);
                PipTable = pipTable;
                PipFragmentRenderer = this.CreatePipFragmentRenderer();
                IpcProvider = IpcFactory.GetProvider();
                var tracker = FileChangeTracker.CreateDisabledTracker(LoggingContext);
                Cache = InMemoryCacheFactory.Create();
                LocalDiskContentStore = new LocalDiskContentStore(LoggingContext, context.PathTable, FileContentTable, tracker);

                SandboxConnection = sandboxConnection;
                m_expectedWrittenContent = new ConcurrentDictionary<FileArtifact, ContentHash>();
                m_producers = new ConcurrentDictionary<FileArtifact, Pip>();
                m_filesystemView = new TestPipGraphFilesystemView(Context.PathTable);
                var fileSystemView = new FileSystemView(Context.PathTable, m_filesystemView, LocalDiskContentStore);
                TempCleaner = new TestMoveDeleteCleaner(tempDirectory);

                State = new PipExecutionState(
                    configuration,
                    LoggingContext,
                    cache: new PipTwoPhaseCache(LoggingContext, Cache, context, PathExpander),
                    unsafeConfiguration: configuration.Sandbox.UnsafeSandboxConfiguration,
                    preserveOutputsSalt: new PreserveOutputsInfo(ContentHashingUtilities.CreateRandom(), Configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputsTrustLevel),
                    fileAccessAllowlist: FileAccessAllowlist,
                    directoryMembershipFingerprinter: this,
                    pathExpander: PathExpander,
                    executionLog: null,
                    fileSystemView: fileSystemView,
                    fileContentManager: new FileContentManager(this, new NullOperationTracker()),
                    directoryMembershipFinterprinterRuleSet: null,
                    sidebandState: null,
                    alienFileEnumerationCache: new ConcurrentBigMap<AbsolutePath, IReadOnlyList<(AbsolutePath, string)>>(),
                    fileTimestampTracker: new FileTimestampTracker(DateTime.UtcNow, context.PathTable),
                    globalReclassificationRules: new ObservationReclassifier());

                DirectoryTranslator = new DirectoryTranslator();
                foreach (var directoryToTranslate in configuration.Engine.DirectoriesToTranslate)
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

            public void AddExpectedWrite(Pip producer, FileArtifact file, ContentHash expectedContent)
            {
                Contract.Assume(!m_executed.ContainsKey(producer));

                bool added = m_producers.TryAdd(file, producer);
                XAssert.IsTrue(added, "File already had an expectation set");

                added = m_expectedWrittenContent.TryAdd(file, expectedContent);
                XAssert.IsTrue(added, "File already had an expectation set");
            }

            public void MarkExecuted(Pip executed)
            {
                bool added = m_executed.TryAdd(executed, Unit.Void);
                XAssert.IsTrue(added, "Executed multiple times");
            }

            public ContentHash GetExpectedContent(FileArtifact file)
            {
                return m_expectedWrittenContent[file];
            }

            public bool TryGetProducerPip(in FileOrDirectoryArtifact artifact, out PipId producer)
            {
                producer = PipId.Invalid;
                if (artifact.IsDirectory) return false;
                var found = m_producers.TryGetValue(artifact.FileArtifact, out var pip);
                if (found) producer = pip.PipId;
                return found;
            }

            public bool IsReachableFrom(PipId from, PipId to)
            {
                throw new NotImplementedException();
            }

            public PipExecutionState State { get; }

            public PipTable PipTable { get; }

            public PipExecutionContext Context { get; }

            public FileContentTable FileContentTable { get; }

            public FileAccessAllowlist FileAccessAllowlist => null;

            /// <summary>
            /// In-memory cache (not the real one)
            /// </summary>
            public EngineCache Cache { get; }

            /// <inheritdoc />
            public LocalDiskContentStore LocalDiskContentStore { get; }

            public PipContentFingerprinter ContentFingerprinter { get; }

            public PipFragmentRenderer PipFragmentRenderer { get; }

            public IIpcProvider IpcProvider { get; }

            public SemanticPathExpander PathExpander => SemanticPathExpander.Default;

            public IFileMonitoringViolationAnalyzer FileMonitoringViolationAnalyzer { get; } = new DisabledFileMonitoringViolationAnalyzer();

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

            public DirectoryFingerprint? TryComputeDirectoryFingerprint(
                AbsolutePath directoryPath, 
                CacheablePipInfo cachePipInfo, 
                Func<EnumerationRequest, PathExistence?> tryEnumerateDirectory, 
                bool cacheableFingerprint, 
                global::BuildXL.Scheduler.DirectoryMembershipFingerprinterRule rule,
                DirectoryMembershipHashedEventData eventData)
            {
                Contract.Requires(directoryPath.IsValid);
                return DirectoryFingerprint.Zero;
            }

            public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact)
            {
                XAssert.Fail("Shouldn't be called since no sealed directories are introduced");
                return default(SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>);
            }

            public bool IsSourceSealedDirectory(DirectoryArtifact directoryArtifact, out bool allDirectories, out ReadOnlyArray<StringId> patterns)
            {
                Contract.Requires(directoryArtifact.IsValid);
                allDirectories = false;
                patterns = ReadOnlyArray<StringId>.Empty;
                return false;
            }

            public bool TryGetCopySourceFile(FileArtifact artifact, out FileArtifact sourceFile)
            {
                sourceFile = FileArtifact.Invalid;
                return false;
            }

            /// <inheritdoc/>
            public SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directoryArtifact)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public bool ShouldScrubFullSealDirectory(DirectoryArtifact directoryArtifact)
            {
                throw new NotImplementedException();
            }

            public void AddWellKnownFile(FileArtifact artifact, ContentHash hash)
            {
                State.FileContentManager.ReportInputContent(artifact, FileMaterializationInfo.CreateWithUnknownLength(hash));
            }

            public bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact fileArtifact)
            {
                return false;
            }

            /// <inheritdoc />
            public bool IsFileRewritten(in FileArtifact fileArtifact)
            {
                return false;
            }

            public void ReportContent(FileArtifact artifact, in FileMaterializationInfo trackedFileContentInfo, PipOutputOrigin origin)
            {
                if (!artifact.IsOutputFile)
                {
                    return;
                }

                XAssert.IsTrue(m_producers.ContainsKey(artifact), "Unknown file artifact {0}", artifact.Path.ToString(Context.PathTable));
                XAssert.IsFalse(m_executed.ContainsKey(m_producers[artifact]), "Producer has already executed yet for {0}", artifact.Path.ToString(Context.PathTable));

                ContentHash expected = m_expectedWrittenContent[artifact];
                XAssert.AreEqual(expected, trackedFileContentInfo.Hash, "Wrong hash reported for an output");
            }

            /// <inheritdoc />
            public void ReportDynamicOutputFile(FileArtifact path)
            {
                // Do nothing.
            }


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

            public void ReportCacheDescriptorHit(string sourceCache)
            {
                Contract.Requires(!string.IsNullOrWhiteSpace(sourceCache));

                // Do Nothing.
            }

            public bool ShouldHaveArtificialMiss(Pip pip)
            {
                Contract.Requires(pip != null);
                return false;
            }

            public void ReportWarnings(bool fromCache, int count)
            {
                Contract.Requires(count > 0);
                XAssert.Fail("Shouldn't be called since no warnings are introduced");
            }

            public int GetPipPriority(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return 0;
            }

            public bool AllowArtifactReadOnly(in FileOrDirectoryArtifact artifact)
            {
                return true;
            }

            public IEnumerable<Pip> GetServicePipClients(PipId pipId)
            {
                return null;
            }

            /// <inheritdoc/>
            public DirectoryTranslator DirectoryTranslator { get; }

            /// <inheritdoc/>
            public LoggingContext LoggingContext { get; }

            /// <inheritdoc />
            public CounterCollection<PipExecutorCounter> Counters { get; } = new CounterCollection<PipExecutorCounter>();

            /// <inheritdoc />
            public IConfiguration Configuration { get; }

            /// <inheritdoc />
            public IReadOnlyDictionary<string, string> RootMappings => new Dictionary<string, string>();

            public IPipGraphFileSystemView PipGraphView => m_filesystemView;

            /// <inheritdoc />
            public PipSpecificPropertiesConfig PipSpecificPropertiesConfig { get; set; }

            SealDirectoryKind IFileContentManagerHost.GetSealDirectoryKind(DirectoryArtifact directory)
            {
                return SealDirectoryKind.Partial;
            }

            bool IFileContentManagerHost.TryGetSourceSealDirectory(DirectoryArtifact directory, out SourceSealWithPatterns sourceSealWithPatterns)
            {
                sourceSealWithPatterns = default;

                if (IsSourceSealedDirectory(directory, out bool allDirectories, out ReadOnlyArray<StringId> patterns))
                {
                    sourceSealWithPatterns = new SourceSealWithPatterns(directory.Path, patterns, !allDirectories);
                    return true;
                }

                return false;
            }

            PipId IFileContentManagerHost.TryGetProducerId(in FileOrDirectoryArtifact artifact)
            {
                return PipId.Invalid;
            }

            Pip IFileContentManagerHost.GetProducer(in FileOrDirectoryArtifact artifact)
            {
                Contract.Assert(artifact.IsFile, "This method is not expected to be called with file. If that changes producers should also contain directory artifacts");
                return m_producers[artifact.FileArtifact];
            }

            string IFileContentManagerHost.GetProducerDescription(in FileOrDirectoryArtifact artifact)
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

            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> IFileContentManagerHost.ListSealDirectoryContents(DirectoryArtifact directory)
                => ListSealedDirectoryContents(directory);

            public void SetMaxExternalProcessRan()
            {
                // Do Nothing.
            }

            public bool CanMaterializeFile(FileArtifact artifact)
            {
                return false;
            }

            public Task<Possible<ContentMaterializationOrigin>> TryMaterializeFileAsync(FileArtifact artifact, OperationContext operationContext)
            {
                throw new NotImplementedException();
            }

            public bool IsFileRewritten(FileArtifact file)
            {
                return false;
            }

            public bool ShouldCreateHandleWithSequentialScan(FileArtifact file) => false;
            public Task<Optional<IEnumerable<AbsolutePath>>> GetReadPathsAsync(OperationContext context, Pip pip) => throw new NotImplementedException();

            public void ReportProblematicWorker()
            {
                throw new NotImplementedException();
            }

            public IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact artifact) => CollectionUtilities.EmptySet<FileArtifact>();

            IArtifactContentCache IFileContentManagerHost.ArtifactContentCache => Cache.ArtifactContentCache;

            IExecutionLogTarget IFileContentManagerHost.ExecutionLog => State.ExecutionLog;

            SemanticPathExpander IFileContentManagerHost.SemanticPathExpander => PathExpander;

            public ISandboxConnection SandboxConnection { get; }

            public VmInitializer VmInitializer { get; }

            public IRemoteProcessManager RemoteProcessManager { get; }

            public ITempCleaner TempCleaner { get; }

            public ReparsePointResolver ReparsePointAccessResolver => null;

            public PluginManager PluginManager { get; }

            public IReadOnlySet<AbsolutePath> TranslatedGlobalUnsafeUntrackedScopes => CollectionUtilities.EmptySet<AbsolutePath>();

            public SchedulerTestHooks SchedulerTestHooks { get; }

            public bool HasFailed => throw new NotImplementedException();

            public bool IsRamProjectionActive => true;

            public LocalWorker LocalWorker { get; }
        }
    }
}
