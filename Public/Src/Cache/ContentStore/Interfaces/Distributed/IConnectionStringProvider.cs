// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
