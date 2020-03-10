// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Defines a mode of TransitioningContentLocationStore.
    /// </summary>
    [Flags]
    public enum ContentLocationMode
    {
        /// <summary>
        /// Specifies that only RedisContentLocationStore be used.
        /// </summary>
        Redis = 1 << 0,

        /// <summary>
        /// Specifies that only LocalLocationStore should be used
        /// </summary>
        LocalLocationStore = 1 << 1,

        /// <summary>
        /// Specifies that both stores should be used
        /// </summary>
        Both = Redis | LocalLocationStore
    }
}
