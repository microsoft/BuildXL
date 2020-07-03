// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks.Dataflow;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     An interface for a batch block with nagle support.
    /// </summary>
    [Obsolete("Use NagleQueue<T> instead.")]
    public interface INagleBlock<T> : IPropagatorBlock<T, T[]>
    {
    }
}
