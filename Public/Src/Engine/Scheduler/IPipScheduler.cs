// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Pips.Filter;
using BuildXL.Processes;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A scheduler for pips
    /// </summary>
    /// <remarks>
    /// All methods are thread-safe.
    /// </remarks>
    public interface IPipScheduler
    {
        /// <summary>
        /// Initialize runtime state, optionally apply a filter and schedule all ready pips.
        /// Do not start the actual execution.
        /// </summary>
        bool InitForOrchestrator([NotNull]LoggingContext loggingContext, RootFilter filter = null, SchedulerState state = null, ISandboxConnection sandboxConnectionKext = null);

        /// <summary>
        /// Start running.
        /// </summary>
        void Start([NotNull]LoggingContext loggingContext);

        /// <summary>
        /// Indicates when a task is done
        /// </summary>
        Task<bool> WhenDone();
    }
}
