// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Dispatcher queue kinds
    /// </summary>
    public enum WorkerPipState
    {
        /// <summary>
        /// Worker has been chosen to do the cache lookup for the pip
        /// </summary>
        ChosenForCacheLookup,

        /// <summary>
        /// Worker has been chosen to execute the pip
        /// </summary>
        ChosenForExecution,

        /// <summary>
        /// Sending result to worker
        /// </summary>
        Sending,

        /// <summary>
        /// Pip queued on worker
        /// </summary>
        Queued,

        /// <summary>
        /// Pip materializing inputs on worker
        /// </summary>
        Prepping,

        /// <summary>
        /// Pip materializing inputs on worker done
        /// </summary>
        Prepped,

        /// <summary>
        /// The pip is executing
        /// </summary>
        Executing,

        /// <summary>
        /// The pip executed
        /// </summary>
        Executed,

        /// <summary>
        /// Serializing result on worker
        /// </summary>
        Recording,

        /// <summary>
        /// Reporting result
        /// </summary>
        Reporting,

        /// <summary>
        /// Reported result
        /// </summary>
        Reported,

        /// <summary>
        /// The pip succeeded
        /// </summary>
        Done,

        /// <summary>
        /// The pip failed
        /// </summary>
        Failed,
    }

    /// <summary>
    /// Extension methods for <see cref="WorkerPipState"/>
    /// </summary>
    public static class WorkerPipStateExtensions
    {
        /// <summary>
        /// Gets whether the given state is reported to the master
        /// </summary>
        public static bool IsReportedState(this WorkerPipState state)
        {
            switch (state)
            {
                case WorkerPipState.Recording:
                case WorkerPipState.Reporting:
                case WorkerPipState.Reported:
                    return false;
                default:
                    return true;
            }
        }
    }
}
