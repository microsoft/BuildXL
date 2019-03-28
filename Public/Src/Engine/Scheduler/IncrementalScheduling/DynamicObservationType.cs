// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Dynamic observation recognized by incremental scheduling.
    /// </summary>
    public enum DynamicObservationType : byte
    {
        /// <summary>
        /// Dynamically observed file read or possibly path probe.
        /// </summary>
        ObservedFile = 0,

        /// <summary>
        /// Dynamically observed directory enumeration.
        /// </summary>
        Enumeration = 1,
    }
}
