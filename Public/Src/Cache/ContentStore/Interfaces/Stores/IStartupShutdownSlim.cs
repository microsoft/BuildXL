// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
