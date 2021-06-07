// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Pip is finished and the result was reported to the orchestrator.
        /// </summary>
        /// <remarks>
        /// When FireForgetMaterializeOutputs is enabled, we transition to this 
        /// state without sending a message to the orchestrator
        /// (i.e. without going through <see cref="WorkerPipState.Reporting"/>)
        /// </remarks>
        Done,
    }

    /// <summary>
    /// Extension methods for <see cref="WorkerPipState"/>
    /// </summary>
    public static class WorkerPipStateExtensions
    {
        /// <summary>
        /// Gets whether the given state is reported to the orchestrator
        /// </summary>
        public static bool IsReportedState(this WorkerPipState state)
        {
            switch (state)
            {
                case WorkerPipState.Recording:
                case WorkerPipState.Reporting:
                case WorkerPipState.Done:
                    return false;
                default:
                    return true;
            }
        }
    }
}
