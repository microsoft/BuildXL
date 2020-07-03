// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Provider for connection strings - implemented opaquely to the cache.
    /// </summary>
    public interface IConnectionStringProvider
    {
        /// <summary>
        /// Returns the connection string
        /// </summary>
        Task<ConnectionStringResult> GetConnectionString();
    }
}
