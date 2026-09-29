// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Pips.Filter;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Common test surface implemented by the static and dynamic schedulers.
    /// </summary>
    public interface ITestScheduler : IDisposable
    {
        PipExecutionContext Context { get; }

        PipExecutionState State { get; }

        ScheduleRunData RunData { get; }

        CounterCollection<PipExecutorCounter> PipExecutionCounters { get; }

        PipCountersByFilter ProcessPipCountersByFilter { get; }

        PipCountersByTelemetryTag ProcessPipCountersByTelemetryTag { get; }

        long PendingProcessPipExpectedSlots { get; }

        long MaxExternalProcessesRan { get; }

        bool InitForOrchestrator(
            LoggingContext loggingContext,
            RootFilter filter = null,
            SchedulerState schedulerState = null,
            ISandboxConnection sandboxConnection = null);

        void Start(LoggingContext loggingContext);

        void UpdateStatus(bool overwriteable = true, int expectedCallbackFrequency = 0);

        Task<bool> WhenDone();

        Task SaveFileChangeTrackerAsync(LoggingContext loggingContext);

        SchedulerPerformanceInfo LogStatsForTest(LoggingContext loggingContext);
    }
}
