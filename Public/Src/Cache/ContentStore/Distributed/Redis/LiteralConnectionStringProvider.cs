// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Implementation of <see cref="IConnectionStringProvider"/> that gives back a connection string specified via constructor.
    /// </summary>
    public class LiteralConnectionStringProvider : IConnectionStringProvider
    {
        private readonly string _connectionString;

        /// <nodoc />
        public LiteralConnectionStringProvider(string connectionString)
        {
            Contract.Requires(!string.IsNullOrEmpty(connectionString));
            _connectionString = connectionString;
        }

        /// <inheritdoc />
        public Task<ConnectionStringResult> GetConnectionString() => Task.FromResult(ConnectionStringResult.CreateSuccess(_connectionString));
    }
}
