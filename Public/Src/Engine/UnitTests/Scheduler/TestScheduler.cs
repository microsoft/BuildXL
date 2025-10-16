// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.VmCommandProxy;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed class TestScheduler : global::BuildXL.Scheduler.Scheduler
    {
        private readonly Dictionary<PipId, PipResultStatus> m_overridePipResults = new Dictionary<PipId, PipResultStatus>();
        private readonly LoggingContext m_loggingContext;

        public ConcurrentDictionary<PipId, PipResultStatus> PipResults => RunData.PipResults;

        public ScheduleRunData RunData { get; } = new ScheduleRunData();

        private readonly TestPipQueue m_testPipQueue;

        private readonly IConfiguration m_configuration;

        public TestScheduler(
            PipGraph graph,
            TestPipQueue pipQueue,
            PipExecutionContext context,
            FileContentTable fileContentTable,
            EngineCache cache,
            IConfiguration configuration,
            FileAccessAllowlist fileAccessAllowlist,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules = null,
            ITempCleaner tempCleaner = null,
            HistoricPerfDataTable runningTimeTable = null,
            JournalState journalState = null,
            PerformanceCollector performanceCollector = null,
            string fingerprintSalt = null,
            PreserveOutputsInfo? previousInputsSalt = null,
            IEnumerable<Pip> successfulPips = null,
            IEnumerable<Pip> failedPips = null,
            LoggingContext loggingContext = null,
            IIpcProvider ipcProvider = null,
            DirectoryTranslator directoryTranslator = null,
            VmInitializer vmInitializer = null,
            SchedulerTestHooks testHooks = null,
            FileTimestampTracker fileTimestampTracker = null,
            PipSpecificPropertiesConfig pipSpecificPropertiesConfig = null,
            ObservationReclassifier globalReclassificationRules = null) : base(graph, pipQueue, context, fileContentTable, cache,
                configuration, fileAccessAllowlist, loggingContext, null, directoryMembershipFingerprinterRules,
                tempCleaner, AsyncLazy<HistoricPerfDataTable>.FromResult(runningTimeTable), performanceCollector, fingerprintSalt, previousInputsSalt,
                ipcProvider: ipcProvider, 
                directoryTranslator: directoryTranslator, 
                journalState: journalState, 
                vmInitializer: vmInitializer,
                testHooks: testHooks,
                fileTimestampTracker: fileTimestampTracker,
                isTestScheduler: true,
                pipSpecificPropertiesConfig: pipSpecificPropertiesConfig,
                globalReclassificationRules: globalReclassificationRules)
        {
            m_testPipQueue = pipQueue;
            m_configuration = configuration;

            if (successfulPips != null)
            {
                foreach (var pip in successfulPips)
                {
                    Contract.Assume(pip.PipId.IsValid, "Override results must be added after the pip has been added to the scheduler");
                    m_overridePipResults.Add(pip.PipId, PipResultStatus.Succeeded);
                }
            }

            if (failedPips != null)
            {
                foreach (var pip in failedPips)
                {
                    Contract.Assume(pip.PipId.IsValid, "Override results must be added after the pip has been added to the scheduler");
                    m_overridePipResults.Add(pip.PipId, PipResultStatus.Failed);
                }
            }

            m_loggingContext = loggingContext;
        }

        public override async Task OnPipCompleted(RunnablePip runnablePip)
        {
            var pipId = runnablePip.Pip.PipId;
            PipResultStatus overrideStatus;
            if (m_overridePipResults.TryGetValue(pipId, out overrideStatus))
            {
                if (overrideStatus.IndicatesFailure())
                {
                    m_loggingContext.SpecifyErrorWasLogged(0);
                }

                runnablePip.SetPipResult(
                    overrideStatus.IndicatesExecution()
                        ? PipResult.CreateWithPointPerformanceInfo(overrideStatus)
                        : PipResult.CreateForNonExecution(overrideStatus));

                if (overrideStatus.IndicatesFailure())
                {
                    m_loggingContext.SpecifyErrorWasLogged(0);
                }
            }

            // Set the 'actual' result. NOTE: override also overrides actual result.
            // We set this before calling the wrapped PipCompleted handler since we may
            // be completing the last pip (don't want to race with a test checking pip
            // result after schedule completion and us setting it.
            PipResults[pipId] = runnablePip.Result.Value.Status;

            if (runnablePip.Result.HasValue && runnablePip.PipType == PipType.Process)
            {
                RunData.CacheLookupResults[pipId] = ((ProcessRunnablePip)runnablePip).CacheResult;
                RunData.ExecutionCachingInfos[pipId] = runnablePip.ExecutionResult?.TwoPhaseCachingInfo;
                RunData.RunnablePipPerformanceInfos[pipId] = runnablePip.Performance;
            }

            await base.OnPipCompleted(runnablePip);

            m_testPipQueue.OnPipCompleted(runnablePip.PipId);
        }

        public void AssertPipResults(
            Pip[] expectedSuccessfulPips = null,
            Pip[] expectedFailedPips = null,
            Pip[] expectedSkippedPips = null,
            Pip[] expectedCanceledPips = null,
            Pip[] expectedUnscheduledPips = null)
        {
            Dictionary<PipId, PipResultStatus?> expectedPipResults = new Dictionary<PipId, PipResultStatus?>();

            expectedSuccessfulPips = expectedSuccessfulPips ?? new Pip[0];
            expectedFailedPips = expectedFailedPips ?? new Pip[0];
            expectedSkippedPips = expectedSkippedPips ?? new Pip[0];
            expectedCanceledPips = expectedCanceledPips ?? new Pip[0];
            expectedUnscheduledPips = expectedUnscheduledPips ?? new Pip[0];

            foreach (var pip in expectedSuccessfulPips)
            {
                Contract.Assume(pip.PipId.IsValid, "Expected results must be added after the pip has been added to the scheduler");
                expectedPipResults.Add(pip.PipId, PipResultStatus.Succeeded);
            }

            foreach (var pip in expectedFailedPips)
            {
                Contract.Assume(pip.PipId.IsValid, "Expected results must be added after the pip has been added to the scheduler");
                expectedPipResults.Add(pip.PipId, PipResultStatus.Failed);
            }

            foreach (var pip in expectedSkippedPips)
            {
                Contract.Assume(pip.PipId.IsValid, "Expected results must be added after the pip has been added to the scheduler");
                expectedPipResults.Add(pip.PipId, PipResultStatus.Skipped);
            }

            foreach (var pip in expectedCanceledPips)
            {
                Contract.Assume(pip.PipId.IsValid, "Expected results must be added after the pip has been added to the scheduler");
                expectedPipResults.Add(pip.PipId, PipResultStatus.Canceled);
            }

            foreach (var pip in expectedUnscheduledPips)
            {
                Contract.Assume(pip.PipId.IsValid, "Expected results must be added after the pip has been added to the scheduler");
                expectedPipResults.Add(pip.PipId, null);
            }

            foreach (var expectedPipResult in expectedPipResults)
            {
                PipResultStatus actualPipResult;

                XAssert.AreEqual(expectedPipResult.Value.HasValue, PipResults.TryGetValue(expectedPipResult.Key, out actualPipResult));

                if (expectedPipResult.Value.HasValue)
                {
                    // Treat DeployedFromCache as Succeeded if that's what we wanted anyway; otherwise it is very hard to guess
                    // if a WriteFile / CopyFile pip will be satisfied from content-cache (many identical files in some tests).
                    if (actualPipResult == PipResultStatus.DeployedFromCache && expectedPipResult.Value.Value == PipResultStatus.Succeeded)
                    {
                        continue;
                    }

                    XAssert.AreEqual(expectedPipResult.Value.Value, actualPipResult);
                }
            }
        }

        /// <inheritdoc/>
        protected override bool InitSandboxConnection(LoggingContext loggingContext, ISandboxConnection sandboxConnection = null)
        {
            // The test scheduler runs in a context where if the EBPF sandbox is enabled, the EBPF daemon is already running, so there is no need
            // to wait for the daemon task to complete (and in this context the daemon task is not actually created).
            // So create a sandbox connection that does not wait for it and pass it downstream
            if (UnixSandboxingEnabled && sandboxConnection == null && m_configuration.Sandbox.EnableEBPFLinuxSandbox)
            {
                return base.InitSandboxConnection(loggingContext, new SandboxConnectionLinuxEBPF(SandboxFailureCallback, isInTestMode: true));
            }

            return base.InitSandboxConnection(loggingContext, sandboxConnection);
        }
    }
}
