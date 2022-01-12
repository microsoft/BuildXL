// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable disable

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Aggregation of IStartup and IShutdown
    /// </summary>
    public interface IStartupShutdown : IStartup<BoolResult>, IShutdown<BoolResult>, IStartupShutdownSlim
    {
    }
}
