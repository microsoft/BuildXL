// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL;
using BuildXL.App.Tracing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Sideband;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using ProcessOutputs = BuildXL.Pips.Builders.ProcessOutputs;
using BuildXL.Utilities.VmCommandProxy;
using BuildXL.Scheduler.Fingerprints;
using System;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Base class for scheduler integration tests.
    /// </summary>
    public class SchedulerIntegrationTestBase : PipTestBase
    {
        public List<ScheduleRunResult> PriorResults = new List<ScheduleRunResult>();
        public CommandLineConfiguration Configuration { get; set; }
        public EngineCache Cache { get; set; }
        public FileContentTable FileContentTable { get; set; }
        public DirectoryTranslator DirectoryTranslator = new DirectoryTranslator();

        // Keep track of whether the graph was changed between runs of the scheduler for sake of passing the same graph and
        // graphid to tests to allow exercising incremental scheduling.
        private bool m_graphWasModified;

        /// <summary>
        /// Sanity check to make sure something was scheduled and avoid empty testing
        /// </summary>
        private bool m_graphWasEverModified;

        public PipGraph LastGraph { get; private set; }

        private JournalState m_journalState;
        private readonly ITestOutputHelper m_testOutputHelper;

        /// <summary>
        /// Whether the scheduler should log all of its statistics at the end of every run.
        /// </summary>
        public bool ShouldLogSchedulerStats { get; set; } = false;

        public bool ShouldCreateLogDir { get; set; } = false;

        /// <summary>
        /// Class for storing pip graph setup.
        /// </summary>
        public class PipGraphSetupData : PipTestBaseSetupData
        {
            private readonly PipGraph m_pipGraph;
            private readonly SchedulerIntegrationTestBase m_testBase;

            private PipGraphSetupData(SchedulerIntegrationTestBase testBase)
                : base(testBase)
            {
                m_pipGraph = testBase.LastGraph;
                m_testBase = testBase;
            }

            public static PipGraphSetupData Save(SchedulerIntegrationTestBase testBase) => new PipGraphSetupData(testBase);

            public override void Restore()
            {
                m_testBase.LastGraph = m_pipGraph;
                m_testBase.m_graphWasModified = false;
                base.Restore();
            }
        }

        /// <nodoc/>
        public SchedulerIntegrationTestBase(ITestOutputHelper output) : base(output)
        {
            m_testOutputHelper = output;

            // Each event listener that we want to capture events from must be listed here
            foreach (var eventSource in BuildXLApp.GeneratedEventSources)
            {
                RegisterEventSource(eventSource);
            }

            // Go through the command line configuration handling used by the real app to get the appropriate defaults
            ICommandLineConfiguration config;
            XAssert.IsTrue(Args.TryParseArguments(new[] { "/c:" + Path.Combine(TemporaryDirectory, "config.dc") }, Context.PathTable, null, out config), "Failed to construct arguments");
            Configuration = new CommandLineConfiguration(config);

            Cache = InMemoryCacheFactory.Create();

            FileContentTable = FileContentTable.CreateNew(LoggingContext);

            // Disable defaults that write disk IO but are not critical to correctness or performance
            Configuration.Logging.StoreFingerprints = false;
            Configuration.Logging.LogExecution = false;
            Configuration.Engine.TrackBuildsInUserFolder = false;
            Configuration.Engine.CleanTempDirectories = false;
            Configuration.Sandbox.EnsureTempDirectoriesExistenceBeforePipExecution = true;

            // Skip hash source file pips
            // Some error becomes a contract exception when SkipHashSourceFile is enabled.
            // Mostly on methods that traverse graph, e.g., DirectedGraph.GetOutgoingEdges();
            // BUG 1472567
            // Configuration.Schedule.SkipHashSourceFile = true;

            // Compute static pip fingerprints for incremental scheduling tests.
            Configuration.Schedule.ComputePipStaticFingerprints = true;

            // Disable currently enabled unsafe option.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreCreateProcessReport = false;

            // Populate file system capabilities.
            // Here, for example, we use copy-on-write instead of hardlinks when Unix file system supports copy-on-write.
            // Particular tests can override this by setting Configuration.Engine.UseHardlinks.
            BuildXLEngine.PopulateFileSystemCapabilities(Configuration, Configuration, Context.PathTable, LoggingContext);

            // Reset pip graph builder to use the populated configuration.
            ResetPipGraphBuilder();
        }

        /// <summary>
        /// Saves current pip graph setup.
        /// </summary>
        public PipGraphSetupData SavePipGraph() => PipGraphSetupData.Save(this);

        /// <summary>
        /// Resets pip graph builder using populated configuration.
        /// </summary>
        public void ResetPipGraphBuilder() => ResetPipGraphBuilder(Configuration);

        /// <summary>
        /// Clears all content from the artifact cache
        /// </summary>
        public void ClearArtifactCache()
        {
            var artifactContentCacheForTest = Cache.ArtifactContentCache as IArtifactContentCacheForTest;
            artifactContentCacheForTest?.Clear();
        }

        /// <summary>
        /// Finds the cache sites of a content in artifact cache.
        /// </summary>
        private CacheSites FindContainingSitesInArtifactCache(ContentHash contentHash)
        {
            var artifactContentCacheForTest = Cache.ArtifactContentCache as IArtifactContentCacheForTest;
            return artifactContentCacheForTest?.FindContainingSites(contentHash) ?? CacheSites.None;
        }

        /// <summary>
        /// Checks if content exists in the artifact cache.
        /// </summary>
        private bool ExistsInArtifactCache(ContentHash contentHash)
        {
            return FindContainingSitesInArtifactCache(contentHash) != CacheSites.None;
        }

        /// <summary>
        /// Checks if file content exists in the artifact cache.
        /// </summary>
        public bool FileContentExistsInArtifactCache(FileArtifact file)
        {
            ContentHash hash = ContentHashingUtilities.HashFileAsync(file.Path.ToString(Context.PathTable)).Result;
            return ExistsInArtifactCache(hash);
        }

        /// <summary>
        /// Checks if string content exists in the artifact cache.
        /// </summary>
        public bool ContentExistsInArtifactCache(string content)
        {
            ContentHash hash = ContentHashingUtilities.HashString(content);
            return ExistsInArtifactCache(hash);
        }

        /// <summary>
        /// Discards content in artifact cache if exists.
        /// </summary>
        public void DiscardContentInArtifactCacheIfExists(ContentHash contentHash, CacheSites sites = CacheSites.LocalAndRemote)
        {
            if (ExistsInArtifactCache(contentHash))
            {
                var artifactContentCacheForTest = Cache.ArtifactContentCache as IArtifactContentCacheForTest;
                if (artifactContentCacheForTest != null)
                {
                    artifactContentCacheForTest.DiscardContentIfPresent(contentHash, sites);
                }
            }
        }

        /// <summary>
        /// Discards file content in artifact cache if exists.
        /// </summary>
        public void DiscardFileContentInArtifactCacheIfExists(FileArtifact file, CacheSites sites = CacheSites.LocalAndRemote)
        {
            ContentHash hash = ContentHashingUtilities.HashFileAsync(file.Path.ToString(Context.PathTable)).Result;
            DiscardContentInArtifactCacheIfExists(hash, sites);
        }

        public FileArtifact WriteFile(AbsolutePath destination, PipDataAtom content, WriteFileEncoding? encoding = null)
        {
            PipDataBuilder pipDataBuilder = new PipDataBuilder(Context.StringTable);
            pipDataBuilder.Add(content);

            if (!PipConstructionHelper.TryWriteFile(
                destination,
                pipDataBuilder.ToPipData(Context.StringTable.Empty, PipDataFragmentEscaping.NoEscaping),
                encoding ?? WriteFileEncoding.Utf8,
                null,
                null,
                out var result))
            {
                throw new BuildXLTestException("Failed to add writefile pip");
            }

            return result;
        }

        public FileArtifact WriteFile(AbsolutePath destination, string content, WriteFileEncoding? encoding = null) => WriteFile(destination, PipDataAtom.FromString(content), encoding);

        public FileArtifact CopyFile(
            FileArtifact source,
            AbsolutePath destination,
            string description = null,
            string[] tags = null,
            CopyFile.Options options = default)
        {
            if (!PipConstructionHelper.TryCopyFile(
                source,
                destination,
                global::BuildXL.Pips.Operations.CopyFile.Options.None,
                tags,
                description,
                out var result))
            {
                throw new BuildXLTestException("Failed to add copyfile pip");
            }

            return result;
        }

        public DirectoryArtifact SealDirectory(
            AbsolutePath directoryRoot,
            IReadOnlyList<FileArtifact> contents,
            SealDirectoryKind kind,
            string[] tags = null,
            string description = null,
            string[] patterns = null,
            bool scrub = false)
        {
            if (!PipConstructionHelper.TrySealDirectory(
                directoryRoot,
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                    contents,
                    OrdinalFileArtifactComparer.Instance),
                CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                kind,
                tags,
                description,
                patterns,
                out DirectoryArtifact sealedDirectory,
                scrub))
            {
                throw new BuildXLTestException("Failed to add sealDirectory pip");
            }

            return sealedDirectory;
        }

        public DirectoryArtifact SealDirectory(
            AbsolutePath directoryRoot,
            SealDirectoryKind kind,
            params FileArtifact[] contents)
        {
            return SealDirectory(directoryRoot, contents, kind);
        }

        public DirectoryArtifact SealDirectory(
            AbsolutePath directoryRoot,
            SealDirectoryKind kind,
            bool scrub = false,
            params FileArtifact[] contents)
        {
            return SealDirectory(directoryRoot, contents, kind, null, null, null, scrub);
        }

        public ProcessWithOutputs SchedulePipBuilder(ProcessBuilder builder)
        {
            AddUntrackedWindowsDirectories(builder);

            if (!builder.TryFinish(PipConstructionHelper, out var process, out var outputs))
            {
                throw new BuildXLTestException("Failed to construct process pip");
            }

            if (!PipGraphBuilder.AddProcess(process))
            {
                throw new BuildXLTestException("Failed to add process pip");
            }

            return new ProcessWithOutputs(process, outputs);
        }

        public readonly struct ProcessWithOutputs
        {
            public Process Process { get; }
            public ProcessOutputs ProcessOutputs { get; }

            public ProcessWithOutputs(Process process, ProcessOutputs processOutputs)
            {
                Process = process;
                ProcessOutputs = processOutputs;
            }

        }

        /// <summary>
        /// Convenience function that creates and schedules a <see cref="PipBuilder"/> constructed process with an arbitrary output file.
        /// This is the smallest pip that can be scheduled.
        /// </summary>
        public ProcessWithOutputs CreateAndSchedulePipBuilderWithArbitraryOutput(IEnumerable<string> tags = null, string description = null)
        {
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(CreateOutputFileArtifact()) }, tags, description);
            return SchedulePipBuilder(pipBuilder);
        }

        /// <summary>
        /// Creates and scheduled a <see cref="PipBuilder"/> constructed process
        /// </summary>
        public ProcessWithOutputs CreateAndSchedulePipBuilder(IEnumerable<Operation> processOperations, IEnumerable<string> tags = null, string description = null, IDictionary<string, string> environmentVariables = null)
        {
            var pipBuilder = CreatePipBuilder(processOperations, tags, description, environmentVariables);
            return SchedulePipBuilder(pipBuilder);
        }

        /// <summary>
        /// Runs the scheduler using the instance member PipGraph and Configuration objects. This will also carry over
        /// any state from any previous run such as the cache
        /// </summary>
        public ScheduleRunResult RunScheduler(
            SchedulerTestHooks testHooks = null,
            SchedulerState schedulerState = null,
            RootFilter filter = null,
            ITempCleaner tempCleaner = null,
            IEnumerable<(Pip before, Pip after)> constraintExecutionOrder = null,
            PerformanceCollector performanceCollector = null,
            bool updateStatusTimerEnabled = false,
            Action<TestScheduler> verifySchedulerPostRun = default,
            string runNameOrDescription = null,
            bool allowEmptySchedule = false,
            CancellationToken cancellationToken = default)
        {
            if (m_graphWasModified || LastGraph == null)
            {
                LastGraph = PipGraphBuilder.Build();
                XAssert.IsNotNull(LastGraph, "Failed to build pip graph");
            }

            m_graphWasModified = false;

            return RunSchedulerSpecific(
                LastGraph,
                tempCleaner ?? MoveDeleteCleaner,
                testHooks,
                schedulerState,
                filter,
                constraintExecutionOrder,
                performanceCollector: performanceCollector,
                updateStatusTimerEnabled: updateStatusTimerEnabled,
                verifySchedulerPostRun: verifySchedulerPostRun,
                runNameOrDescription: runNameOrDescription,
                allowEmptySchedule: allowEmptySchedule,
                cancellationToken: cancellationToken);
        }

        public NodeId GetProducerNode(FileArtifact file) => PipGraphBuilder.GetProducerNode(file);

        /// <summary>
        /// Special accessor for the PipGraphBuilder so the test can determine if the graph was changed between builds.
        /// </summary>
        protected override PipGraph.Builder PipGraphBuilder
        {
            get
            {
                m_graphWasModified = true;
                m_graphWasEverModified = true;
                return base.PipGraphBuilder;
            }
        }

        private void MarkSchedulerRun(string runNameOrDescription = null)
        {
            m_testOutputHelper.WriteLine("################################################################################");
            m_testOutputHelper.WriteLine($"## {nameof(RunSchedulerSpecific)} {runNameOrDescription ?? string.Empty}");
            m_testOutputHelper.WriteLine("################################################################################");
        }

        /// <summary>
        /// Runs the scheduler allowing various options to be specifically set
        /// </summary>
        public ScheduleRunResult RunSchedulerSpecific(
            PipGraph graph,
            ITempCleaner tempCleaner,
            SchedulerTestHooks testHooks = null,
            SchedulerState schedulerState = null,
            RootFilter filter = null,
            IEnumerable<(Pip before, Pip after)> constraintExecutionOrder = null,
            string runNameOrDescription = null,
            PerformanceCollector performanceCollector = null,
            bool updateStatusTimerEnabled = false,
            Action<TestScheduler> verifySchedulerPostRun = default,
            bool allowEmptySchedule = false,
            CancellationToken cancellationToken = default)
        {
            XAssert.IsTrue(m_graphWasEverModified || allowEmptySchedule,
    "Attempting to run an empty scheduler. This usually means you forgot to schedule the pips in the test case. Suppress this failure by passing allowEmptySchedule = true");

            MarkSchedulerRun(runNameOrDescription);

            // This is a new logging context to be used just for this instantiation of the scheduler. That way it can
            // be validated against the LoggingContext to make sure the scheduler's return result and error logging
            // are in agreement.
            var localLoggingContext = CreateLoggingContextForTest();
            var config = new CommandLineConfiguration(Configuration);

            // By default we allow out of mount writes for tests, unless specified otherwise.
            config.Engine.UnsafeAllowOutOfMountWrites ??= true;

            // Populating the configuration may modify the configuration, so it should occur first.
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(config, Context.PathTable, bxlExeLocation: null, inTestMode: true);
            if (!BuildXLEngine.PopulateAndValidateConfiguration(config, config, Context.PathTable, LoggingContext))
            {
                return null;
            }

            FileAccessAllowlist allowlist = new FileAccessAllowlist(Context);
            allowlist.Initialize(config);

            IReadOnlyList<string> junctionRoots = Configuration.Engine.DirectoriesToTranslate?.Select(a => a.ToPath.ToString(Context.PathTable)).ToList();

            var map = JournalUtils.TryCreateMapOfAllLocalVolumes(localLoggingContext, junctionRoots);
            var maybeAccessor = TryGetJournalAccessor(map);

            // Although scan change journal is enabled, but if we cannot create an enabled journal accessor, then create a disabled one.
            m_journalState = map == null || !maybeAccessor.Succeeded
                ? JournalState.DisabledJournal
                : JournalState.CreateEnabledJournal(map, maybeAccessor.Result);

            if (config.Schedule.IncrementalScheduling)
            {
                // Ensure that we can scan the journal when incremental scheduling is enabled.
                XAssert.IsTrue(m_journalState.IsEnabled, "Incremental scheduling requires that journal is enabled");
            }

            (string drive, string path)? subst = null;
            if (!DirectoryTranslator.Sealed && TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                DirectoryTranslator.AddTranslation(substSource, substTarget);
                subst = FileUtilities.GetSubstDriveAndPath(substSource, substTarget);
            }

            DirectoryTranslator.AddDirectoryTranslationFromEnvironment();

            // Seal the translator if not sealed yet
            DirectoryTranslator.Seal();

            // .....................................................................................
            // some dummy setup in order to get a PreserveOutputsInfo.txt file and an actual salt
            // .....................................................................................
            string dummyCacheDir = Path.Combine(TemporaryDirectory, "Out", "Cache");
            Directory.CreateDirectory(dummyCacheDir); // EngineSchedule tries to put the PreserveOutputsInfo.txt here
            PreserveOutputsInfo? previousOutputsSalt =
                EngineSchedule.PreparePreviousOutputsSalt(localLoggingContext, Context.PathTable, config);
            Contract.Assert(previousOutputsSalt.HasValue);
            // .....................................................................................

            testHooks ??= new SchedulerTestHooks();
            testHooks.FingerprintStoreTestHooks ??= new FingerprintStoreTestHooks();
            Contract.Assert(!(config.Engine.CleanTempDirectories && tempCleaner == null));

            using (var queue = new PipQueue(LoggingContext, config))
            using (var testQueue = new TestPipQueue(queue, localLoggingContext, initiallyPaused: constraintExecutionOrder != null))
            using (var testScheduler = new TestScheduler(
                graph: graph,
                pipQueue: constraintExecutionOrder == null ?
                            testQueue :
                            constraintExecutionOrder.Aggregate(testQueue, (TestPipQueue testQueue, (Pip before, Pip after) constraint) => { testQueue.ConstrainExecutionOrder(constraint.before, constraint.after); return testQueue; }).Unpause(),
                context: BuildXLContext.CreateInstanceForTestingWithCancellationToken(Context, cancellationToken),
                fileContentTable: FileContentTable,
                loggingContext: localLoggingContext,
                cache: Cache,
                configuration: config,
                journalState: m_journalState,
                fileAccessAllowlist: allowlist,
                fingerprintSalt: Configuration.Cache.CacheSalt,
                directoryMembershipFingerprinterRules: new DirectoryMembershipFingerprinterRuleSet(Configuration, Context.StringTable),
                tempCleaner: tempCleaner,
                previousInputsSalt: previousOutputsSalt.Value,
                successfulPips: null,
                failedPips: null,
                ipcProvider: null,
                directoryTranslator: DirectoryTranslator,
                vmInitializer: VmInitializer.CreateFromEngine(
                    config.Layout.BuildEngineDirectory.ToString(Context.PathTable),
                    config.Layout.ExternalSandboxedProcessDirectory.ToString(Context.PathTable),
                    subst: subst), // VM command proxy for unit tests comes from engine.
                testHooks: testHooks,
                performanceCollector: performanceCollector))
            {
                CancellableTimedAction updateStatusAction = null;

                MountPathExpander mountPathExpander = null;
                var frontEndNonScrubbablePaths = CollectionUtilities.EmptyArray<string>();

                if (filter == null)
                {
                    EngineSchedule.TryGetPipFilter(localLoggingContext, Context, config, config, Expander.TryGetRootByMountName, out filter);
                }

                var nonScrubbablePaths = EngineSchedule.GetNonScrubbablePaths(Context.PathTable, config, frontEndNonScrubbablePaths, tempCleaner);
                EngineSchedule.ScrubExtraneousFilesAndDirectories(mountPathExpander, testScheduler, localLoggingContext, config, nonScrubbablePaths, tempCleaner, filter);

                XAssert.IsTrue(testScheduler.InitForOrchestrator(localLoggingContext, filter, schedulerState), "Failed to initialized test scheduler");

                if (ShouldCreateLogDir || ShouldLogSchedulerStats)
                {
                    var logsDir = config.Logging.LogsDirectory.ToString(Context.PathTable);
                    Directory.CreateDirectory(logsDir);
                }

                testScheduler.Start(localLoggingContext);
                testScheduler.UpdateStatus();

                if (updateStatusTimerEnabled)
                {
                    updateStatusAction = new CancellableTimedAction(
                        () => testScheduler.UpdateStatus(overwriteable: true, expectedCallbackFrequency: 1000),
                        1000,
                        "SchedulerUpdateStatus");
                    updateStatusAction.Start();
                }

                bool success = testScheduler.WhenDone().GetAwaiter().GetResult();

                // Only save file change tracking information for incremental scheduling tests in order to reduce I/O
                if (Configuration.Schedule.IncrementalScheduling)
                {
                    testScheduler.SaveFileChangeTrackerAsync(localLoggingContext).Wait();
                }

                if (ShouldLogSchedulerStats)
                {
                    // Logs are not written out normally during these tests, but LogStats depends on the existence of the logs directory
                    // to write out the stats perf JSON file
                    testScheduler.LogStats(localLoggingContext, null);
                }

                // Verify internal data of scheduler.
                verifySchedulerPostRun?.Invoke(testScheduler);
                
                var runResult = new ScheduleRunResult
                {
                    Graph = graph,
                    Config = config,
                    Success = success,
                    RunData = testScheduler.RunData,
                    PipExecutorCounters = testScheduler.PipExecutionCounters,
                    ProcessPipCountersByFilter = testScheduler.ProcessPipCountersByFilter,
                    ProcessPipCountersByTelemetryTag = testScheduler.ProcessPipCountersByTelemetryTag,
                    SchedulerState = new SchedulerState(testScheduler),
                    Session = localLoggingContext.Session,
                    FileSystemView = testScheduler.State?.FileSystemView
                };

                runResult.AssertSuccessMatchesLogging(localLoggingContext);

                // Prmote this run's specific LoggingContext into the test's LoggingContext.
                LoggingContext.AbsorbLoggingContextState(localLoggingContext);

                updateStatusAction?.Cancel();
                updateStatusAction?.Join();

                return runResult;
            }
        }

        private Possible<global::BuildXL.Storage.ChangeJournalService.IChangeJournalAccessor> TryGetJournalAccessor(VolumeMap map)
        {
            return map.Volumes.Any()
                ? JournalUtils.TryGetJournalAccessorForTest(map)
                : new Failure<string>("Invalid");
        }

        public string ArtifactToString(FileOrDirectoryArtifact file, PathTable pathTable = null) => ToString(file.Path, pathTable);
        protected string ToString(AbsolutePath path, PathTable pathTable = null) => path.ToString(pathTable ?? Context.PathTable);
        protected AbsolutePath ToPath(string path, PathTable pathTable = null) => AbsolutePath.Create(pathTable ?? Context.PathTable, path);

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            params FileArtifact[] filesToProduceDynamically)
        {
            return SchedulePipBuilder(CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically));
        }

        protected ProcessBuilder CreateSharedOpaqueProducer(
            string sharedOpaqueDir,
            params FileArtifact[] filesToProduceDynamically)
        {
            return CreateSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: FileArtifact.Invalid,
                filesToProduceDynamically.Select(f => new KeyValuePair<FileArtifact, string>(f, null)).ToArray());
        }

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params FileArtifact[] filesToProduceDynamically)
        {
            return SchedulePipBuilder(CreateSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically, sourceFileToRead, filesToProduceDynamically));
        }

        protected ProcessBuilder CreateSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params FileArtifact[] filesToProduceDynamically)
        {
            return CreateSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically,
                sourceFileToRead,
                filesToProduceDynamically.Select(f => new KeyValuePair<FileArtifact, string>(f, null)).ToArray());
        }

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params KeyValuePair<FileArtifact, string>[] filesAndContentToProduceDynamically)
        {
            return SchedulePipBuilder(CreateSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically, sourceFileToRead, filesAndContentToProduceDynamically));
        }

        protected ProcessBuilder CreateSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params KeyValuePair<FileArtifact, string>[] filesAndContentToProduceDynamically)
        {
            return CreateSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically,
                sourceFileToRead,
                filesAndContentToProduceDynamically.SelectMany(
                    fac =>
                    {
                        return new[]
                        {
                            Operation.DeleteFile(fac.Key),
                            Operation.WriteFile(fac.Key, content: fac.Value, doNotInfer: true)
                        };
                    }).ToArray());
        }

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params Operation[] additionalOperations)
        {
            return SchedulePipBuilder(CreateSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically, sourceFileToRead, additionalOperations));
        }

        protected ProcessBuilder CreateSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params Operation[] additionalOperations)
        {
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var operations = new List<Operation>();

            if (fileToProduceStatically.IsValid)
            {
                operations.Add(Operation.WriteFile(fileToProduceStatically));
            }

            if (sourceFileToRead.IsValid)
            {
                operations.Add(Operation.ReadFile(sourceFileToRead));
            }

            operations.AddRange(additionalOperations);

            var builder = CreatePipBuilder(operations);
            builder.AddOutputDirectory(sharedOpaqueDirectoryArtifact, SealDirectoryKind.SharedOpaque);
            return builder;
        }

        protected ProcessWithOutputs CreateAndScheduleOpaqueProducer(string opaqueDir, FileArtifact sourceFile, params KeyValuePair<FileArtifact, string>[] filesAndContent)
        {
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact output1 = CreateOutputFileArtifact(opaqueDir);

            List<Operation> operations = new List<Operation>();
            operations.Add(Operation.ReadFile(sourceFile));
            foreach (var fac in filesAndContent)
            {
                operations.Add(Operation.WriteFile(fac.Key, content: fac.Value, doNotInfer: true));
            }

            var builder = CreatePipBuilder(operations);
            builder.AddOutputDirectory(opaqueDirPath);
            return SchedulePipBuilder(builder);
        }

        protected ProcessWithOutputs CreateAndScheduleConsumingPip(FileArtifact outputFile, params DirectoryArtifact[] directories)
        {
            var operations = directories.Select(directory => Operation.EnumerateDir(directory)).ToList();
            operations.Add(Operation.WriteFile(outputFile));

            var builder = CreatePipBuilder(operations);

            foreach (var directory in directories)
            {
                builder.AddInputDirectory(directory);
            }
            return SchedulePipBuilder(builder);
        }

        protected ProcessBuilder CreateOpaqueDirectoryConsumer(
            FileArtifact outputFile,
            FileArtifact? staticallyConsumedFile,
            DirectoryArtifact opaqueDirectory,
            params FileArtifact[] dynamicallyConsumedFiles)
        {
            var operations = dynamicallyConsumedFiles.Select(file => Operation.ReadFile(file, doNotInfer: true)).ToList();
            if (staticallyConsumedFile.HasValue)
            {
                operations.Add(Operation.ReadFile(staticallyConsumedFile.Value));
            }
            operations.Add(Operation.WriteFile(outputFile));

            var builder = CreatePipBuilder(operations);
            builder.AddInputDirectory(opaqueDirectory);

            return builder;
        }

        protected void AssertWritesJournaled(ScheduleRunResult result, ProcessWithOutputs pip, AbsolutePath outputInSharedOpaque)
        {
            // Assert that shared opaque outputs were journaled and the explicitly declared ones were not
            var journaledWrites = GetJournaledWritesForProcess(result, pip.Process);
            XAssert.Contains(journaledWrites, outputInSharedOpaque);
            XAssert.ContainsNot(journaledWrites, pip.ProcessOutputs.GetOutputFiles().Select(f => f.Path).ToArray());
        }

        protected string GetSidebandFile(ScheduleRunResult result, Process process)
            => SidebandWriter.GetSidebandFileForProcess(Context.PathTable, result.Config.Layout.SharedOpaqueSidebandDirectory, process);

        protected AbsolutePath[] GetJournaledWritesForProcess(ScheduleRunResult result, Process process)
        {
            var logFile = GetSidebandFile(result, process);
            XAssert.IsTrue(File.Exists(logFile));
            return SidebandWriter
                .ReadRecordedPathsFromSidebandFile(logFile)
                .Select(path => AbsolutePath.Create(Context.PathTable, path))
                .Distinct()
                .ToArray();
        }

        protected void SetExtraSalts(string salt, bool booleanOptionValues)
        {
            Configuration.Cache.CacheSalt = salt;
            Configuration.Sandbox.MaskUntrackedAccesses = booleanOptionValues;
            Configuration.Sandbox.NormalizeReadTimestamps = booleanOptionValues;
            Configuration.Logging.TreatWarningsAsErrors = booleanOptionValues;
            Configuration.Distribution.ValidateDistribution = booleanOptionValues;
        }

        protected override void Dispose(bool disposing)
        {
            if (m_expectedErrorCount > 0 // protect from lazy loading ErrorsLoggedById in success cases
                && (m_expectedErrorCount != LoggingContext.ErrorsLoggedById.Count))
            {
                // Protect with conditional to prevent doing string join in passing cases
                var errorsLogged = string.Join(", ", LoggingContext.ErrorsLoggedById.ToArray());
                AssertAreEqual(m_expectedErrorCount, LoggingContext.ErrorsLoggedById.Count, "Mismatch in expected error count. Errors logged: " + errorsLogged);
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Builds and schedules a number of pips that are waiting for the same file to be written
        /// before finishing execution.
        /// </summary>
        /// <returns>The FileArtifact corresponding to the file that the pips will wait on</returns>
        protected FileArtifact ScheduleWaitingForFilePips(int numberOfPips, int weight = 1)
        {
            var waitFile = CreateOutputFileArtifact(prefix: "wait");

            for (var i = 0; i < numberOfPips; i++)
            {
                var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.WaitUntilFileExists(waitFile, doNotInfer: true),
                    Operation.WriteFile(CreateOutputFileArtifact()),
                });

                builder.Weight = weight;
                builder.AddUntrackedFile(waitFile);
                SchedulePipBuilder(builder);
            }

            return waitFile;
        }

    }
}
