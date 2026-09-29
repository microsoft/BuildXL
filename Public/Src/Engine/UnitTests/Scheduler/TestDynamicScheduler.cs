// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
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
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Dynamic scheduler wrapper that captures the same result data as <see cref="TestScheduler"/>.
    /// </summary>
    public sealed class TestDynamicScheduler : DynamicScheduler, ITestScheduler
    {
        private readonly TestPipQueue m_testPipQueue;
        private readonly IConfiguration m_configuration;

        public ConcurrentDictionary<PipId, PipResultStatus> PipResults => RunData.PipResults;

        public ScheduleRunData RunData { get; } = new ScheduleRunData();

        public TestDynamicScheduler(
            IDynamicGraph graph,
            TestPipQueue pipQueue,
            PipExecutionContext context,
            FileContentTable fileContentTable,
            EngineCache cache,
            IConfiguration configuration,
            FileAccessAllowlist fileAccessAllowlist,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules = null,
            ITempCleaner tempCleaner = null,
            JournalState journalState = null,
            PerformanceCollector performanceCollector = null,
            string fingerprintSalt = null,
            PreserveOutputsInfo? previousInputsSalt = null,
            LoggingContext loggingContext = null,
            DirectoryTranslator directoryTranslator = null,
            VmInitializer vmInitializer = null,
            SchedulerTestHooks testHooks = null,
            FileTimestampTracker fileTimestampTracker = null,
            PipSpecificPropertiesConfig pipSpecificPropertiesConfig = null,
            ObservationReclassifier globalReclassificationRules = null)
            : base(
                graph,
                pipQueue,
                context,
                fileContentTable,
                cache,
                configuration,
                fileAccessAllowlist,
                loggingContext,
                buildEngineFingerprint: null,
                directoryMembershipFingerprinterRules,
                tempCleaner,
                runningTimeTable: null,
                performanceCollector,
                fingerprintSalt,
                previousInputsSalt,
                directoryTranslator,
                ipcProvider: null,
                pipTwoPhaseCache: null,
                journalState,
                vmInitializer,
                testHooks,
                fileTimestampTracker,
                isTestScheduler: true,
                pipSpecificPropertiesConfig,
                globalReclassificationRules)
        {
            m_testPipQueue = pipQueue;
            m_configuration = configuration;
        }

        public override async Task OnPipCompleted(RunnablePip runnablePip)
        {
            var pipId = runnablePip.Pip.PipId;

            if (runnablePip.Result.HasValue)
            {
                PipResults[pipId] = runnablePip.Result.Value.Status;

                if (runnablePip.PipType == PipType.Process)
                {
                    RunData.CacheLookupResults[pipId] = ((ProcessRunnablePip)runnablePip).CacheResult;
                    RunData.ExecutionCachingInfos[pipId] = runnablePip.ExecutionResult?.TwoPhaseCachingInfo;
                    RunData.RunnablePipPerformanceInfos[pipId] = runnablePip.Performance;
                }
            }

            await base.OnPipCompleted(runnablePip);
            m_testPipQueue.OnPipCompleted(runnablePip.PipId);
        }

        public SchedulerPerformanceInfo LogStatsForTest(LoggingContext loggingContext) => LogStats(loggingContext, null);

        protected override bool InitSandboxConnection(LoggingContext loggingContext, ISandboxConnection sandboxConnection = null)
        {
            if (UnixSandboxingEnabled && sandboxConnection == null && m_configuration.Sandbox.EnableEBPFLinuxSandbox)
            {
                return base.InitSandboxConnection(loggingContext, new SandboxConnectionLinuxEBPF(SandboxFailureCallback, isInTestMode: true));
            }

            return base.InitSandboxConnection(loggingContext, sandboxConnection);
        }
    }
}
