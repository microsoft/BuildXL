// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Options for memory usage
    /// </summary>
    public enum MemoryUsageOption
    {
        /// <summary>
        /// The system will try to pick the best strategy for you
        /// </summary>
        Auto,

        /// <summary>
        /// Avoid caching values in memory when they can be fetched from disk on demand
        /// </summary>
        Minimize,

        /// <summary>
        /// Don't hesitate to use memory for caching values in memory to avoid seeking on disk
        /// </summary>
        Liberal,
    }
}
