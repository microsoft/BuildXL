// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
