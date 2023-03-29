// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Denotes an object that has an end time
    /// </summary>
    public interface IHasEndTime
    {
        /// <summary>
        /// Elapsed ms
        /// </summary>
        int ElapsedMilliseconds { get; set; }
    }
}
