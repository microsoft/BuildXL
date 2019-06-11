// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Scheduler.FileSystem;
using Test.BuildXL.Scheduler.Utils;
using KextConnection = BuildXL.Processes.KextConnection;
using BuildXL.Processes.Containers;
using BuildXL.Utilities.VmCommandProxy;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipQueueTest : XunitBuildXLTest
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
                using (var pipQueue = new PipQueue(new ScheduleConfiguration()))
                {
                    pipQueue.SetAsFinalized();
                    pipQueue.DrainQueues();
                    XAssert.IsTrue(pipQueue.IsFinished);
                }
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
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
                    var executionEnvironment = new PipQueueTestExecutionEnvironment(context, config, pipTable, GetSandboxedKextConnection());

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

                    using (var phase1PipQueue = new PipQueue(executionEnvironment.Configuration.Schedule))
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
                                    using (var phase2PipQueue = new PipQueue(executionEnvironment.Configuration.Schedule))
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
                                                        PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "-c", "'", "cp", sourceArtifact, destinationArtifact, "'") :
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
            private readonly ConcurrentDictionary<FileArtifact, ContentHash> m_wellKnownFiles;
            private readonly ConcurrentDictionary<FileArtifact, Pip> m_producers;
            private readonly ConcurrentDictionary<Pip, Unit> m_executed = new ConcurrentDictionary<Pip, Unit>();

            private readonly TestPipGraphFilesystemView m_filesystemView;
            private readonly ConcurrentBigMap<DirectoryArtifact, int[]> m_sealContentsById;
            private readonly IKextConnection m_sandboxedKextConnection;

            private readonly IFileMonitoringViolationAnalyzer m_disabledFileMonitoringViolationAnalyzer = new DisabledFileMonitoringViolationAnalyzer();
            
            public PipQueueTestExecutionEnvironment(BuildXLContext context, IConfiguration configuration, PipTable pipTable, IKextConnection sandboxedKextConnection = null)
            {
                Contract.Requires(context != null);
                Contract.Requires(configuration != null);

                Context = context;
                LoggingContext = CreateLoggingContextForTest();
                Configuration = configuration;
                FileContentTable = FileContentTable.CreateNew();
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

                m_sandboxedKextConnection = sandboxedKextConnection;
                m_expectedWrittenContent = new ConcurrentDictionary<FileArtifact, ContentHash>();
                m_wellKnownFiles = new ConcurrentDictionary<FileArtifact, ContentHash>();
                m_producers = new ConcurrentDictionary<FileArtifact, Pip>();
                m_filesystemView = new TestPipGraphFilesystemView(Context.PathTable);
                var fileSystemView = new FileSystemView(Context.PathTable, m_filesystemView, LocalDiskContentStore);

                State = new PipExecutionState(
                    configuration,
                    cache: new PipTwoPhaseCache(LoggingContext, Cache, context, PathExpander),
                    unsafeConfiguration: configuration.Sandbox.UnsafeSandboxConfiguration,
                    preserveOutputsSalt: ContentHashingUtilities.CreateRandom(),
                    fileAccessWhitelist: FileAccessWhitelist,
                    directoryMembershipFingerprinter: this,
                    pathExpander: PathExpander,
                    executionLog: null,
                    fileSystemView: fileSystemView,
                    fileContentManager: new FileContentManager(this, new NullOperationTracker()),
                    directoryMembershipFinterprinterRuleSet: null);

                m_sealContentsById = new ConcurrentBigMap<DirectoryArtifact, int[]>();

                ProcessInContainerManager = new ProcessInContainerManager(LoggingContext, context.PathTable);
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

            public PipExecutionState State { get; }

            public PipTable PipTable { get; }

            public PipExecutionContext Context { get; }

            public FileContentTable FileContentTable { get; }

            public FileAccessWhitelist FileAccessWhitelist => null;

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

            public IFileMonitoringViolationAnalyzer FileMonitoringViolationAnalyzer => m_disabledFileMonitoringViolationAnalyzer;

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


            public void ReportMaterializedArtifact(in FileOrDirectoryArtifact artifact)
            {
                // Do nothing.
            }

            /// <inheritdoc />
            public void ReportFileArtifactPlaced(in FileArtifact artifact)
            {
                // Do nothing.
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
            public DirectoryTranslator DirectoryTranslator => null;

            /// <inheritdoc/>
            public LoggingContext LoggingContext { get; }

            /// <inheritdoc />
            public CounterCollection<PipExecutorCounter> Counters { get; } = new CounterCollection<PipExecutorCounter>();

            /// <inheritdoc />
            public IConfiguration Configuration { get; }

            /// <inheritdoc />
            public IReadOnlyDictionary<string, string> RootMappings => new Dictionary<string, string>();

            public IPipGraphFileSystemView PipGraphView => m_filesystemView;

            SealDirectoryKind IFileContentManagerHost.GetSealDirectoryKind(DirectoryArtifact directory)
            {
                return SealDirectoryKind.Partial;
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

            IArtifactContentCache IFileContentManagerHost.ArtifactContentCache => Cache.ArtifactContentCache;

            IExecutionLogTarget IFileContentManagerHost.ExecutionLog => State.ExecutionLog;

            SemanticPathExpander IFileContentManagerHost.SemanticPathExpander => PathExpander;

            public IKextConnection SandboxedKextConnection => m_sandboxedKextConnection;

            public ProcessInContainerManager ProcessInContainerManager { get; }

            public VmInitializer VmInitializer { get; }
        }
    }

    public class TestPipExecutor
    {
        /// <summary>
        /// Executes a pip.
        /// </summary>
        public static async Task<PipResult> ExecuteAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Pip pip,
            bool materializeInputs = false)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            PipResult result;

            // This method runs pips in isolation so we need to
            // hash inputs prior to running to ensure content information is populated
            // because report output will not be called for dependencies
            var maybeHashed = await environment.State.FileContentManager.TryHashDependenciesAsync(pip, operationContext);
            XAssert.IsTrue(maybeHashed.Succeeded, "Hashing inputs should succeed");

            ExecutionResult executionResult = null;
            DateTime start = DateTime.UtcNow;
            switch (pip.PipType)
            {
                case PipType.WriteFile:
                    result = await PipExecutor.ExecuteWriteFileAsync(operationContext, environment, (WriteFile)pip);
                    break;
                case PipType.CopyFile:
                    result = await PipExecutor.ExecuteCopyFileAsync(operationContext, environment, (CopyFile)pip);
                    break;
                case PipType.Process:
                    {
                        var pipScope = environment.State.GetScope((Process)pip);
                        var cacheableProcess = pipScope.GetCacheableProcess((Process)pip, environment);

                        var runnablePip = (ProcessRunnablePip)RunnablePip.Create(operationContext, environment, pip, 0, null);
                        runnablePip.Start(new OperationTracker(operationContext), operationContext);

                        var cacheResult = await PipExecutor.TryCheckProcessRunnableFromCacheAsync(runnablePip, pipScope, cacheableProcess);
                        if (cacheResult == null)
                        {
                            executionResult = ExecutionResult.GetFailureNotRunResult(operationContext);
                        }
                        else if (cacheResult.CanRunFromCache)
                        {
                            executionResult = await PipExecutor.RunFromCacheWithWarningsAsync(operationContext, environment, pipScope, (Process)pip, cacheResult, cacheableProcess.Description);
                        }
                        else
                        {
                            executionResult = await PipExecutor.ExecuteProcessAsync(operationContext, environment, pipScope, (Process)pip, cacheResult.Fingerprint);
                            executionResult.Seal();

                            executionResult = PipExecutor.AnalyzeFileAccessViolations(
                                operationContext,
                                environment,
                                pipScope,
                                executionResult,
                                cacheableProcess.Process,
                                out _,
                                out _);

                            executionResult = await PipExecutor.PostProcessExecution(operationContext, environment, pipScope, cacheableProcess, executionResult);
                            PipExecutor.ReportExecutionResultOutputContent(operationContext, environment, cacheableProcess.Description, executionResult);
                        }

                        result = RunnablePip.CreatePipResultFromExecutionResult(start, executionResult, withPerformanceInfo: true);
                        break;
                    }
                case PipType.Ipc:
                    var ipcResult = await PipExecutor.ExecuteIpcAsync(operationContext, environment, (IpcPip)pip);
                    PipExecutor.ReportExecutionResultOutputContent(
                        operationContext, 
                        environment, 
                        pip.GetDescription(environment.Context), 
                        ipcResult);
                    result = RunnablePip.CreatePipResultFromExecutionResult(start, ipcResult);
                    break;
                default:
                    throw Contract.AssertFailure("Do not know how to run this pip type:" + pip.PipType);
            }

            if (result.Status == PipResultStatus.NotMaterialized)
            {
                // Ensure outputs are materialized
                var materializationResult = await PipExecutor.MaterializeOutputsAsync(operationContext, environment, pip);
                if (executionResult != null)
                {
                    result = RunnablePip.CreatePipResultFromExecutionResult(start, executionResult.CloneSealedWithResult(materializationResult), withPerformanceInfo: true);
                }
                else
                {
                    result = new PipResult(
                        materializationResult,
                        result.PerformanceInfo,
                        result.MustBeConsideredPerpetuallyDirty,
                        result.DynamicallyObservedFiles,
                        result.DynamicallyObservedEnumerations);
                }
            }

            return result;
        }
    }

    public class TestPipGraphFilesystemView : IPipGraphFileSystemView
    {
        private readonly PathTable m_pathTable;

        public TestPipGraphFilesystemView(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        public bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItUnderSharedOpaque)
        {
            isItUnderSharedOpaque = false;
            return false;
        }

        public FileArtifact TryGetLatestFileArtifactForPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return FileArtifact.Invalid;
        }
    }
}
