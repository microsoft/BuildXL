// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips
{
    /// <summary>
    /// Object pools for pip types.
    /// </summary>
    public static class PipPools
    {
        /// <summary>
        /// Global pool of sets of <see cref="PipId" />s.
        /// </summary>
        public static ObjectPool<HashSet<PipId>> PipIdSetPool { get; } =
            new ObjectPool<HashSet<PipId>>(
                () => new HashSet<PipId>(EqualityComparer<PipId>.Default),
                s => s.Clear());
    }
}
