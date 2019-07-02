// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;
using ProcessOutputs = BuildXL.Pips.Builders.ProcessOutputs;
using BuildXL.Utilities.VmCommandProxy;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Base class for scheduler integration tests.
    /// </summary>
    public class SchedulerIntegrationTestBase : PipTestBase
    {
        public List<ScheduleRunResult> PriorResults = new List<ScheduleRunResult>();
        public CommandLineConfiguration Configuration;
        public EngineCache Cache;
        public FileContentTable FileContentTable;
        public DirectoryTranslator DirectoryTranslator = new DirectoryTranslator();

        // Keep track of whether the graph was changed between runs of the scheduler for sake of passing the same graph and
        // graphid to tests to allow exercising incremental scheduling.
        private bool m_graphWasModified;

        private PipGraph m_lastGraph;

        private JournalState m_journalState;

        /// <summary>
        /// Whether the scheduler should log all of its statistics at the end of every run.
        /// </summary>
        public bool ShouldLogSchedulerStats { get; set; } = false;

        /// <nodoc/>
        public SchedulerIntegrationTestBase(ITestOutputHelper output) : base(output)
        {
            CaptureAllDiagnosticMessages = false;

            // Each event listener that we want to capture events from must be listed here
            foreach (var eventSource in BuildXLApp.GeneratedEventSources)
            {
                RegisterEventSource(eventSource);
            }

            // Go through the command line configuration handling used by the real app to get the appropriate defaults
            ICommandLineConfiguration config;
            XAssert.IsTrue(Args.TryParseArguments(new[] { "/c:" + Path.Combine(TemporaryDirectory, "config.dc") }, Context.PathTable, null, out config), "Failed to construct arguments");
            Configuration = new CommandLineConfiguration(config);

            Cache = OperatingSystemHelper.IsUnixOS ? InMemoryCacheFactory.Create() : MockCacheFactory.Create(CacheRoot);

            FileContentTable = FileContentTable.CreateNew();

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

            // Populate file system capabilities.
            // Here, for example, we use copy-on-write instead of hardlinks when Unix file system supports copy-on-write.
            // Particular tests can override this by setting Configuration.Engine.UseHardlinks.
            BuildXLEngine.PopulateFileSystemCapabilities(Configuration, Configuration, Context.PathTable, LoggingContext);

            // Reset pip graph builder to use the populated configuration.
            ResetPipGraphBuilder();
        }

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
        public ProcessWithOutputs CreateAndSchedulePipBuilder(IEnumerable<Operation> processOperations, IEnumerable<string> tags = null, string description = null)
        {
            var pipBuilder = CreatePipBuilder(processOperations, tags, description);
            return SchedulePipBuilder(pipBuilder);
        }

        /// <summary>
        /// Runs the scheduler using the instance member PipGraph and Configuration objects. This will also carry over
        /// any state from any previous run such as the cache
        /// </summary>
        public ScheduleRunResult RunScheduler(SchedulerTestHooks testHooks = null, SchedulerState schedulerState = null, RootFilter filter = null, TempCleaner tempCleaner = null, IEnumerable<(Pip before, Pip after)> constraintExecutionOrder = null)
        {
            if (m_graphWasModified || m_lastGraph == null)
            {
                m_lastGraph = PipGraphBuilder.Build();
                XAssert.IsNotNull(m_lastGraph, "Failed to build pip graph");
            }
            
            m_graphWasModified = false;
            return RunSchedulerSpecific(m_lastGraph, testHooks, schedulerState, filter, tempCleaner, constraintExecutionOrder);
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
                return base.PipGraphBuilder;
            }
        }

        /// <summary>
        /// Runs the scheduler allowing various options to be specifically set
        /// </summary>
        public ScheduleRunResult RunSchedulerSpecific(
            PipGraph graph, 
            SchedulerTestHooks testHooks = null, 
            SchedulerState schedulerState = null,
            RootFilter filter = null,
            TempCleaner tempCleaner = null,
            IEnumerable<(Pip before, Pip after)> constraintExecutionOrder = null)
        {
            // This is a new logging context to be used just for this instantiation of the scheduler. That way it can
            // be validated against the LoggingContext to make sure the scheduler's return result and error logging
            // are in agreement.
            var localLoggingContext = BuildXLTestBase.CreateLoggingContextForTest();
            var config = new CommandLineConfiguration(Configuration);

            // Populating the configuration may modify the configuration, so it should occur first.
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(config, Context.PathTable, bxlExeLocation: null, inTestMode: true);
            BuildXLEngine.PopulateAndValidateConfiguration(config, config, Context.PathTable, LoggingContext);
            
            FileAccessWhitelist whitelist = new FileAccessWhitelist(Context);
            whitelist.Initialize(config);

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

            // Seal the translator if not sealed
            DirectoryTranslator.Seal();

            // .....................................................................................
            // some dummy setup in order to get a PreserveOutputsSalt.txt file and an actual salt
            // .....................................................................................
            string dummyCacheDir = Path.Combine(TemporaryDirectory, "Out", "Cache");
            Directory.CreateDirectory(dummyCacheDir); // EngineSchedule tries to put the PreserveOutputsSalt.txt here
            ContentHash? previousOutputsSalt =
                EngineSchedule.PreparePreviousOutputsSalt(localLoggingContext, Context.PathTable, config);
            Contract.Assert(previousOutputsSalt.HasValue);
            // .....................................................................................

            testHooks = testHooks ?? new SchedulerTestHooks();
            Contract.Assert(!(config.Engine.CleanTempDirectories && tempCleaner == null));

            using (var queue = new PipQueue(config.Schedule))
            using (var testQueue = new TestPipQueue(queue, localLoggingContext, initiallyPaused: constraintExecutionOrder != null))
            using (var testScheduler = new TestScheduler(
                graph: graph,
                pipQueue: constraintExecutionOrder == null ? 
                            testQueue : 
                            constraintExecutionOrder.Aggregate(testQueue, (TestPipQueue _testQueue, (Pip before, Pip after) constraint) => { _testQueue.ConstrainExecutionOrder(constraint.before, constraint.after); return _testQueue; }).Unpause(),
                context: Context,
                fileContentTable: FileContentTable,
                loggingContext: localLoggingContext,
                cache: Cache,
                configuration: config,
                journalState: m_journalState,
                fileAccessWhitelist: whitelist,
                fingerprintSalt: Configuration.Cache.CacheSalt,
                directoryMembershipFingerprinterRules: new DirectoryMembershipFingerprinterRuleSet(Configuration, Context.StringTable),
                tempCleaner: tempCleaner,
                previousInputsSalt: previousOutputsSalt.Value,
                successfulPips: null,
                failedPips: null,
                ipcProvider: null,
                directoryTranslator: DirectoryTranslator,
                vmInitializer: VmInitializer.CreateFromEngine(config.Layout.BuildEngineDirectory.ToString(Context.PathTable)),
                testHooks: testHooks))
            {
                MountPathExpander mountPathExpander = null;
                var frontEndNonScrubbablePaths = CollectionUtilities.EmptyArray<string>();
                var nonScrubbablePaths = EngineSchedule.GetNonScrubbablePaths(Context.PathTable, config, frontEndNonScrubbablePaths, tempCleaner);
                EngineSchedule.ScrubExtraneousFilesAndDirectories(mountPathExpander, testScheduler, localLoggingContext, config, nonScrubbablePaths, tempCleaner);

                if (filter == null)
                {
                    EngineSchedule.TryGetPipFilter(localLoggingContext, Context, config, config, Expander.TryGetRootByMountName, out filter);
                }

                XAssert.IsTrue(testScheduler.InitForMaster(localLoggingContext, filter, schedulerState), "Failed to initialized test scheduler");

                testScheduler.Start(localLoggingContext);

                bool success = testScheduler.WhenDone().GetAwaiter().GetResult();
                testScheduler.SaveFileChangeTrackerAsync(localLoggingContext).Wait();

                if (ShouldLogSchedulerStats)
                {
                    // Logs are not written out normally during these tests, but LogStats depends on the existence of the logs directory
                    // to write out the stats perf JSON file
                    var logsDir = config.Logging.LogsDirectory.ToString(Context.PathTable);
                    Directory.CreateDirectory(logsDir);
                    testScheduler.LogStats(localLoggingContext);
                }

                var runResult = new ScheduleRunResult
                {
                    Graph = graph,
                    Config = config,
                    Success = success,
                    PipResults = testScheduler.PipResults,
                    PipExecutorCounters = testScheduler.PipExecutionCounters,
                    PathSets = testScheduler.PathSets,
                    ProcessPipCountersByFilter = testScheduler.ProcessPipCountersByFilter,
                    ProcessPipCountersByTelemetryTag = testScheduler.ProcessPipCountersByTelemetryTag,
                    SchedulerState = new SchedulerState(testScheduler)
                };

                runResult.AssertSuccessMatchesLogging(localLoggingContext);

                // Prmote this run's specific LoggingContext into the test's LoggingContext.
                LoggingContext.AbsorbLoggingContextState(localLoggingContext);
                return runResult;
            }
        }

        private Possible<global::BuildXL.Storage.ChangeJournalService.IChangeJournalAccessor> TryGetJournalAccessor(VolumeMap map)
        {
            return map.Volumes.Any()
                ? JournalUtils.TryGetJournalAccessorForTest(map)
                : new Failure<string>("Invalid");
        }

        public string ArtifactToString(FileOrDirectoryArtifact file, PathTable pathTable = null)
        {
            return file.Path.ToString(pathTable ?? Context.PathTable);
        }

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            params FileArtifact[] filesToProduce)
        {
            return CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir, 
                fileToProduceStatically: FileArtifact.Invalid, 
                sourceFileToRead: FileArtifact.Invalid, 
                filesToProduce.Select(f => new KeyValuePair<FileArtifact, string>(f, null)).ToArray());
        }

        protected ProcessWithOutputs CreateAndScheduleSharedOpaqueProducer(
            string sharedOpaqueDir,
            FileArtifact fileToProduceStatically,
            FileArtifact sourceFileToRead,
            params FileArtifact[] filesToProduceDynamically)
        {
            return CreateAndScheduleSharedOpaqueProducer(
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
            return CreateAndScheduleSharedOpaqueProducer(
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
            return SchedulePipBuilder(builder);
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
    }
}
