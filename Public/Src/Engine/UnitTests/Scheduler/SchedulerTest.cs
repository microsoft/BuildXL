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
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Processes;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;
using ProcessBuilder = BuildXL.Pips.Builders.ProcessBuilder;

namespace Test.BuildXL.Scheduler
{
    [Trait("Category", "SchedulerTest")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)] /* Bug 1378018 */
    public sealed partial class SchedulerTest : PipTestBase
    {
        /// <summary>
        /// Pip queue, which can be used to monitor completion of scheduled pips.
        /// </summary>
        private PipQueue m_pipQueue;

        /// <summary>
        /// Configuration
        /// </summary>
        private CommandLineConfiguration m_configuration;
        
        private TestScheduler m_scheduler;

        /// <summary>
        /// Flag from test attribute: Controls scheduler stopping on first failure.
        /// </summary>
        private bool m_schedulerShouldStopOnFirstFailure;

        /// <summary>
        /// The test pip queue which allows altering the result of running a pip
        /// </summary>
        private TestPipQueue m_testQueue;

        private FileContentTable m_fileContentTable;

        private JournalState m_journalState;

        public SchedulerTest(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        private void Setup(
            bool stopOnFirstFailure = false,
            bool pausePipQueue = false,
            bool disableLazyOutputMaterialization = false,
            bool disablePipSerialization = false,
            bool enableDeterminismProbe = false,
            bool scheduleMetaPips = false,
            int maxProcesses = 1,
            bool enableJournal = false,
            bool enableIncrementalScheduling = false,
            bool enableGraphAgnosticIncrementalScheduling = true)
        {
            m_fileContentTable = FileContentTable.CreateNew();

            // Used for later construction of a scheduler (after pips added).
            m_schedulerShouldStopOnFirstFailure = stopOnFirstFailure;

            bool pauseQueue = pausePipQueue;
            bool enableLazyOutputMaterialization = !disableLazyOutputMaterialization;

            m_configuration = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "config.ds")));
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(m_configuration, Context.PathTable, bxlExeLocation: null, inTestMode: true);

            m_configuration.Schedule.MaxProcesses = maxProcesses;
            m_configuration.Schedule.MaxIO = 1;
            m_configuration.Schedule.MaxLightProcesses = 1;
            m_configuration.Schedule.EnableLazyOutputMaterialization = enableLazyOutputMaterialization;
            m_configuration.Sandbox.FileAccessIgnoreCodeCoverage = true;
            m_configuration.Cache.DeterminismProbe = enableDeterminismProbe;
            m_configuration.Schedule.ScheduleMetaPips = scheduleMetaPips;

            if (enableIncrementalScheduling)
            {
                m_configuration.Schedule.IncrementalScheduling = true;
                m_configuration.Schedule.ComputePipStaticFingerprints = true;

                if (!enableGraphAgnosticIncrementalScheduling)
                {
                    m_configuration.Schedule.GraphAgnosticIncrementalScheduling = false;
                }
            }

            if (m_configuration.Schedule.IncrementalScheduling)
            {
                m_configuration.Schedule.SkipHashSourceFile = false;
            }

            BaseSetup(m_configuration, disablePipSerialization: disablePipSerialization);
            m_pipQueue = new PipQueue(m_configuration.Schedule);
            m_testQueue = new TestPipQueue(m_pipQueue, LoggingContext, initiallyPaused: pauseQueue);

            if (enableJournal)
            {
                m_journalState = ConnectToJournal();
                XAssert.IsTrue(m_journalState.IsEnabled);
            }
        }

        private JournalState ConnectToJournal()
        {
            m_volumeMap = VolumeMap.TryCreateMapOfAllLocalVolumes(LoggingContext);
            XAssert.IsNotNull(m_volumeMap);

            var maybeJournal = JournalAccessorGetter.TryGetJournalAccessor(LoggingContext, m_volumeMap, AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            XAssert.IsNotNull(maybeJournal.IsValid, "Could not connect to journal");

            m_journal = maybeJournal.Value;
            return JournalState.CreateEnabledJournal(m_volumeMap, m_journal);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            m_scheduler?.Dispose();
            m_testQueue?.Dispose();
            m_pipQueue?.Dispose();
            PipTable?.Dispose();
        }

        [Feature(Features.CopyFilePip)]
        [Fact]
        public Task TestSchedulerAddCopyFile()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile();
            FileArtifact destinationArtifact = CreateOutputFileArtifact();
            CopyFile copyFile = CreateCopyFile(sourceArtifact, destinationArtifact);

            bool addCopyFile = PipGraphBuilder.AddCopyFile(copyFile);
            XAssert.IsTrue(addCopyFile);

            addCopyFile = PipGraphBuilder.AddCopyFile(copyFile);
            XAssert.IsTrue(addCopyFile);

            SetExpectedFailures(0, 0);

            return RunScheduler();
        }

        [Feature(Features.Mount)]
        [Fact]
        public Task TestSchedulerFailReadFromNonReadableRoot()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile(NonReadableRoot);
            FileArtifact copyDestinationArtifact = CreateOutputFileArtifact();
            FileArtifact processDestinationArtifact = CreateOutputFileArtifact();
            CopyFile copyFile = CreateCopyFile(sourceArtifact, copyDestinationArtifact);

            Process process = CreateProcess(
                dependencies: new[] { sourceArtifact },
                outputs: new[] { processDestinationArtifact });

            XAssert.IsFalse(PipGraphBuilder.AddCopyFile(copyFile));
            AssertSchedulerErrorEventLogged(EventId.InvalidInputUnderNonReadableRoot);

            XAssert.IsFalse(PipGraphBuilder.AddProcess(process));
            AssertSchedulerErrorEventLogged(EventId.InvalidInputUnderNonReadableRoot);

            return RunScheduler();
        }

        private Task TestSchedulerNoHashFromNonHashableRoot(bool determinismProbe = false)
        {
            Setup(enableDeterminismProbe: determinismProbe);
            FileArtifact sourceArtifact = CreateSourceFile(NonHashableRoot);
            FileArtifact processDestinationArtifact = CreateOutputFileArtifact();

            Process process = CreateProcess(
                dependencies: new[] { sourceArtifact },
                outputs: new[] { processDestinationArtifact });

            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));
            return RunScheduler();
        }

        [Feature(Features.Mount)]
        [Fact]
        public async Task TestSchedulerNoHashFromNonHashableRootDeterminismProbeDisabled()
        {
            await TestSchedulerNoHashFromNonHashableRoot(determinismProbe: false);
            XAssert.AreEqual(0, m_scheduler.PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipDeterminismProbeProcessCannotRunFromCache), "count of processes that cannot run from cache");
        }

        [Feature(Features.Mount)]
        [Fact]
        public async Task TestSchedulerNoHashFromNonHashableRootWithDeterminismProbeEnabled()
        {
            await TestSchedulerNoHashFromNonHashableRoot(determinismProbe: true);
            XAssert.AreEqual(1, m_scheduler.PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipDeterminismProbeProcessCannotRunFromCache), "count of processes that cannot run from cache");
        }

        [Feature(Features.Mount)]
        [Fact]
        public Task TestSchedulerFailWriteToNonWritableRoot()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile();
            FileArtifact copyDestinationArtifact = CreateOutputFileArtifact(ReadonlyRoot);
            FileArtifact writeDestinationArtifact = CreateOutputFileArtifact(ReadonlyRoot);
            FileArtifact processDestinationArtifact = CreateOutputFileArtifact(ReadonlyRoot);
            CopyFile copyFile = CreateCopyFile(sourceArtifact, copyDestinationArtifact);
            WriteFile writeFile = CreateWriteFile(writeDestinationArtifact, string.Empty, new[] { "content to be written to readonly location" });

            Process process = CreateProcess(
                dependencies: new[] { sourceArtifact },
                outputs: new[] { processDestinationArtifact });

            XAssert.IsFalse(PipGraphBuilder.AddWriteFile(writeFile));
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputUnderNonWritableRoot);

            XAssert.IsFalse(PipGraphBuilder.AddCopyFile(copyFile));
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputUnderNonWritableRoot);

            XAssert.IsFalse(PipGraphBuilder.AddProcess(process));
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputUnderNonWritableRoot);

            string workingDirectory = Path.Combine(ReadonlyRoot, "processWorking1");

            // Test setting working directory to non-writable root
            Process processWithNonWritableWorkingDirectory = CreateProcess(
                dependencies: new[] { sourceArtifact },
                outputs: new[] { CreateOutputFileArtifact() },
                workingDirectory: AbsolutePath.Create(Context.PathTable, workingDirectory));

            foreach (var tmpVar in BuildParameters.DisallowedTempVariables)
            {
                // Test setting TEMP environment variable to non-writable root
                Process processWithNonWritableTempDirectory = CreateProcess(
                    dependencies: new[] { sourceArtifact },
                    outputs: new[] { CreateOutputFileArtifact() },
                    environmentVariables: new[]
                    {
                        new EnvironmentVariable(
                            StringId.Create(Context.StringTable, tmpVar),
                            PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, Path.Combine(ReadonlyRoot, "processTemp")))
                    });
                XAssert.IsFalse(PipGraphBuilder.AddProcess(processWithNonWritableTempDirectory));
                AssertSchedulerErrorEventLogged(EventId.InvalidTempDirectoryUnderNonWritableRoot);

                Process processWithInvalidPathTempDirectory = CreateProcess(
                    dependencies: new[] { sourceArtifact },
                    outputs: new[] { CreateOutputFileArtifact() },
                    environmentVariables: new[]
                    {
                        new EnvironmentVariable(
                            StringId.Create(Context.StringTable, tmpVar),
                            PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, ReadonlyRoot + @"\invalidTemp:?|"))
                    });

                XAssert.IsFalse(PipGraphBuilder.AddProcess(processWithInvalidPathTempDirectory));

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    // Test setting TEMP environment variable to invalid path causes an error to be logged on Windows only
                    AssertSchedulerErrorEventLogged(EventId.InvalidTempDirectoryInvalidPath);
                }
                else
                {
                    AssertSchedulerErrorEventLogged(EventId.InvalidTempDirectoryUnderNonWritableRoot);
                }
            }

            IgnoreWarnings();
            return RunScheduler();
        }

        [Feature(Features.SealedDirectory)]
        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public void TestLazyHashingSealDirectory()
        {
            Setup();
            const string File1 = @"file1";
            const string File2 = @"file2";

            using (TestEnv env = new TestEnv("TestLazyHashingSealDirectory", TemporaryDirectory, enableLazyOutputMaterialization: true))
            {
                AbsolutePath dirPath = env.SourceRoot.Combine(env.PathTable, "myDir");
                AbsolutePath file1Path = dirPath.Combine(env.PathTable, "file1.txt");
                AbsolutePath file2Path = dirPath.Combine(env.PathTable, "file2.txt");

                // Write some actual files to disk
                Directory.CreateDirectory(dirPath.ToString(env.Context.PathTable));
                File.WriteAllText(file1Path.ToString(env.Context.PathTable), File1);
                File.WriteAllText(file2Path.ToString(env.Context.PathTable), File2);


                // CreateSourceFile a sealed directory from the 2 files
                var sealedDir = env.PipConstructionHelper.SealDirectoryFull(
                    dirPath,
                    new[]
                    {
                        FileArtifact.CreateSourceFile(file1Path),
                        FileArtifact.CreateSourceFile(file2Path)
                    });

                // CreateSourceFile a pip that consumes 1 of the two files
                ScheduleProcessToConsumeFile(env, file1Path, sealedDir, "output1.txt");

                // Schedule another pip to consume the same file to make sure it is only hashed once when consumed through a sealed directory
                ScheduleProcessToConsumeFile(env, file1Path, sealedDir, "output2.txt");

                RunSchedule(env);
            }

            if (OperatingSystemHelper.IsUnixOS)
            {
                // ignoring /bin/sh is being used as a source file
                AssertWarningEventLogged(EventId.IgnoringUntrackedSourceFileNotUnderMount);
            }

            // Only 1 source file should have been hashed (file1.txt). Also the second pip that consumes file1.txt should not cause the file to be rehashed
            // in a sealed directory used as input twice
            AssertVerboseEventLogged(EventId.HashedSourceFile);
        }

        /// <summary>
        /// Runs scheduled pips, waiting for the schedule, pip queue, and pip table to drain.
        /// Asserts on the overall pass / fail reslt of the scheduler.
        /// </summary>
        /// <remarks>
        /// This is not async despite waiting on tasks, for easier test debugging.
        /// </remarks>
        public void RunSchedule(TestEnv env)
        {
            PipQueue pipQueue = null;
            global::BuildXL.Scheduler.Scheduler scheduler = null;
            EngineCache cacheLayer = null;

            try
            {
                pipQueue = new PipQueue(env.Configuration.Schedule);

                var schedulerAndContentCache = TestSchedulerFactory.CreateWithCaching(
                    env.Context,
                    env.Configuration,
                    (PipGraph.Builder)env.PipGraph,
                    pipQueue);

                // Both the created scheduler and content cache wrapper need to be disposed.
                scheduler = schedulerAndContentCache.Item1;
                cacheLayer = schedulerAndContentCache.Item2;

                Contract.Assert(cacheLayer != null);

                Contract.Assume(scheduler != null);
                scheduler.InitForMaster(LoggingContext, kextConnection: GetSandboxedKextConnection());
                scheduler.Start(LoggingContext);

                bool success = scheduler.WhenDone().Result;

                if (!success)
                {
                    XAssert.Fail("Unexpected scheduler result");
                }
            }
            finally
            {
                scheduler?.Dispose();
                pipQueue?.Dispose();
                cacheLayer?.Dispose();
            }
        }

        private void ScheduleProcessToConsumeFile(TestEnv env, AbsolutePath file, DirectoryArtifact sealedDir, string outputFileName)
        {
            var exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(env.PathTable, CmdHelper.OsShellExe));
            var outputArtifact = env.ObjectRoot.Combine(env.PathTable, outputFileName);

            var builder = ProcessBuilder.Create(env.PathTable, env.PipDataBuilderPool.GetInstance());
            builder.Executable = exe;
            builder.AddInputFile(exe);
            foreach (var scope in CmdHelper.GetCmdDependencyScopes(env.PathTable))
            {
                builder.AddUntrackedDirectoryScope(scope);
            }

            var argsBulder = builder.ArgumentsBuilder;
            using (argsBulder.StartFragment(PipDataFragmentEscaping.NoEscaping, env.PathTable.StringTable.Empty))
            {
                argsBulder.Add(OperatingSystemHelper.IsUnixOS ? "-c ' /bin/cat" : "/D /C type ");
            }
            argsBulder.Add(file);
            using (argsBulder.StartFragment(PipDataFragmentEscaping.NoEscaping, env.PathTable.StringTable.Empty))
            {
                argsBulder.Add(" > ");
            }
            argsBulder.Add(outputArtifact);
            if (OperatingSystemHelper.IsUnixOS)
            {
                using (argsBulder.StartFragment(PipDataFragmentEscaping.NoEscaping, env.PathTable.StringTable.Empty))
                {
                    argsBulder.Add("'");
                }
            }

            builder.AddOutputFile(outputArtifact);
            builder.AddInputDirectory(sealedDir);
            if (!OperatingSystemHelper.IsUnixOS)
            {
                builder.AddCurrentHostOSDirectories();
            }
            env.PipConstructionHelper.AddProcess(builder);
        }

        [Fact]
        public async Task TestSchedulerStopOnFirstErrorWithDependants()
        {
            Setup(stopOnFirstFailure: true, pausePipQueue: true);
            CopyFile copyFileA1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "A1");
            CopyFile copyFileA2 = CreateCopyFile(copyFileA1.Destination, CreateOutputFileArtifact(), "A2");

            CopyFile copyFileB1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "B1"); // Root failing pip
            CopyFile copyFileB2 = CreateCopyFile(copyFileB1.Destination, CreateOutputFileArtifact(), "B2"); // Dependent failing pip

            // Dependent of a failing pip (which also depends on successful pip)
            Process process = CreateProcess(
                dependencies: new[] { copyFileA2.Destination, copyFileB2.Destination },
                outputs: new[] { CreateOutputFileArtifact() });

            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA2));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB2));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            var graph = PipGraphBuilder.Build();

            // A1 is supposed to be canceled in queue, so it has to happen after the failure.
            // Further, its HashSourceFile dependency has to be done by the time of failure (or it won't be in queue)
            m_testQueue.ConstrainExecutionOrderAfterSourceFileHashing(PipTable, graph, copyFileB1);
            m_testQueue.ConstrainExecutionOrder(copyFileB1, copyFileA1);

            m_testQueue.Unpause();

            bool scheduleSucceeded = await RunScheduler(
                overriddenSuccessfulPips: new Pip[0],
                overriddenFailedPips: new Pip[] { copyFileB1 },
                expectedFailedPips: new Pip[] { copyFileB1 },

                // copyFileA1 ends up canceled since it was definitely in the queue by the time of schedule failure.
                expectedCanceledPips: new Pip[] { copyFileA1 },

                // The dependent copy pip and process pips should not have any results. They remain unscheduled due to the schedule terminating after B1
                // (upon which copyFileB2 and process both depend), and copyFileA1 thus being canceled in queue (upon which copyFileA2 depends).
                expectedUnscheduledPips: new Pip[] { copyFileB2, copyFileA2, process });

            XAssert.IsFalse(scheduleSucceeded);

            AssertVerboseEventLogged(EventId.CancelingPipSinceScheduleIsTerminating);
            AssertErrorEventLogged(EventId.TerminatingDueToPipFailure);
        }

        [Fact]
        public async Task TestSemaphores()
        {
            Setup(maxProcesses: 100);
            int limit = 2;

            FileArtifact sourceArtifact = CreateSourceFile();

            for (int i = 0; i < 100; i++)
            {
                // Pip dependent on another that already finished in the queue (so this one should be canceled in the queue)
                Process process = CreateProcess(
                    dependencies: new[] { sourceArtifact },
                    outputs: new[] { CreateOutputFileArtifact().WithAttributes() },
                    semaphores: new[] { new ProcessSemaphoreInfo(StringId.Create(Context.StringTable, "TestSemaphore"), 1, limit) });

                XAssert.IsTrue(PipGraphBuilder.AddProcess(process));
            }

            PipGraphBuilder.Build();

            bool scheduleSucceeded = await RunScheduler();

            XAssert.IsTrue(scheduleSucceeded);
            XAssert.IsTrue(m_scheduler.MaxExternalProcessesRan <= limit, $"{m_scheduler.MaxExternalProcessesRan}, {limit}");
            IgnoreWarnings();
        }

        [Fact]
        public async Task TestSchedulerStopOnFirstErrorWithoutDependants()
        {
            Setup(stopOnFirstFailure: true, pausePipQueue: true, disableLazyOutputMaterialization: true);
            CopyFile preFailureCopyFile = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "A1");

            WriteFile unluckyWrite = CreateWriteFile(CreateOutputFileArtifact(), string.Empty, new[] { "Bad", "Wolf" });

            // Pip which is eligible to be queued and so will be canceled.
            CopyFile postFailureCopyFile = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "B1");

            // Pip dependant on another that is skipped in the queue (so this one should not have any results)
            Process processWithSkippedDependant = CreateProcess(
                dependencies: new[] { postFailureCopyFile.Destination },
                outputs: new[] { CreateOutputFileArtifact() });

            // Pip dependent on another that already finished in the queue (so this one should be canceled in the queue)
            Process processSkippedInQueue = CreateProcess(
                dependencies: new[] { preFailureCopyFile.Destination },
                outputs: new[] { CreateOutputFileArtifact() });

            XAssert.IsTrue(PipGraphBuilder.AddWriteFile(unluckyWrite));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(preFailureCopyFile));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(postFailureCopyFile));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(processWithSkippedDependant));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(processSkippedInQueue));

            // preFailureCopyFile should succeed since unluckyWrite is after it
            m_testQueue.ConstrainExecutionOrder(preFailureCopyFile, unluckyWrite);

            var graph = PipGraphBuilder.Build();

            // The rest remain queued until the schedule has failed. We also need any HashSourceFile
            // dependencies to be done already, or they won't be eligible for the queue.
            m_testQueue.ConstrainExecutionOrderAfterSourceFileHashing(PipTable, graph, unluckyWrite);
            m_testQueue.ConstrainExecutionOrder(unluckyWrite, postFailureCopyFile);
            m_testQueue.ConstrainExecutionOrder(unluckyWrite, processWithSkippedDependant);
            m_testQueue.ConstrainExecutionOrder(unluckyWrite, processSkippedInQueue);

            m_testQueue.Unpause();
            bool scheduleSucceeded = await RunScheduler(
                overriddenSuccessfulPips: new Pip[0],
                overriddenFailedPips: new Pip[] { unluckyWrite },
                expectedSuccessfulPips: new Pip[] { preFailureCopyFile },
                expectedFailedPips: new Pip[] { unluckyWrite },
                expectedCanceledPips: new Pip[] { postFailureCopyFile, processSkippedInQueue },
                expectedUnscheduledPips: new Pip[] { processWithSkippedDependant });

            XAssert.IsFalse(scheduleSucceeded);

            // With the given constraints  2 or 3 pips may get cancelled depending on the actual schedule. Uncertainty is  due to the fact that
            // processSkippedInQueue may get cancelled or skipped.
            AssertVerboseEventCountIsInInterval(EventId.CancelingPipSinceScheduleIsTerminating, minOccurrences: 2, maxOccurrences: 3);
            AssertErrorEventLogged(EventId.TerminatingDueToPipFailure);

            AssertLatestProcessPipCounts(succeeded: 0, failed: 0, skipped: 1);
        }

        [Fact]
        public async Task TestSchedulerFailedPipWithIgnoredDependent()
        {
            Setup();
            FileArtifact output = CreateOutputFileArtifact();
            Process passesFilter = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { output },
                tags: new[] { "Yep" });

            Process failsFilter = CreateProcess(
                dependencies: new[] { output },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "Nope" });

            XAssert.IsTrue(PipGraphBuilder.AddProcess(passesFilter));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(failsFilter));

            bool scheduleSucceeded = await RunScheduler(
                CreateFilterForTags(tagsWhitelist: new[] { "Yep" }.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)), tagsBlacklist: new StringId[] { }),
                overriddenSuccessfulPips: new Pip[0],
                overriddenFailedPips: new Pip[] { passesFilter },
                expectedFailedPips: new Pip[] { passesFilter },
                expectedUnscheduledPips: new Pip[] { failsFilter });

            XAssert.IsFalse(scheduleSucceeded);

            VerifyPipState(failsFilter, PipState.Ignored);
            VerifyPipState(passesFilter, PipState.Failed);

            AssertLatestProcessPipCounts(succeeded: 0, failed: 1, skipped: 0);
        }

        [Fact]
        public async Task TestFilteringMultipleSealDirectoryPerPath()
        {
            Setup();
            FileArtifact output = CreateOutputFileArtifact();

            var sealRoot = CreateUniqueSourcePath("SSD");
            var sealMember = CreateSourceFile(sealRoot.ToString(Context.PathTable));
            SealDirectory seal1 = CreateSealDirectory(sealRoot, SealDirectoryKind.Full, "Yep", sealMember);
            var seal1Dir = PipGraphBuilder.AddSealDirectory(seal1);

            Process passesFilter = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                directoryDependencies: new[] { seal1Dir },
                outputs: new[] { output },
                tags: new[] { "Yep" });

            SealDirectory seal2 = CreateSealDirectory(sealRoot, SealDirectoryKind.Full, "Yep", sealMember);
            var seal2Dir = PipGraphBuilder.AddSealDirectory(seal2);

            FileArtifact output2 = CreateOutputFileArtifact();
            Process passesFilter2 = CreateProcess(
                dependencies: new[] { output },
                directoryDependencies: new[] { seal2Dir },
                outputs: new[] { output2 },
                tags: new[] { "Yep" });

            XAssert.IsTrue(PipGraphBuilder.AddProcess(passesFilter));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(passesFilter2));

            bool scheduleSucceeded = await RunScheduler(
                CreateFilterForTags(tagsWhitelist: new[] { "Yep" }.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)), tagsBlacklist: new StringId[] { }),
                expectedSuccessfulPips: new Pip[] { seal2, seal1, passesFilter, passesFilter2 });

            XAssert.IsTrue(scheduleSucceeded);
        }

        [Fact]
        public async Task TestSchedulerContinueOnError()
        {
            Setup(disableLazyOutputMaterialization: true);
            CopyFile copyFileA1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "A1");
            CopyFile copyFileA2 = CreateCopyFile(copyFileA1.Destination, CreateOutputFileArtifact(), "A2");
            CopyFile copyFileB1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "B1"); // Root failing pip
            CopyFile copyFileB2 = CreateCopyFile(copyFileB1.Destination, CreateOutputFileArtifact(), "B2"); // Dependent failing pip

            Process process = CreateProcess(
                dependencies: new[] { copyFileA2.Destination, copyFileB2.Destination },
                outputs: new[] { CreateOutputFileArtifact() }); // Dependent failing pip which also depends on  successful pip

            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA2));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB2));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            XAssert.IsFalse(await RunScheduler(
                overriddenSuccessfulPips: new Pip[0],
                overriddenFailedPips: new Pip[] { copyFileB1 },
                expectedSuccessfulPips: new Pip[] { copyFileA1, copyFileA2 },
                expectedFailedPips: new Pip[] { copyFileB1 },
                expectedSkippedPips: new Pip[] { copyFileB2, process }));

            AssertVerboseEventLogged(EventId.PipFailedDueToFailedPrerequisite, 2 /*copy_7,copy_8*/ + 3 /*value_5,Value_7,Value_8*/ + 3 /*spec_5,spec_7,spec_8*/ + 1 /*module_1*/);
        }

        /// <summary>
        ///   Dataflow graph:
        ///     A1  B1
        ///     |   |
        ///     v   v
        ///     A2  B2
        ///      \ /
        ///       P
        /// </summary>
        private sealed class TestGraphForTagFilter
        {
            public Pip A1;
            public Pip A2;
            public Pip B1;
            public Pip B2;
            public Pip P;
        }

        private TestGraphForTagFilter CreateGraphForTagFilterTestingWithCopyPips()
        {
            CopyFile copyFileA1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "A1", tags: new[] { "A", "1" });
            CopyFile copyFileA2 = CreateCopyFile(copyFileA1.Destination, CreateOutputFileArtifact(), "A2", tags: new[] { "A", "2" });
            CopyFile copyFileB1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), "B1", tags: new[] { "B", "1" });
            CopyFile copyFileB2 = CreateCopyFile(copyFileB1.Destination, CreateOutputFileArtifact(), "B2", tags: new[] { "B", "2" });

            Process process = CreateProcess(
                dependencies: new[] { copyFileA2.Destination, copyFileB2.Destination },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "P", "0" });

            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA2));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB1));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB2));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            return new TestGraphForTagFilter
            {
                A1 = copyFileA1,
                A2 = copyFileA2,
                B1 = copyFileB1,
                B2 = copyFileB2,
                P = process
            };
        }

        private void AssertPipStatesForTestGraphForTagFilter(TestGraphForTagFilter g, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        {
            XAssert.AreEqual(a1Done, m_scheduler.PipResults.ContainsKey(g.A1.PipId));
            XAssert.AreEqual(a2Done, m_scheduler.PipResults.ContainsKey(g.A2.PipId));
            XAssert.AreEqual(b1Done, m_scheduler.PipResults.ContainsKey(g.B1.PipId));
            XAssert.AreEqual(b2Done, m_scheduler.PipResults.ContainsKey(g.B2.PipId));
            XAssert.AreEqual(pDone, m_scheduler.PipResults.ContainsKey(g.P.PipId));

            Func<bool, PipState> toPipState = (b) => b ? PipState.Done : PipState.Ignored;
            VerifyPipState(g.A1, toPipState(a1Done));
            VerifyPipState(g.A2, toPipState(a2Done));
            VerifyPipState(g.B1, toPipState(b1Done));
            VerifyPipState(g.B2, toPipState(b2Done));
            VerifyPipState(g.P, toPipState(pDone));
        }

        /// <summary>
        /// Data for <see cref="TestScheduleByTag"/> and <see cref="TestIpcPipTagFiltering"/>.
        ///
        /// Each returned object array corresponds to the following formal parameters:
        /// <code>
        ///     (string whitelistTags, string blacklistTags, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        /// </code>
        ///
        /// Each returned object array indicates which pips should be executed (i.e., "done") given a
        /// <see cref="TestGraphForTagFilter"/> pip graph, whitelist tags, and blacklist tags.
        /// </summary>
        public static IEnumerable<object[]> TagFilteringTestData()
        {
            yield return new object[] { "A", null, true, true, false, false, false };
            yield return new object[] { "1", null, true, false, true, false, false };
            yield return new object[] { "P", null, true, true, true, true, true };
            yield return new object[] { null, "BP", true, true, false, false, false };
            yield return new object[] { null, "1", true, true, true, true, true };
            yield return new object[] { null, "2P", true, false, true, false, false };
        }

        [Feature(Features.Filtering)]
        [Theory]
        [MemberData(nameof(TagFilteringTestData))]
        public async Task TestScheduleByTag(string whitelistTags, string blacklistTags, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        {
            Setup(disableLazyOutputMaterialization: true);

            TestGraphForTagFilter g = CreateGraphForTagFilterTestingWithCopyPips();
            await RunScheduler(CreateFilterForTags(ToStringIds(whitelistTags), ToStringIds(blacklistTags)));

            AssertPipStatesForTestGraphForTagFilter(g, a1Done, a2Done, b1Done, b2Done, pDone);
        }

        [Feature(Features.Filtering)]
        [Fact]
        public async Task TestSchedulingDependentsWithDependenciesOutsideOfCone()
        {
            Setup();
            IgnoreWarnings();

            // CreateSourceFile a graph like:
            //  p1     p2
            //  ^      ^
            //   \    /
            //    \  /
            //     p3    p4
            //     ^     ^
            //      \   / \            ^
            //       \ /   \            \
            //       p5    p6         <--v1 - I consume all pips
            //
            // Where p3 depends on both p1 and p2, and p5 depends on p3 and p4
            // We explicitly schedule p3, its dependencies, and dependents. One of those dependents is p5. we need to
            // make sure p4 gets scheduled to satisfy p5's dependencies
            //
            // Also, all pips are created with the same shared provenance. That means there will be a single value pip
            // that is a dependency of all of the pips in the graph.

            PipProvenance sharedProvenance = CreateProvenance();
            FileArtifact p1Output = CreateOutputFileArtifact();
            FileArtifact p2Output = CreateOutputFileArtifact();
            FileArtifact p3Output = CreateOutputFileArtifact();
            FileArtifact p4Output = CreateOutputFileArtifact();

            Process p1 = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { p1Output },
                tags: new[] { "FilterOut" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { p2Output },
                tags: new[] { "FilterOut" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            Process p3 = CreateProcess(
                dependencies: new[] { p1Output, p2Output },
                outputs: new[] { p3Output },
                tags: new[] { "FilterIn" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            Process p4 = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { p4Output },
                tags: new[] { "FilterOut" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p4);

            Process p5 = CreateProcess(
                dependencies: new[] { p3Output, p4Output },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "FilterOut" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p5);

            Process p6 = CreateProcess(
                dependencies: new[] { p4Output },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "FilterOut" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p6);

            RootFilter filter = new RootFilter(new DependentsFilter(new TagFilter(StringId.Create(Context.StringTable, "FilterIn"))));
            await RunScheduler(filter);

            ExpectPipsDone(
                LabelPip(p1, nameof(p1)),
                LabelPip(p2, nameof(p2)),
                LabelPip(p3, nameof(p3)),
                LabelPip(p4, nameof(p4)),
                LabelPip(p5, nameof(p5)));

            // p6 should not be run. Since everything shares the same provenance, v1 is a value pip that depends on all
            // process pips. If the filtering did not take into a account whether a pip is a metadata pit, it would schedule
            // p6 because of its connection through v1. So make sure p6 is not scheduled.
            ExpectPipsNotDone(LabelPip(p6, nameof(p6)));
        }

        [Feature(Features.OpaqueDirectory)]
        [Feature(Features.Filtering)]
        [Fact]
        public async Task TestSchedulingWithTagAndOpaqueDirectories()
        {
            // SSD <-- P1 (OACRANALYZE) <-- D1 <-- P2 (OACRANALYZE) <-- D2
            //   F <-- P3 <-- O

            Setup();
            PipProvenance sharedProvenance = CreateProvenance();

            // Create sealed source directory SSD, and its files, SSD/F1 and SSD/F2
            AbsolutePath ssdPath = CreateUniqueSourcePath("SSD");
            FileArtifact ssdF1 = CreateSourceFile(ssdPath.ToString(Context.PathTable));
            FileArtifact ssdF2 = CreateSourceFile(ssdPath.ToString(Context.PathTable));

            SealDirectory ssdPip = CreateSealDirectory(ssdPath, SealDirectoryKind.SourceAllDirectories);
            DirectoryArtifact ssd = PipGraphBuilder.AddSealDirectory(ssdPip);

            // Create output directory D1 and its files D1/O1 and D1/O2.
            AbsolutePath d1 = CreateUniqueObjPath("D1");
            FileArtifact d1O1 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O1"));
            FileArtifact d1O2 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O2"));

            // Create output directory D2 and its files D2/O1 and D2/O2.
            AbsolutePath d2 = CreateUniqueObjPath("D2");
            FileArtifact d2O1 = FileArtifact.CreateOutputFile(d2.Combine(Context.PathTable, "O1"));
            FileArtifact d2O2 = FileArtifact.CreateOutputFile(d2.Combine(Context.PathTable, "O2"));

            var sealedOutputDirectories = new Dictionary<AbsolutePath, DirectoryArtifact>();

            Process p1 = CreateProcess(
                dependencies: new FileArtifact[0],
                outputs: new FileArtifact[0],
                directoryDependencies: new[] { ssd },
                directoryDependenciesToConsume: new[] { ssdF1, ssdF2 },
                outputDirectoryPaths: new[] { d1 },
                directoryOutputsToProduce: new[] { d1O1, d1O2 },
                resultingSealedOutputDirectories: sealedOutputDirectories,
                provenance: sharedProvenance,
                tags: new[] { "OACRANALYZE" });
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new FileArtifact[0],
                outputs: new FileArtifact[0],
                directoryDependencies: new[] { sealedOutputDirectories[d1] },
                directoryDependenciesToConsume: new[] { d1O1, d1O2 },
                outputDirectoryPaths: new[] { d2 },
                directoryOutputsToProduce: new[] { d2O1, d2O2 },
                resultingSealedOutputDirectories: sealedOutputDirectories,
                provenance: sharedProvenance,
                tags: new[] { "OACRANALYZE" });
            PipGraphBuilder.AddProcess(p2);

            FileArtifact f = CreateSourceFile();
            FileArtifact o = CreateOutputFileArtifact();

            Process p3 = CreateProcess(
                dependencies: new[] { f },
                outputs: new[] { o },
                provenance: sharedProvenance,
                tags: new string[0]);
            PipGraphBuilder.AddProcess(p3);

            RootFilter filter = new RootFilter(new TagFilter(StringId.Create(Context.StringTable, "OACRANALYZE")));
            bool succeed = await RunScheduler(filter);
            XAssert.IsTrue(succeed);

            ExpectPipsDone(LabelPip(p1, nameof(p1)), LabelPip(p2, nameof(p2)));
            ExpectPipsNotDone(LabelPip(p3, nameof(p3)));

            filter = new RootFilter(new NegatingFilter(new TagFilter(StringId.Create(Context.StringTable, "OACRANALYZE"))));
            succeed = await RunScheduler(filter);
            XAssert.IsTrue(succeed);

            ExpectPipsNotDone(LabelPip(p1, nameof(p1)), LabelPip(p2, nameof(p2)));
            ExpectPipsDone(LabelPip(p3, nameof(p3)));
        }

        public (Pip, string) LabelPip(Pip pip, string label)
        {
            return (pip, label);
        }

        public (Pip, string, PipResultStatus) LabelPipWithStatus(Pip pip, string label, PipResultStatus status)
        {
            return (pip, label, status);
        }

        public (Pip, string, PipResultStatus) LabelPipWithStatus(Dictionary<string, Pip> pips, string label, PipResultStatus status)
        {
            return (pips[label], label, status);
        }

        public void ExpectPipsDone(params (Pip, string)[] pips)
        {
            foreach (var pip in pips)
            {
                string pipDescription =
                    I(
                        $"{pip.Item2} ({pip.Item1.GetDescription(Context)})");
                XAssert.IsTrue(
                    m_scheduler.PipResults.ContainsKey(pip.Item1.PipId),
                    I($"Expected pip '{pipDescription}' is not done"));
            }
        }

        public void ExpectPipsNotDone(params (Pip, string)[] pips)
        {
            foreach (var pip in pips)
            {
                string pipDescription =
                    I(
                        $"{pip.Item2} ({pip.Item1.GetDescription(Context)})");
                XAssert.IsFalse(m_scheduler.PipResults.ContainsKey(pip.Item1.PipId), I($"Expected pip '{pipDescription}' is done"));
            }
        }

        public void ExpectPipResults(params (Pip, string, PipResultStatus)[] pipsAndExpectedResultStatus)
        {
            foreach (var pipAndExpectedResultStatus in pipsAndExpectedResultStatus)
            {
                PipResultStatus actualStatus;
                string pipDescription =
                    I(
                        $"{pipAndExpectedResultStatus.Item2} ({pipAndExpectedResultStatus.Item1.GetDescription(Context)})");
                XAssert.IsTrue(
                    m_scheduler.PipResults.TryGetValue(pipAndExpectedResultStatus.Item1.PipId, out actualStatus),
                    I($"Expected pip '{pipDescription}' does not exist in pip results"));
                XAssert.AreEqual(pipAndExpectedResultStatus.Item3, actualStatus, I($"Pip '{pipDescription}', expected: {pipAndExpectedResultStatus.Item3}, actual: {actualStatus.ToString()}"));
            }
        }

        [Feature(Features.CopyFilePip)]
        [Fact]
        public Task TestSchedulerAddCopyFileUseOutput()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile();
            FileArtifact destinationArtifact = CreateOutputFileArtifact();
            FileArtifact destinationArtifactToo = CreateOutputFileArtifact();
            CopyFile copyFile = CreateCopyFile(sourceArtifact, destinationArtifact);
            CopyFile copyFileToo = CreateCopyFile(destinationArtifact, destinationArtifactToo);

            bool addCopyFile = PipGraphBuilder.AddCopyFile(copyFile);
            XAssert.IsTrue(addCopyFile);

            addCopyFile = PipGraphBuilder.AddCopyFile(copyFileToo);
            XAssert.IsTrue(addCopyFile);

            SetExpectedFailures(0, 0);

            return RunScheduler();
        }

        [Feature(Features.WriteFilePip)]
        [Fact]
        public Task TestSchedulerAddWriteFile()
        {
            Setup();
            FileArtifact targetArtifact = CreateOutputFileArtifact();
            WriteFile writeFile = CreateWriteFile(targetArtifact, "\n", new List<string> { "1", "2", "3" });

            bool addWriteFile = PipGraphBuilder.AddWriteFile(writeFile);
            XAssert.IsTrue(addWriteFile);

            addWriteFile = PipGraphBuilder.AddWriteFile(writeFile);
            XAssert.IsTrue(addWriteFile);

            SetExpectedFailures(0, 0);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerAddProcess()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerAddProcessInvalidDependency()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = depArtifact1.CreateNextWrittenVersion();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsFalse(addProcess);

            AssertSchedulerErrorEventLogged(EventId.InvalidInputDueToMultipleConflictingRewriteCounts);
            AssertPipDescriptionAndProvenanceLogged(process);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerAddProcessNoOutput()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1 },
                new List<FileArtifact>());

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsFalse(addProcess);

            AssertSchedulerErrorEventLogged(EventId.InvalidProcessPipDueToNoOutputArtifacts);
            AssertPipDescriptionAndProvenanceLogged(process);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerAddProcessUseOutput()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            FileArtifact outArtifact3 = CreateOutputFileArtifact();

            Process processToo = CreateProcess(
                new List<FileArtifact> { outArtifact2 },
                new List<FileArtifact> { outArtifact3 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsTrue(addProcess);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerAddProcessWithOrderDependencies()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            FileArtifact depArtifact3 = CreateSourceFile();
            FileArtifact outArtifact3 = CreateOutputFileArtifact();

            Process processToo = CreateProcess(
                new List<FileArtifact> { depArtifact3 },
                new List<FileArtifact> { outArtifact3 },
                orderDependencies: new List<PipId> { process.PipId });

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsTrue(addProcess);

            return RunScheduler();
        }

        [Fact]
        public Task TestSchedulerBottomUp()
        {
            Setup();
            const int NumOfWriteFiles = 5;
            const int NumOfSourceFiles = 5;
            const int NumOfPips = 20;

            var sources = new HashSet<FileArtifact>();

            for (int i = 0; i < NumOfSourceFiles; ++i)
            {
                sources.Add(CreateSourceFile());
            }

            for (int i = 0; i < NumOfWriteFiles; ++i)
            {
                FileArtifact targetArtifact = CreateOutputFileArtifact();
                WriteFile writeFile = CreateWriteFile(
                    targetArtifact,
                    "\n",
                    new List<string> { i.ToString(CultureInfo.InvariantCulture) });
                sources.Add(targetArtifact);

                bool addWriteFile = PipGraphBuilder.AddWriteFile(writeFile);
                XAssert.IsTrue(addWriteFile);
            }

            var randomGen = new Random(0);

            for (int i = 0; i < NumOfPips; ++i)
            {
                int random = randomGen.Next(2);

                if (random == 0)
                {
                    FileArtifact sourceArtifact = GetAnyFileArtifact(sources);
                    sources.Remove(sourceArtifact);

                    FileArtifact targetArtifact = CreateOutputFileArtifact();

                    CopyFile copyFile = CreateCopyFile(sourceArtifact, targetArtifact);
                    sources.Add(targetArtifact);

                    bool addCopyFile = PipGraphBuilder.AddCopyFile(copyFile);
                    XAssert.IsTrue(addCopyFile);
                }
                else
                {
                    FileArtifact depArtifact = GetAnyFileArtifact(sources);
                    sources.Remove(depArtifact);

                    FileArtifact depArtifact2 = GetAnyFileArtifact(sources);
                    sources.Remove(depArtifact2);

                    FileArtifact outArtifact1 = CreateOutputFileArtifact();
                    FileArtifact outArtifact2 = randomGen.NextDouble() > 0.5
                        ? CreateOutputFileArtifact()
                        : depArtifact2.CreateNextWrittenVersion();

                    Process process = CreateProcess(
                        new List<FileArtifact> { depArtifact, depArtifact2 },
                        new List<FileArtifact> { outArtifact1, outArtifact2 });

                    sources.Add(outArtifact1);
                    sources.Add(outArtifact2);

                    bool addProcess = PipGraphBuilder.AddProcess(process);
                    XAssert.IsTrue(addProcess);
                }
            }

            return RunScheduler();
        }

        [Feature(Features.NonStandardOptions)]
        [Fact]
        public async Task TestSchedulerSpecifyDirectoryAsInputFileShouldFail()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = FileArtifact.CreateSourceFile(CreateUniqueDirectory());
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            var customConfiguration = new ScheduleConfiguration(m_configuration.Schedule, new PathRemapper())
            {
                TreatDirectoryAsAbsentFileOnHashingInputContent = false
            };

            await RunScheduler(scheduleConfiguration: customConfiguration);
            AssertWarningEventLogged(EventId.FailedToHashInputFileBecauseTheFileIsDirectory);
            AssertErrorEventLogged(LogEventId.PipFailedDueToSourceDependenciesCannotBeHashed);
        }

        private async Task<bool> RunScheduler(
            RootFilter filter = null,
            ScheduleConfiguration scheduleConfiguration = null,
            IEnumerable<Pip> overriddenSuccessfulPips = null,
            IEnumerable<Pip> overriddenFailedPips = null,
            Pip[] expectedSuccessfulPips = null,
            Pip[] expectedFailedPips = null,
            Pip[] expectedSkippedPips = null,
            Pip[] expectedCanceledPips = null,
            Pip[] expectedUnscheduledPips = null,
            IIpcProvider ipcProvider = null,
            EngineCache cache = null,
            SchedulerTestHooks testHooks = null)
        {
            Contract.Assume(
                !m_testQueue.Paused,
                "The TestPipQueue must be Unpause()-ed before waiting on the scheduler (when PausePipQueue is set on the test).");

            if (testHooks == null)
            {
                testHooks = new SchedulerTestHooks();
            }

            var graph = PipGraphBuilder.Build();

            if (scheduleConfiguration != null)
            {
                m_configuration.Schedule = scheduleConfiguration;
            }

            m_configuration.Schedule.StopOnFirstError = m_schedulerShouldStopOnFirstFailure;

            m_scheduler?.Dispose();

            m_scheduler = new TestScheduler(
                graph: graph,
                pipQueue: m_testQueue,
                context: Context,
                fileContentTable: m_fileContentTable,
                loggingContext: LoggingContext,
                cache: cache ?? InMemoryCacheFactory.Create(),
                configuration: m_configuration,
                fileAccessWhitelist: new FileAccessWhitelist(Context),
                successfulPips: overriddenSuccessfulPips,
                failedPips: overriddenFailedPips,
                ipcProvider: ipcProvider,
                journalState: m_journalState,
                testHooks: testHooks);

            bool success = m_scheduler.InitForMaster(LoggingContext, filter);
            XAssert.IsTrue(success);

            m_scheduler.Start(LoggingContext);
            success = await m_scheduler.WhenDone();

            await PipTable.WhenDone();
            m_scheduler.UpdateStatus(overwriteable: false);

            m_scheduler.SaveFileChangeTrackerAsync(LoggingContext).Wait();
            m_scheduler.AssertPipResults(
                expectedSuccessfulPips,
                expectedFailedPips,
                expectedSkippedPips,
                expectedCanceledPips,
                expectedUnscheduledPips);

            return success;
        }

        private void VerifyPipState(Pip pip, PipState desiredState)
        {
            XAssert.AreEqual(desiredState, m_scheduler.GetPipState(pip.PipId));
        }

        private static FileArtifact GetAnyFileArtifact(IEnumerable<FileArtifact> sources)
        {
            FileArtifact[] asArray = sources.ToArray();
            return asArray[0];
        }

        private Process CreateProcess(
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifact> outputs,
            IEnumerable<string> tags = null,
            PipProvenance provenance = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<AbsolutePath> outputDirectoryPaths = null,
            IEnumerable<PipId> orderDependencies = null,
            IEnumerable<FileArtifact> directoryDependenciesToConsume = null,
            IEnumerable<FileArtifact> directoryOutputsToProduce = null,
            AbsolutePath workingDirectory = default(AbsolutePath),
            IEnumerable<EnvironmentVariable> environmentVariables = null,
            Dictionary<AbsolutePath, DirectoryArtifact> resultingSealedOutputDirectories = null)
        {
            Contract.Requires(dependencies != null, "Argument dependencies cannot be null");
            Contract.Requires(outputs != null, "Argument outputs cannot be null");

            return CreateProcess(
                dependencies,
                outputs.Select(o => o.WithAttributes()),
                tags,
                provenance,
                directoryDependencies,
                outputDirectoryPaths,
                orderDependencies,
                directoryDependenciesToConsume,
                directoryOutputsToProduce,
                workingDirectory,
                environmentVariables,
                resultingSealedOutputDirectories);
        }

        private PipData CreateArguments(
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifact> outputs)
        {
            Contract.Requires(dependencies != null, "Argument dependencies cannot be null");
            Contract.Requires(outputs != null, "Argument outputs cannot be null");

            PipDataBuilder pipDataBuilder = new PipDataBuilder(Context.StringTable);
            int i = 0;
            foreach (FileArtifact output in outputs)
            {
                if (i > 0)
                {
                    pipDataBuilder.Add("&&");
                }

                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    if (dependencies.Any())
                    {
                        pipDataBuilder.Add(OperatingSystemHelper.IsUnixOS ? "/bin/ls" : "dir");
                        foreach (var dependency in dependencies)
                        {
                            pipDataBuilder.Add(dependency);
                        }
                    }
                    else
                    {
                        pipDataBuilder.Add("echo");
                    }

                    pipDataBuilder.Add(">");
                    pipDataBuilder.Add(output);
                }

                i++;
            }

            var argsData = pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);

            pipDataBuilder = new PipDataBuilder(Context.StringTable);
            if (OperatingSystemHelper.IsUnixOS)
            {
                pipDataBuilder.Add("-c");
                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, " "))
                {
                    pipDataBuilder.Add("'");
                    pipDataBuilder.Add(argsData);
                    pipDataBuilder.Add("'");
                }
            }
            else
            {
                pipDataBuilder.Add("/d");
                pipDataBuilder.Add("/c");
                pipDataBuilder.Add(argsData);
            }

            return pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
        }

        private Utils.ProcessBuilder NewProcessBuilderWithPreDeterminedArgumentsFactory()
        {
            var cmdBuilder = NewCmdProcessBuilder();
            cmdBuilder.WithArgumentsFactory(

                // Slightly complicated, but the final arguments depend on dependencies and outputs
                // that's are not specified yet. Func allows to construct arguments during Build method call.
                builder =>
                    CreateArguments(
                        UnionNullableEnumerables(builder.Dependencies, builder.DirectoryDependenciesToConsume),
                        builder.Outputs.Select(f => f.ToFileArtifact())));
            return cmdBuilder;
        }

        private Utils.ProcessBuilder NewCmdProcessBuilder()
        {
            FileArtifact executable = GetCmdExecutable();

            return
                new Utils.ProcessBuilder()
                    .WithExecutable(executable)
                    .WithWorkingDirectory(GetWorkingDirectory())
                    .WithEnvironmentVariables(
                        new EnvironmentVariable(
                            StringId.Create(Context.PathTable.StringTable, "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty)),
                            PipDataBuilder.CreatePipData(Context.PathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "TEST")))
                    .WithStandardDirectory(GetStandardDirectory())
                    .WithUntrackedPaths(CmdHelper.GetCmdDependencies(Context.PathTable))
                    .WithUntrackedScopes(CmdHelper.GetCmdDependencyScopes(Context.PathTable))
                    .WithProvenance(CreateProvenance());
        }

        private Process CreateProcess(
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifactWithAttributes> outputs,
            IEnumerable<string> tags = null,
            PipProvenance provenance = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<AbsolutePath> outputDirectoryPaths = null,
            IEnumerable<PipId> orderDependencies = null,
            IEnumerable<FileArtifact> directoryDependenciesToConsume = null,
            IEnumerable<FileArtifact> directoryOutputsToProduce = null,
            AbsolutePath workingDirectory = default(AbsolutePath),
            IEnumerable<EnvironmentVariable> environmentVariables = null,
            Dictionary<AbsolutePath, DirectoryArtifact> resultingSealedOutputDirectories = null,
            IEnumerable<ProcessSemaphoreInfo> semaphores = null)
        {
            Contract.Requires(dependencies != null, "Argument dependencies cannot be null");
            Contract.Requires(outputs != null, "Argument outputs cannot be null");

            FileArtifact executable = GetCmdExecutable();

            environmentVariables = environmentVariables ?? Enumerable.Empty<EnvironmentVariable>();
            DirectoryArtifact[] outputDirectories = null;

            if (outputDirectoryPaths != null)
            {
                outputDirectories = outputDirectoryPaths.Select(OutputDirectory.Create).ToArray();
                if (resultingSealedOutputDirectories != null)
                {
                    foreach (var directory in outputDirectories)
                    {
                        resultingSealedOutputDirectories.Add(directory.Path, directory);
                    }
                }
            }

            var finalDependencies = dependencies.Union(directoryDependenciesToConsume ?? ReadOnlyArray<FileArtifact>.Empty);
            var finalOutputs = outputs.Select(f => f.ToFileArtifact()).Union(directoryOutputsToProduce ?? ReadOnlyArray<FileArtifact>.Empty);

            var process =
                new Process(
                    executable,
                    workingDirectory.IsValid ? workingDirectory : GetWorkingDirectory(),
                    CreateArguments(finalDependencies, finalOutputs),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(
                        new[]
                        {
                            new EnvironmentVariable(
                                StringId.Create(Context.PathTable.StringTable, "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty)),
                                PipDataBuilder.CreatePipData(
                                    Context.PathTable.StringTable,
                                    " ",
                                    PipDataFragmentEscaping.CRuntimeArgumentRules,
                                    "TEST"))
                        }.Concat(environmentVariables)),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    GetStandardDirectory(),
                    null,
                    null,

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.From(
                        new[] { executable /*, responseFile*/}
                            .Concat(dependencies)),
                    ReadOnlyArray<FileArtifactWithAttributes>.From(new FileArtifactWithAttributes[] { }.Concat(outputs)),
                    ReadOnlyArray<DirectoryArtifact>.From(directoryDependencies ?? new DirectoryArtifact[0]),
                    ReadOnlyArray<DirectoryArtifact>.From(outputDirectories ?? new DirectoryArtifact[0]),
                    orderDependencies != null ? ReadOnlyArray<PipId>.From(orderDependencies) : ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(Context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(
                        CmdHelper.GetCmdDependencyScopes(Context.PathTable)
                            .Concat(
                                outputDirectoryPaths == null ? ReadOnlyArray<AbsolutePath>.Empty : ReadOnlyArray<AbsolutePath>.From(outputDirectoryPaths))),
                    tags != null
                        ? ReadOnlyArray<StringId>.From(tags.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)))
                        : ReadOnlyArray<StringId>.Empty,

                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.From(semaphores ?? ReadOnlyArray<ProcessSemaphoreInfo>.Empty),
                    provenance ?? CreateProvenance(),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

            return process;
        }

        private static IEnumerable<TSource> UnionNullableEnumerables<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            return (first ?? Enumerable.Empty<TSource>()).Union(second ?? Enumerable.Empty<TSource>());
        }

        private void AssertSchedulerErrorEventLogged(EventId eventId, int count = 1)
        {
            Contract.Requires(count >= 0);
            AssertErrorEventLogged(eventId, count);
        }

        private void AssertPipDescriptionAndProvenanceLogged(Pip pip)
        {
            Contract.Requires(pip != null);
            AssertLogContains(
                false,
                pip.Provenance.Token.Path.ToString(Context.PathTable),
                pip.GetDescription(Context),
                pip.Provenance.OutputValueSymbol.ToString(Context.SymbolTable));
        }

        #region Rewrite / double-write handling

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task TestSchedulerAddProcessRewrite()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            FileArtifact outArtifact3 = outArtifact2.CreateNextWrittenVersion();

            Process processToo = CreateProcess(
                new List<FileArtifact> { outArtifact2 },
                new List<FileArtifact> { outArtifact3 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsTrue(addProcess);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task OutputCannotRewriteSourceFile()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile(ObjectRoot);
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = depArtifact2.CreateNextWrittenVersion();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsFalse(addProcess);

            AssertPipDescriptionAndProvenanceLogged(process);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceOutputIsSource);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task CannotUseExistingOutputAsSourceFile()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();
            FileArtifact out1AsSource = CreateSourceFile();
            FileArtifact out1 = out1AsSource.CreateNextWrittenVersion();
            FileArtifact out2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            Process process2 = CreateProcess(
                new List<FileArtifact> { out1AsSource },
                new List<FileArtifact> { out2 });
            XAssert.IsFalse(PipGraphBuilder.AddProcess(process2));

            AssertPipDescriptionAndProvenanceLogged(process2);
            AssertSchedulerErrorEventLogged(EventId.InvalidInputSincePathIsWrittenAndThusNotSource);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task RewrittenOutputMustBeANewerVersion()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1 });

            Process processToo = CreateProcess(
                new List<FileArtifact> { outArtifact1 },
                new List<FileArtifact> { outArtifact1 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsFalse(addProcess);

            AssertPipDescriptionAndProvenanceLogged(processToo);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceRewrittenOutputMismatchedWithInput);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task RewrittenOutputMustCorrespondToInputIfPresent()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            // We bump the write count twice, so the input rewrite count is two less that of the output (rather than one, which is required).
            FileArtifact rewrittenOutArtifact2 = outArtifact2.CreateNextWrittenVersion().CreateNextWrittenVersion();

            Process processToo = CreateProcess(
                new List<FileArtifact> { outArtifact2 },
                new List<FileArtifact> { rewrittenOutArtifact2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsFalse(addProcess);

            AssertPipDescriptionAndProvenanceLogged(processToo);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceRewrittenOutputMismatchedWithInput);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task RewrittenOutputMustHaveAntecedentInGraph()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();

            // Out1 is a rewritten version of an output file not yet produced.
            FileArtifact out1 = CreateOutputFileArtifact().CreateNextWrittenVersion();
            FileArtifact out2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1, out2 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsFalse(addProcess);

            AssertPipDescriptionAndProvenanceLogged(process);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceOutputHasUnexpectedlyHighWriteCount);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task SinglePipCannotWriteMultipleVersionsOfAPath()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();
            FileArtifact out1 = CreateOutputFileArtifact();
            FileArtifact rewrittenOut1 = out1.CreateNextWrittenVersion();

            Process process = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1, rewrittenOut1 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsFalse(addProcess);

            AssertSchedulerErrorEventLogged(EventId.InvalidOutputDueToMultipleConflictingRewriteCounts);
            AssertPipDescriptionAndProvenanceLogged(process);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task TwoPipsCannotWriteTheFirstVersionOfAPath()
        {
            Setup();
            FileArtifact depArtifact1 = CreateSourceFile();
            FileArtifact depArtifact2 = CreateSourceFile();
            FileArtifact outArtifact1 = CreateOutputFileArtifact();
            FileArtifact outArtifact2 = CreateOutputFileArtifact();

            Process process = CreateProcess(
                new List<FileArtifact> { depArtifact1, depArtifact2 },
                new List<FileArtifact> { outArtifact1, outArtifact2 });

            FileArtifact depArtifact3 = CreateSourceFile();
            FileArtifact outArtifact3 = outArtifact1;

            Process processToo = CreateProcess(
                new List<FileArtifact> { depArtifact3 },
                new List<FileArtifact> { outArtifact3 });

            bool addProcess = PipGraphBuilder.AddProcess(process);
            XAssert.IsTrue(addProcess);

            addProcess = PipGraphBuilder.AddProcess(processToo);
            XAssert.IsFalse(addProcess);

            AssertPipDescriptionAndProvenanceLogged(processToo);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputDueToSimpleDoubleWrite);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task TwoPipsCannotRewriteAPathToTheSameVersion()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();
            FileArtifact out1 = CreateOutputFileArtifact();

            // The rewriters depend on out2 (rather than the rewritten out1), since there is a separate message
            // for usages of out-of-date rewritten artifacts as input.
            FileArtifact out2 = CreateOutputFileArtifact();
            FileArtifact rewrittenOut1 = out1.CreateNextWrittenVersion();

            Process writer = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1, out2 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(writer));

            Process firstRewriter = CreateProcess(
                new List<FileArtifact> { out2, dep1 },
                new List<FileArtifact> { rewrittenOut1 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(firstRewriter));

            Process secondWriter = CreateProcess(
                new List<FileArtifact> { out2 },
                new List<FileArtifact> { rewrittenOut1 });
            XAssert.IsFalse(PipGraphBuilder.AddProcess(secondWriter));

            AssertPipDescriptionAndProvenanceLogged(secondWriter);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceRewritingOldVersion);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task CannotUseNonLatestRewrittenArtifactAsInput()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();
            FileArtifact out1 = CreateOutputFileArtifact();
            FileArtifact out2 = CreateOutputFileArtifact();
            FileArtifact rewrittenOut1 = out1.CreateNextWrittenVersion();

            Process writer = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(writer));

            Process rewriter = CreateProcess(
                new List<FileArtifact> { out1 },
                new List<FileArtifact> { rewrittenOut1 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(rewriter));

            Process sneakyConsumer = CreateProcess(
                new List<FileArtifact> { out1 },
                new List<FileArtifact> { out2 });
            XAssert.IsFalse(PipGraphBuilder.AddProcess(sneakyConsumer));

            AssertPipDescriptionAndProvenanceLogged(sneakyConsumer);
            AssertSchedulerErrorEventLogged(EventId.InvalidInputSinceInputIsRewritten);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public Task CannotRewriteAnArtifactWhichHasBeenRead()
        {
            Setup();
            FileArtifact dep1 = CreateSourceFile();
            FileArtifact out1 = CreateOutputFileArtifact();
            FileArtifact out2 = CreateOutputFileArtifact();
            FileArtifact rewrittenOut1 = out1.CreateNextWrittenVersion();

            Process writer = CreateProcess(
                new List<FileArtifact> { dep1 },
                new List<FileArtifact> { out1 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(writer));

            Process earlyBirdConsumer = CreateProcess(
                new List<FileArtifact> { out1 },
                new List<FileArtifact> { out2 });
            XAssert.IsTrue(PipGraphBuilder.AddProcess(earlyBirdConsumer));

            Process unluckyRewriter = CreateProcess(
                new List<FileArtifact> { out1 },
                new List<FileArtifact> { rewrittenOut1 });
            XAssert.IsFalse(PipGraphBuilder.AddProcess(unluckyRewriter));

            AssertPipDescriptionAndProvenanceLogged(unluckyRewriter);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSincePreviousVersionUsedAsInput);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        [Feature(Features.CopyFilePip)]
        public Task CopyFileCanRewrite()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile();
            FileArtifact firstCopy = CreateOutputFileArtifact();
            FileArtifact secondCopy = CreateOutputFileArtifact();
            FileArtifact rewrittenFirstCopy = firstCopy.CreateNextWrittenVersion();

            CopyFile createFirstCopy = CreateCopyFile(sourceArtifact, firstCopy);
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(createFirstCopy));

            CopyFile createSecondCopy = CreateCopyFile(sourceArtifact, secondCopy);
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(createSecondCopy));

            CopyFile rewriteFirstCopy = CreateCopyFile(secondCopy, rewrittenFirstCopy);
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(rewriteFirstCopy));

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        [Feature(Features.CopyFilePip)]
        public Task CopyFileCannotRewriteExistingVersion()
        {
            Setup();
            FileArtifact sourceArtifact = CreateSourceFile();
            FileArtifact destinationArtifact = CreateOutputFileArtifact();

            CopyFile copyFile = CreateCopyFile(sourceArtifact, destinationArtifact);
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFile));

            CopyFile copyFile2 = CreateCopyFile(sourceArtifact, destinationArtifact);
            XAssert.IsFalse(PipGraphBuilder.AddCopyFile(copyFile2));

            AssertPipDescriptionAndProvenanceLogged(copyFile2);
            AssertSchedulerErrorEventLogged(EventId.InvalidOutputDueToSimpleDoubleWrite);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        [Feature(Features.WriteFilePip)]
        public Task WriteFilePipsAreDisallowedFromRewritingToNewVersions()
        {
            Setup();
            FileArtifact targetArtifact = CreateOutputFileArtifact();
            WriteFile firstWrite = CreateWriteFile(targetArtifact, "\n", new List<string> { "1", "2", "3" });
            XAssert.IsTrue(PipGraphBuilder.AddWriteFile(firstWrite));

            // This would be fine with a Process pip, but we disallow WriteFile from rewriting.
            WriteFile secondWrite = CreateWriteFile(targetArtifact.CreateNextWrittenVersion(), "\n", new List<string> { "4", "5", "6" });
            XAssert.IsFalse(PipGraphBuilder.AddWriteFile(secondWrite));

            AssertSchedulerErrorEventLogged(EventId.InvalidWriteFilePipSinceOutputIsRewritten);
            AssertPipDescriptionAndProvenanceLogged(secondWrite);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        [Feature(Features.WriteFilePip)]
        public Task WriteFilePipsAreDisallowedFromRewritingExistingVersions()
        {
            Setup();
            FileArtifact targetArtifact = CreateOutputFileArtifact();
            WriteFile firstWrite = CreateWriteFile(targetArtifact, "\n", new List<string> { "1", "2", "3" });
            XAssert.IsTrue(PipGraphBuilder.AddWriteFile(firstWrite));
            WriteFile secondWrite = CreateWriteFile(targetArtifact, "\n", new List<string> { "4", "5", "6" });
            XAssert.IsFalse(PipGraphBuilder.AddWriteFile(secondWrite));

            AssertSchedulerErrorEventLogged(EventId.InvalidOutputDueToSimpleDoubleWrite);
            AssertPipDescriptionAndProvenanceLogged(secondWrite);

            return RunScheduler();
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        [Feature(Features.WriteFilePip)]
        public Task WriteFileCannotRewriteSource()
        {
            Setup();
            FileArtifact targetArtifact = CreateSourceFile();
            WriteFile writeFile = CreateWriteFile(targetArtifact, "\n", new List<string> { "1", "2", "3" });

            bool addWriteFile = PipGraphBuilder.AddWriteFile(writeFile);
            XAssert.IsFalse(addWriteFile);

            AssertSchedulerErrorEventLogged(EventId.InvalidOutputSinceOutputIsSource);

            return RunScheduler();
        }

        #endregion

        #region Service pips
        private void SetupServicePips(out Process servicePip, out Process shutdownPip, out Process clientPip)
        {
            Setup();
            CreateSourceFile(NonHashableRoot);

            shutdownPip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact(prefix: "service-shutdown-out"))
                .WithServiceInfo(ServiceInfo.ServiceShutdown)
                .Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(shutdownPip));

            servicePip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact(prefix: "service-pip-out"))
                .WithServiceInfo(ServiceInfo.Service(shutdownPip.PipId))
                .Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(servicePip));

            clientPip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact(prefix: "client-pip-out"))
                .WithServiceInfo(ServiceInfo.ServiceClient(new[] { servicePip.PipId }))
                .Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(clientPip));
        }

        [Fact]
        [Feature(Features.ServicePip)]
        public void TestShutdownPipHasNoIncomingProcessEdges()
        {
            Process servicePip, shutdownPip, clientPip;
            SetupServicePips(servicePip: out servicePip, shutdownPip: out shutdownPip, clientPip: out clientPip);

            var shutdownPipProcessDependencies = PipGraphBuilder
                .Build()
                .DataflowGraph
                .GetIncomingEdges(shutdownPip.PipId.ToNodeId())
                .Where(e => PipTable.GetPipType(e.OtherNode.ToPipId()) == PipType.Process);
            XAssert.AreEqual(0, shutdownPipProcessDependencies.Count());
        }

        [Fact]
        [Feature(Features.ServicePip)]
        public void TestServicePipAndServiceShutdownPipAreRun()
        {
            Process servicePip, shutdownPip, clientPip;
            SetupServicePips(servicePip: out servicePip, shutdownPip: out shutdownPip, clientPip: out clientPip);
            XAssert.IsTrue(RunScheduler().GetAwaiter().GetResult());

            // service pip and service shutdown pip must be uncacheable
            XAssert.AreEqual(2, m_scheduler.PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipsExecutedButUncacheable));

            // check scheduler stats
            XAssert.AreEqual(1, m_scheduler.SchedulerStats.ProcessPipsCompleted); // Client process pips run, service and shutdown pip not counted
            XAssert.AreEqual(1, m_scheduler.SchedulerStats.ServicePipsCompleted);
            XAssert.AreEqual(1, m_scheduler.SchedulerStats.ServiceShutdownPipsCompleted);

            // check services pips don't get counted as cache hits or cache misses
            XAssert.AreEqual(0, m_scheduler.SchedulerStats.ProcessPipsSatisfiedFromCache);
            XAssert.AreEqual(1, m_scheduler.SchedulerStats.ProcessPipsUnsatisfiedFromCache);
        }

        [Fact]
        [Feature(Features.ServicePip)]
        public void TestServiceWithNoClientsIsNotRun()
        {
            Setup();
            CreateSourceFile(NonHashableRoot);

            var shutdownPip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact())
                .WithServiceInfo(ServiceInfo.ServiceShutdown)
                .Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(shutdownPip));

            var servicePip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact())
                .WithServiceInfo(ServiceInfo.Service(shutdownPip.PipId))
                .Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(servicePip));

            XAssert.IsTrue(RunScheduler().GetAwaiter().GetResult());

            // The service pip is not run because it has no clients
            XAssert.AreEqual(0, m_scheduler.SchedulerStats.ServicePipsCompleted);
        }

        [Fact]
        [Feature(Features.ServicePip)]
        [Feature(Features.CopyFilePip)]
        public void TestAddProcessWithCopyPipAsServicePipDependencyFails()
        {
            Setup();
            CreateSourceFile(NonHashableRoot);

            var copyPip = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact());
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyPip));

            var clientPip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact())
                .WithServiceInfo(ServiceInfo.ServiceClient(new[] { copyPip.PipId }))
                .Build();
            XAssert.IsFalse(PipGraphBuilder.AddProcess(clientPip));
            AssertSchedulerErrorEventLogged(EventId.InvalidPipDueToInvalidServicePipDependency);
        }

        [Fact]
        [Feature(Features.ServicePip)]
        public void TestAddProcessWithNonServiceProcessAsServicePipDependencyFails()
        {
            Setup();
            CreateSourceFile(NonHashableRoot);

            var processPip = NewProcessBuilderWithPreDeterminedArgumentsFactory().WithOutputs(CreateOutputFileArtifact()).Build();
            XAssert.IsTrue(PipGraphBuilder.AddProcess(processPip));

            var clientPip = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact())
                .WithServiceInfo(ServiceInfo.ServiceClient(new[] { processPip.PipId }))
                .Build();
            XAssert.IsFalse(PipGraphBuilder.AddProcess(clientPip));
            AssertSchedulerErrorEventLogged(EventId.InvalidPipDueToInvalidServicePipDependency);
        }
        #endregion

        #region IPC pips
        private static readonly IIpcOperationExecutor s_succeedingEchoingIpcExecutor = new LambdaIpcOperationExecutor(op => IpcResult.Success(op.Payload));

        [Fact]
        [Feature(Features.IpcPip)]
        public void TestAddIpcPipToGraph()
        {
            Setup();

            var ipcProvider = new DummyIpcProvider();
            var moniker = ipcProvider.CreateNewMoniker();
            var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());
            var ipcPip = IpcPip.CreateFromStringPayload(Context, GetWorkingDirectory(), ipcInfo, "hi", CreateProvenance());
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));
            XAssert.IsTrue(ipcPip.PipId.IsValid);
        }

        [Fact]
        [Feature(Features.IpcPip)]
        public void TestAddIpcPipDependencies()
        {
            Setup();

            var moniker = new DummyIpcProvider().CreateNewMoniker();
            var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());

            CopyFile copyPip1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact());
            IpcPip ipcPip = IpcPip.CreateFromStringPayload(
                Context,
                GetWorkingDirectory(),
                ipcInfo,
                "hi",
                CreateProvenance(),
                fileDependencies: new[] { copyPip1.Destination },
                outputFile: CreateOutputFileArtifact());
            CopyFile copyPip2 = CreateCopyFile(ipcPip.OutputFile, CreateOutputFileArtifact());

            var builder = CreatePipBuilder(new[]
            {
                Test.BuildXL.Executables.TestProcess.Operation.ReadFile(copyPip2.Destination),
                Test.BuildXL.Executables.TestProcess.Operation.WriteFile(CreateOutputFileArtifact())
            });
            var outputDirectory = CreateOutputDirectoryArtifact();
            builder.AddOutputDirectory(outputDirectory);
            XAssert.IsTrue(builder.TryFinish(PipConstructionHelper, out var p1, out var processOutputs));

            IpcPip ipcPip2 = IpcPip.CreateFromStringPayload(
                Context,
                GetWorkingDirectory(),
                ipcInfo,
                "hi again",
                CreateProvenance(),
                directoryDependencies: new[] { processOutputs.GetOpaqueDirectory(outputDirectory.Path) },
                outputFile: CreateOutputFileArtifact());

            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyPip1));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyPip2));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(p1));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPip2, PipId.Invalid));

            var graph = PipGraphBuilder.Build();
            AssertGraphDirectPrecedence(graph, copyPip1, ipcPip);
            AssertGraphDirectPrecedence(graph, ipcPip, copyPip2);
            AssertGraphDirectPrecedence(graph, copyPip2, p1);
            // Ipc pip (ipcPip2) depends on the opaque directory produced by process pip (p1).
            // In the graph, ipc and process pips are not linked directly, instead they are
            // linked via sealDirectory pip which is added automatically behind the scenes.
            var directory = graph.TryGetDirectoryArtifactForPath(outputDirectory.Path);
            var sealDirectoryPipId = graph.GetSealedDirectoryNode(directory).ToPipId();
            var sealDirectoryPip = graph.GetPipFromPipId(sealDirectoryPipId);
            AssertGraphDirectPrecedence(graph, p1, sealDirectoryPip);
            AssertGraphDirectPrecedence(graph, sealDirectoryPip, ipcPip2);
            XAssert.AreEqual(ipcPip, graph.GetProducingPip(ipcPip.OutputFile));
        }

        [Fact]
        [Feature(Features.IpcPip)]
        public void TestAddIpcPipWithInvalidDependencies()
        {
            Setup();

            var moniker = new DummyIpcProvider().CreateNewMoniker();
            var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());

            var copySource = CreateSourceFile();
            var copyTarget = CreateOutputFileArtifact();
            CopyFile copyPip1 = CreateCopyFile(copySource, copyTarget);
            IpcPip ipcPip = IpcPip.CreateFromStringPayload(
                Context,
                GetWorkingDirectory(),
                ipcInfo,
                "hi",
                CreateProvenance(),
                fileDependencies: new[] { FileArtifact.CreateSourceFile(copyTarget.Path) });

            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyPip1));
            XAssert.IsFalse(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));
            AssertSchedulerErrorEventLogged(EventId.InvalidInputSincePathIsWrittenAndThusNotSource);
        }

        private void AssertGraphDirectPrecedence(PipGraph graph, Pip prev, Pip next)
        {
            var prevDesc = prev.GetDescription(Context);
            var nextDesc = next.GetDescription(Context);
            NodeId prevNode = prev.PipId.ToNodeId();
            NodeId nextNode = next.PipId.ToNodeId();
            XAssert.IsTrue(
                graph.DataflowGraph.GetOutgoingEdges(prev.PipId.ToNodeId()).Any(edge => edge.OtherNode == next.PipId.ToNodeId()),
                "expected to find an edge between '" + prevDesc + "' and '" + nextDesc + "'");
        }

        [Fact]
        [Feature(Features.IpcPip)]
        [Feature(Features.ServicePip)]
        public void TestAddIpcPipWithCopyPipAsServicePipDependencyFails()
        {
            Setup();

            var copyPip = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact());
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyPip));

            var moniker = new DummyIpcProvider().CreateNewMoniker();
            var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());
            var ipcPip = IpcPip.CreateFromStringPayload(
                Context,
                GetWorkingDirectory(),
                ipcInfo,
                "hi",
                CreateProvenance(),
                servicePipDependencies: new[] { copyPip.PipId });
            XAssert.IsFalse(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));
            AssertSchedulerErrorEventLogged(EventId.InvalidPipDueToInvalidServicePipDependency);
        }

        [Fact]
        [Feature(Features.IpcPip)]
        public void TestIpcClientHasFailures()
        {
            Setup();

            // set up a provider that returns an IPC client whose completion indicates an error
            var ipcProvider = new MockIpcProvider
            {
                GetClientFn = (_, c) => new MockClient
                {
                    Config = c,
                    Completion = TaskUtilities.FromException<Unit>(new Exception("FAIL!")),
                },
            };

            WithIpcServer(
                ipcProvider,
                s_succeedingEchoingIpcExecutor,
                new ServerConfig(),
                (moniker, server) =>
                {
                    var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());
                    var ipcPip = IpcPip.CreateFromStringPayload(Context, GetWorkingDirectory(), ipcInfo, "hi", CreateProvenance());

                    // assert adding pip to the graph succeeds
                    XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));

                    // run scheduler and assert it succeeded (because all pips succeeded)
                    XAssert.AreEqual(true, RunScheduler(ipcProvider: ipcProvider).GetAwaiter().GetResult());

                    // assert the ipc pip succeeded
                    XAssert.AreEqual(1, m_scheduler.SchedulerStats.IpcPipsCompleted);
                    ExpectPipsDone(LabelPip(ipcPip, nameof(ipcPip)));

                    // assert IpcClientFailed error was logged
                    AssertWarningEventLogged(EventId.IpcClientFailed);
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Feature(Features.IpcPip)]
        public void TestIpcPipExecution(bool shouldFail)
        {
            Setup();

            var ipcProvider = new DummyIpcProvider(statusToAlwaysReturn: shouldFail ? IpcResultStatus.GenericError : IpcResultStatus.Success);

            WithIpcServer(
                ipcProvider,
                s_succeedingEchoingIpcExecutor,
                new ServerConfig(),
                (moniker, server) =>
                {
                    var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());
                    var ipcPip = IpcPip.CreateFromStringPayload(Context, GetWorkingDirectory(), ipcInfo, "hi", CreateProvenance());

                    // assert adding pip to the graph succeeds
                    XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPip, PipId.Invalid));

                    // run scheduler and assert it succeeded IFF 'shouldFail' is false
                    XAssert.AreEqual(!shouldFail, RunScheduler(ipcProvider: ipcProvider).GetAwaiter().GetResult());
                    XAssert.AreEqual(1, m_scheduler.SchedulerStats.IpcPipsCompleted);

                    // assert error was logged
                    if (shouldFail)
                    {
                        AssertSchedulerErrorEventLogged(EventId.PipIpcFailed);
                    }
                });
        }

        [Theory]
        [MemberData(nameof(TagFilteringTestData))]
        [Feature(Features.IpcPip)]
        [Feature(Features.Filtering)]
        public Task TestIpcPipTagFiltering(string whitelistTags, string blacklistTags, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        {
            return DoIpcTagFilteringTest(CreateGraphForTagFilterTestingWithIpcPips, whitelistTags, blacklistTags, a1Done, a2Done, b1Done, b2Done, pDone);
        }

        [Theory]
        [MemberData(nameof(TagFilteringTestData))]
        [Feature(Features.Filtering)]
        [Feature(Features.IpcPip)]
        public Task TestIpcAndCopyPipTagFiltering(string whitelistTags, string blacklistTags, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        {
            return DoIpcTagFilteringTest(CreateGraphForTagFilterTestingWithIpcAndCopyPips, whitelistTags, blacklistTags, a1Done, a2Done, b1Done, b2Done, pDone);
        }

        private async Task DoIpcTagFilteringTest(Func<IpcClientInfo, TestGraphForTagFilter> graphFactory, string whitelistTags, string blacklistTags, bool a1Done, bool a2Done, bool b1Done, bool b2Done, bool pDone)
        {
            Setup(disableLazyOutputMaterialization: true);

            var ipcProvider = new DummyIpcProvider();
            await WithIpcServer(
                ipcProvider,
                s_succeedingEchoingIpcExecutor,
                new ServerConfig(),
                async (moniker, server) =>
                {
                    var ipcInfo = new IpcClientInfo(moniker.ToStringId(Context.StringTable), new ClientConfig());
                    TestGraphForTagFilter g = graphFactory(ipcInfo);
                    await RunScheduler(CreateFilterForTags(ToStringIds(whitelistTags), ToStringIds(blacklistTags)), ipcProvider: ipcProvider);
                    AssertPipStatesForTestGraphForTagFilter(g, a1Done, a2Done, b1Done, b2Done, pDone);
                });
        }

        private TestGraphForTagFilter CreateGraphForTagFilterTestingWithIpcPips(IpcClientInfo ipcInfo)
        {
            var ipcPipA1 = CreateTaggedIpcPip(ipcInfo, input: CreateSourceFile(), output: CreateOutputFileArtifact(), tags: new[] { "A", "1" });
            var ipcPipA2 = CreateTaggedIpcPip(ipcInfo, input: ipcPipA1.OutputFile, output: CreateOutputFileArtifact(), tags: new[] { "A", "2" });
            var ipcPipB1 = CreateTaggedIpcPip(ipcInfo, input: CreateSourceFile(), output: CreateOutputFileArtifact(), tags: new[] { "B", "1" });
            var ipcPipB2 = CreateTaggedIpcPip(ipcInfo, input: ipcPipB1.OutputFile, output: CreateOutputFileArtifact(), tags: new[] { "B", "2" });

            var process = CreateProcess(
                dependencies: new[] { ipcPipA2.OutputFile, ipcPipB2.OutputFile },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "P", "0" });

            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipA1, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipA2, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipB1, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipB2, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            return new TestGraphForTagFilter
            {
                A1 = ipcPipA1,
                A2 = ipcPipA2,
                B1 = ipcPipB1,
                B2 = ipcPipB2,
                P = process
            };
        }

        private TestGraphForTagFilter CreateGraphForTagFilterTestingWithIpcAndCopyPips(IpcClientInfo ipcInfo)
        {
            var ipcPipA1 = CreateTaggedIpcPip(ipcInfo, input: CreateSourceFile(), output: CreateOutputFileArtifact(), tags: new[] { "A", "1" });
            var copyFileA2 = CreateCopyFile(ipcPipA1.OutputFile, CreateOutputFileArtifact(), tags: new[] { "A", "2" });
            var copyFileB1 = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact(), tags: new[] { "B", "1" });
            var ipcPipB2 = CreateTaggedIpcPip(ipcInfo, input: copyFileB1.Destination, output: CreateOutputFileArtifact(), tags: new[] { "B", "2" });

            var process = CreateProcess(
                dependencies: new[] { copyFileA2.Destination, ipcPipB2.OutputFile },
                outputs: new[] { CreateOutputFileArtifact() },
                tags: new[] { "P", "0" });

            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipA1, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileA2, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddCopyFile(copyFileB1, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddIpcPip(ipcPipB2, PipId.Invalid));
            XAssert.IsTrue(PipGraphBuilder.AddProcess(process, PipId.Invalid));

            return new TestGraphForTagFilter
            {
                A1 = ipcPipA1,
                A2 = copyFileA2,
                B1 = copyFileB1,
                B2 = ipcPipB2,
                P = process
            };
        }

        private IEnumerable<StringId> ToStringIds(string values)
        {
            return values == null
                ? CollectionUtilities.EmptyArray<StringId>()
                : values.ToCharArray().Select(c => StringId.Create(Context.StringTable, c.ToString()));
        }

        private IpcPip CreateTaggedIpcPip(IpcClientInfo ipcInfo, string[] tags, string operation = null, FileArtifact input = default(FileArtifact), FileArtifact output = default(FileArtifact))
        {
            return IpcPip.CreateFromStringPayload(
                Context,
                GetWorkingDirectory(),
                ipcInfo,
                operation ?? "hi",
                CreateProvenance(),
                fileDependencies: input.IsValid ? new[] { input } : new FileArtifact[0],
                outputFile: output,
                tags: tags.Select(t => StringId.Create(Context.StringTable, t)));
        }
        #endregion

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        [Trait("Category", "WindowsOSOnly")] // when one opens a file for writing on Unix, others can still read it
        public async Task TestSourceFileLocked()
        {
            Setup();
            FileArtifact inputFile = CreateSourceFile();

            Process process = CreateProcess(
                new[] { inputFile },
                new[] { CreateOutputFileArtifact() });

            XAssert.IsTrue(PipGraphBuilder.AddProcess(process));

            using (var writer = new StreamWriter(inputFile.Path.ToString(Context.PathTable)))
            {
                writer.Write('z');
                await RunScheduler();
            }

            AssertWarningEventLogged(EventId.FailedToHashInputFile);

            AssertErrorEventLogged(LogEventId.PipFailedDueToSourceDependenciesCannotBeHashed);
        }

        /// <summary>
        /// Makes sure a cached graph works with indirect SealDirectory pip dependencies, which will have HashSourceFile
        /// dependencies that are RunnableOnDemand
        /// </summary>
        [Feature(Features.SealedDirectory)]
        [Fact]
        public async Task ReuseCachedGraphWithSealedDirectory()
        {
            Setup(

                // We assert on Succeeded status rather than one of the lazy-materialization ones.
                disableLazyOutputMaterialization: true);
            IgnoreWarnings();

            // CreateSourceFile dependency tree P2 -> P1 -> SealDirectory
            // and add it to the scheduler
            PipProvenance sharedProvenance = CreateProvenance();
            FileArtifact sealDirectoryInput = CreateSourceFile();
            FileArtifact p1Output = CreateOutputFileArtifact();
            FileArtifact p2Output = CreateOutputFileArtifact();
            DirectoryArtifact sealDirectory = PipGraphBuilder.AddSealDirectory(
                new SealDirectory(
                    sealDirectoryInput.Path,
                    SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(new[] { sealDirectoryInput }, OrdinalFileArtifactComparer.Instance),
                    SealDirectoryKind.Full,
                    sharedProvenance,
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<StringId>.Empty));

            Process p1 = CreateProcess(
                dependencies: new[] { CreateSourceFile() },
                directoryDependencies: new[] { sealDirectory },
                outputs: new[] { p1Output },
                provenance: sharedProvenance,
                directoryDependenciesToConsume: new[] { sealDirectoryInput });
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] { CreateSourceFile(), p1Output },
                outputs: new[] { p2Output },
                tags: new[] { "P2" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            using (MemoryStream stream = new MemoryStream())
            {
                // Serialize the schedule
                BuildXLWriter writer = new BuildXLWriter(true, stream, true, true);
                PipTable.Serialize(writer, maxDegreeOfParallelism: Environment.ProcessorCount);
                PipGraphBuilder.DataflowGraph.Serialize(writer);
                var pipGraph = PipGraphBuilder.Build();
                pipGraph.Serialize(writer);

                // Deserialize the schedule and run all pips using a cache.
                var cache = InMemoryCacheFactory.Create();
                await DeserializeScheduleAndRun(stream, cache, null, disableLazyOutputMaterialization: true);

                // Deserialize a second time with the same cache. This time run pips based on a filter.
                var pipResults =
                    await
                        DeserializeScheduleAndRun(
                            stream,
                            cache,
                            new RootFilter(new TagFilter(StringId.Create(Context.StringTable, "P2"))),
                            disableLazyOutputMaterialization: true);

                // Ensure P1 is a hit from the previous run
                PipResultStatus pipResult;
                XAssert.IsTrue(pipResults.TryGetValue(p1.PipId, out pipResult));

                XAssert.AreEqual(PipResultStatus.UpToDate, pipResult);
            }
        }

        [Fact]
        public void UnresponsivenessFactorTests()
        {
            DateTime baseTime = new DateTime(2001, 1, 1);

            XAssert.AreEqual(0, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(0, baseTime, baseTime.AddSeconds(2)));
            XAssert.AreEqual(0, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(0, baseTime, baseTime.AddSeconds(-2)));
            XAssert.AreEqual(0, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(2000, DateTime.MaxValue, baseTime.AddSeconds(2)));
            XAssert.AreEqual(1, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(2000, baseTime, baseTime.AddSeconds(2)));
            XAssert.AreEqual(2, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(2000, baseTime, baseTime.AddSeconds(4.1)));
            XAssert.AreEqual(5, global::BuildXL.Scheduler.Scheduler.ComputeUnresponsivenessFactor(2000, baseTime, baseTime.AddSeconds(10.1)));
        }

        [Fact]
        public void TestInvalidPreserveOutputsFlag()
        {
            Setup();
            CreateSourceFile(NonHashableRoot);

            var output = CreateOutputFileArtifact();
            var processPipBuilder = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(output)
                .WithPreserveOutputWhitelist(output.Path);

            XAssert.IsFalse(PipGraphBuilder.AddProcess(processPipBuilder.Build()));
            AssertSchedulerErrorEventLogged(EventId.ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag);

            var processPipBuilder2 = NewProcessBuilderWithPreDeterminedArgumentsFactory()
                .WithOutputs(CreateOutputFileArtifact())
                .WithOptions(Process.Options.AllowPreserveOutputs)
                .WithPreserveOutputWhitelist(CreateOutputFileArtifact().Path);

            XAssert.IsFalse(PipGraphBuilder.AddProcess(processPipBuilder2.Build()));
            AssertSchedulerErrorEventLogged(EventId.ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist);

        }

        private async Task<ConcurrentDictionary<PipId, PipResultStatus>> DeserializeScheduleAndRun(Stream stream, EngineCache cache, RootFilter filter, bool disableLazyOutputMaterialization = false)
        {
            stream.Position = 0;
            BuildXLReader reader = new BuildXLReader(true, stream, true);
            PipTable m_pipTable = await PipTable.DeserializeAsync(reader, Task.FromResult<PathTable>(Context.PathTable),
                Task.FromResult<SymbolTable>(Context.SymbolTable),
                10,
                10,
                false);

            var configuration = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "config.ds")));
            configuration.Schedule.StopOnFirstError = false;
            configuration.Schedule.EnableLazyOutputMaterialization = !disableLazyOutputMaterialization;

            PipQueue queue = new PipQueue(configuration.Schedule);
            var testQueue = new TestPipQueue(queue, LoggingContext, true);
            var context = Task.FromResult<PipExecutionContext>(new SchedulerContext(Context));
            DeserializedDirectedGraph directedGraph = await DeserializedDirectedGraph.DeserializeAsync(reader);
            PipGraph graph = await PipGraph.DeserializeAsync(
                reader,
                LoggingContext,
                Task.FromResult(m_pipTable),
                Task.FromResult(directedGraph),
                context,
                Task.FromResult<SemanticPathExpander>(Expander));

            var newScheduler = new TestScheduler(
                graph,
                pipQueue: testQueue,
                context: context.Result,
                loggingContext: LoggingContext,
                fileContentTable: m_fileContentTable,
                fileAccessWhitelist: new FileAccessWhitelist(Context),
                configuration: configuration,
                tempCleaner: null,
                cache: cache,
                testHooks: new SchedulerTestHooks());

            newScheduler.InitForMaster(LoggingContext, filter);

            testQueue.Unpause();
            newScheduler.Start(LoggingContext);
            await newScheduler.WhenDone();
            newScheduler.Dispose();

            return newScheduler.PipResults;
        }

        /// <summary>
        /// Asserts that the given (verbose) event id occurs no less than <paramref name="minOccurrences"/> and no more than <paramref name="maxOccurrences"/> times in the event log.
        /// </summary>
        private void AssertVerboseEventCountIsInInterval(EventId eventId, int minOccurrences = 1, int maxOccurrences = 1)
        {
            Contract.Requires(minOccurrences >= 0);
            Contract.Requires(minOccurrences <= maxOccurrences);

            int occurrences = EventListener.GetEventCount(eventId);
            XAssert.IsTrue(occurrences >= minOccurrences && occurrences <= maxOccurrences,
                "Event {0} should have been logged between {1} and {2} times, but got logged {3} times.", eventId.ToString("G"), minOccurrences, maxOccurrences, occurrences);
        }

        /// <summary>
        /// Creates a filter for the legacy tag whitelist and blacklist
        /// </summary>
        private static RootFilter CreateFilterForTags(IEnumerable<StringId> tagsWhitelist, IEnumerable<StringId> tagsBlacklist)
        {
            Contract.Requires(tagsWhitelist != null);
            Contract.Requires(tagsBlacklist != null);
            Contract.Requires(tagsWhitelist.Any() ^ tagsBlacklist.Any());

            FilterOperator filterOperator = FilterOperator.Or;
            bool isNegated = false;
            IEnumerable<StringId> tagsToFilter = null;

            if (tagsWhitelist.Any())
            {
                filterOperator = FilterOperator.Or;
                tagsToFilter = tagsWhitelist;
            }
            else if (tagsBlacklist.Any())
            {
                filterOperator = FilterOperator.And;
                isNegated = true;
                tagsToFilter = tagsBlacklist;
            }

            Contract.Assume(tagsToFilter != null);

            PipFilter filter = null;
            foreach (StringId tag in tagsToFilter)
            {
                PipFilter tagFilter = new TagFilter(tag);
                if (isNegated)
                {
                    tagFilter = tagFilter.Negate();
                }

                filter = filter == null ? tagFilter : new BinaryFilter(filter, filterOperator, tagFilter);
            }

            return new RootFilter(filter);
        }
    }
}
