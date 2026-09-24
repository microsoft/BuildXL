// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Minimal scheduler view required by the engine.
    /// </summary>
    public interface IEngineScheduler
    {
        /// <summary>
        /// Gets the pip graph used by the scheduler.
        /// </summary>
        IDynamicGraph PipGraph { get; }

        /// <summary>
        /// Gets the graph selected for scheduling.
        /// </summary>
        IReadonlyDirectedGraph ScheduledGraph { get; }

        /// <summary>
        /// Indicates whether scheduling is terminating.
        /// </summary>
        bool IsTerminating { get; }

        /// <summary>
        /// Requests termination after an internal engine error.
        /// </summary>
        void TerminateForInternalError();

        /// <summary>
        /// Sets the process start time used by scheduler telemetry.
        /// </summary>
        void SetProcessStartTime(DateTime processStartTimeUtc);

        /// <summary>
        /// Gets execution time percentages limited by each resource.
        /// </summary>
        LimitingResourcePercentages GetLimitingResourcePercentages();

        /// <summary>
        /// Gets the process pips currently executing.
        /// </summary>
        IEnumerable<PipReference> RetrieveExecutingProcessPips();
    }
}
