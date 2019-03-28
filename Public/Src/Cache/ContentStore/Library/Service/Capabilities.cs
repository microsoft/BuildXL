// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Set of features exposed or handled by a service or client.
    /// </summary>
    [Flags]
    public enum Capabilities
    {
        /// <summary>
        /// Exposes none of the below capabilities.
        /// </summary>
        None = 0,

        /// <summary>
        /// Is capable of sending or receiving a heartbeat.
        /// </summary>
        Heartbeat = 1,

        /// <summary>
        /// Is capable of sending or receiving a GetStats.
        /// </summary>
        GetStats = 1 << 1,

        /// <summary>
        /// Is capable of sending or receiving memoization requests.
        /// </summary>
        Memoization = 1 << 2,

        /// <summary>
        /// Is capable of sending or receiving content-related requests but not memoization operations.
        /// </summary>
        ContentOnly = All & ~Memoization,

        /// <summary>
        /// All capabilities.
        /// </summary>
        All = Heartbeat | GetStats | Memoization,

        /// <summary>
        /// Set of required capabilities, meaning if a client requests them a service must provide them as well.
        /// </summary>
        RequiredCapabilitiesMask = Memoization,
    }
}
