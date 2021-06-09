// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A factory for creating a redis database
    /// </summary>
    internal interface IRedisDatabaseFactory : IStartupShutdownSlim
    {
        Task<RedisDatabaseAdapter> CreateAsync(OperationContext context, string databaseName, string connectionString);
    }
}
