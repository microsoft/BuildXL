// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Dynamic observation recognized by incremental scheduling.
    /// </summary>
    public enum DynamicObservationType : byte
    {
        /// <summary>
        /// Dynamically observed file read.
        /// </summary>
        ObservedFile = 0,

        /// <summary>
        /// Dynamically observed file probe.
        /// </summary>
        ProbedFile = 1,

        /// <summary>
        /// Dynamically observed directory enumeration.
        /// </summary>
        Enumeration = 2,

        /// <summary>
        /// Dynamically observed absent path probe.
        /// </summary>
        AbsentPathProbe = 3,
    }
}
