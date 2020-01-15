// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    ///     Setting determining what will be done when nondeterministic values are discovered in the metadata cache.
    /// </summary>
    public enum ReadThroughMode
    {
        /// <summary>
        ///     The nondeterministic value will be returned as-is.
        /// </summary>
        None,

        /// <summary>
        ///     Nondeterministic values will be ignored and the inner cache will be read.
        /// </summary>
        /// <remarks>
        ///     If a deterministic value is found in the inner cache, the nondeterministic value will
        ///     be deleted from the metadata cache to make way for the deterministic value.
        /// </remarks>
        ReadThrough
    }
}
