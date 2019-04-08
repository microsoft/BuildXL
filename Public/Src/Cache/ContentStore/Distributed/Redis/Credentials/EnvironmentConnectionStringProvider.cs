// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

namespace BuildXL.Cache.ContentStore.Distributed.Redis.Credentials
{
    /// <summary>
    /// Provides a connection string based on an environment variables
    /// </summary>
    public class EnvironmentConnectionStringProvider : IConnectionStringProvider
    {
        /// <summary>
        /// Environment variable for directly providing a connection string (usually for testing)
        /// </summary>
        public const string RedisConnectionStringEnvironmentVariable = "CloudStoreRedisConnectionString";

        private readonly string _connectionStringVariableName;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnvironmentConnectionStringProvider"/> class.
        /// </summary>
        public EnvironmentConnectionStringProvider(string environmentVariableName)
        {
            _connectionStringVariableName = environmentVariableName;
        }

        /// <inheritdoc />
        public Task<ConnectionStringResult> GetConnectionString()
        {
            var value = Environment.GetEnvironmentVariable(_connectionStringVariableName);
            return Task.FromResult(value == null
                ? ConnectionStringResult.CreateFailure(
                    $"Expected connection string environment variable not defined: [{_connectionStringVariableName}]'")
                : ConnectionStringResult.CreateSuccess(value));
        }
    }
}
