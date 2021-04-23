// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Is capable of creating sessions that publish to the L3.
        /// </summary>
        Publishing = 1 << 3,

        /// <summary>
        /// Is capable of sending or receiving content-related requests but not memoization operations.
        /// </summary>
        ContentOnly = All & ~Memoization & ~Publishing,

        /// <summary>
        /// All capabilities.
        /// </summary>
        All = Heartbeat | GetStats | Memoization | Publishing,

        /// <summary>
        /// Is capable of receiving all requests except creating publishing sessions.
        /// </summary>
        AllNonPublishing = All & ~Publishing,

        /// <summary>
        /// Set of required capabilities, meaning if a client requests them a service must provide them as well.
        /// </summary>
        RequiredCapabilitiesMask = Memoization | Publishing,
    }
}
