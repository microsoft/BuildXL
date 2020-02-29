// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Memory resource
    /// </summary>
    [Flags]
    public enum MemoryResource : byte
    {
        /// <summary>
        /// Available
        /// </summary>
        Available,

        /// <summary>
        /// Low ram memory
        /// </summary>
        LowRam,

        /// <summary>
        /// Low commit memory
        /// </summary>
        LowCommit
    }
}
