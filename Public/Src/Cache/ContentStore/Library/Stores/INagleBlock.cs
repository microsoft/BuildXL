// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
