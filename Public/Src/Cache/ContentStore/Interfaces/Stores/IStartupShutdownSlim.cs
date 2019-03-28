// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// Aggregation of <see cref="IStartup{T}"/> and <see cref="IShutdownSlim{T}"/> interfaces.
    /// </summary>
    public interface IStartupShutdownSlim : IStartup<BoolResult>, IShutdownSlim<BoolResult>
    {
    }
}
