// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Dynamic observations considered in the pip's input processing
    /// </summary>
    [Flags]
    public enum DynamicObservationKind : byte
    {
        /// <summary>
        /// Dynamically observed file read.
        /// </summary>
        ObservedFile = 0,

        /// <summary>
        /// Dynamically observed file probe.
        /// </summary>
        ProbedFile = 1 << 1,

        /// <summary>
        /// Dynamically observed directory enumeration.
        /// </summary>
        Enumeration = 1 << 2,

        /// <summary>
        /// Dynamically observed absent path probe
        /// </summary>
        AbsentPathProbe = 1 << 3,

        /// <summary>
        /// Absent path probe under known opaque outputs
        /// </summary>
        AbsentPathProbeUnderOutputDirectory = AbsentPathProbe | 1 << 4,

        /// <summary>
        /// Absent path probe outside known opaque outputs
        /// </summary>
        AbsentPathProbeOutsideOutputDirectory = AbsentPathProbe | 1 << 5,
    }
}
