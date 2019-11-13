// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Scheduler.Filter;
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
        bool InitForMaster([NotNull]LoggingContext loggingContext, RootFilter filter = null, SchedulerState state = null, ISandboxConnection sandboxConnectionKext = null);

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
