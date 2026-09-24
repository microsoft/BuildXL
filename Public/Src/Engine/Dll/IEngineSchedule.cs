// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution;
using BuildXL.Pips;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.ViewModel;

namespace BuildXL.Engine
{
    /// <summary>
    /// Engine-facing contract for a constructed schedule.
    /// </summary>
    /// <remarks>
    /// This interface contains the instance members the engine needs after selecting a schedule implementation.
    /// Implementation-specific construction, loading, and serialization operations remain on the concrete schedule types.
    /// </remarks>
    internal interface IEngineSchedule : IDisposable
    {
        /// <summary>
        /// Gets the execution context associated with this schedule.
        /// </summary>
        EngineContext Context { get; }

        /// <summary>
        /// Gets the scheduler view used by the engine.
        /// </summary>
        IEngineScheduler Scheduler { get; }

        /// <summary>
        /// Gets the immutable graph produced after graph publication completes.
        /// </summary>
        PipGraph FinalizedPipGraph { get; }

        /// <summary>
        /// Gets the table containing the schedule's pips.
        /// </summary>
        IPipTable PipTable { get; }

        /// <summary>
        /// Initializes the schedule for execution.
        /// </summary>
        bool PrepareForBuild(
            LoggingContext loggingContext,
            ICommandLineConfiguration commandLineConfiguration,
            IConfiguration configuration,
            SchedulerState schedulerState,
            ref RootFilter filter,
            IReadOnlyList<string> nonScrubbablePaths,
            EnginePerformanceInfo enginePerformanceInfo,
            bool skipScrubbingOnCleanMachine = false);

        /// <summary>
        /// Executes the pips prepared by the scheduler.
        /// </summary>
        bool ExecuteScheduledPips(LoggingContext loggingContext, WorkerService workerService);

        /// <summary>
        /// Performs post-execution persistence and reporting.
        /// </summary>
        Task<bool> ProcessPostExecutionTasksAsync(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration);

        /// <summary>
        /// Logs scheduler statistics and returns the collected performance information.
        /// </summary>
        SchedulerPerformanceInfo LogStats(LoggingContext loggingContext, BuildSummary buildSummary);

        /// <summary>
        /// Creates or updates the reusable engine state for this schedule.
        /// </summary>
        EngineState GetOrCreateNewEngineState(EngineState previousEngineState);

        /// <summary>
        /// Transfers ownership of the pip table to another engine-owned lifetime.
        /// </summary>
        bool TransferPipTableOwnership(IPipTable table);
    }
}
