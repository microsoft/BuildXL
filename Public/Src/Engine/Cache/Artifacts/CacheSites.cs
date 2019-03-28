// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Cache sites (levels) at which content may exist
    /// </summary>
    /// <remarks>
    /// We only need to model local vs. remote storage for testing scenarios in which 'bytes transferred' / 'files transferred' metrics
    /// need to be authentic.
    /// </remarks>
    [Flags]
    public enum CacheSites
    {
        /// <nodoc />
        None = 0,

        /// <summary>
        /// Local cache only (not available in a remote cache)
        /// </summary>
        Local = 1,

        /// <summary>
        /// Remote cache only (not available in local cache)
        /// </summary>
        Remote = 2,

        /// <summary>
        /// Local and remote (available in either)
        /// </summary>
        LocalAndRemote = Local | Remote,
    }
}
