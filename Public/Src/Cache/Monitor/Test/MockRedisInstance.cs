// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;

namespace BuildXL.Cache.Monitor.Test
{
    internal class MockRedisInstance : IRedisInstance
    {
        public string Id => "mock-redis-instance";

        public string Name => "MockRedisInstance";

        public bool IsFailed => State == "Failed";

        public bool IsReadyToScale => State != "Failed" && State != "Scaling";

        public string State { get; set; } = "Running";

        public RedisClusterSize ClusterSize { get; set; }

        public MockRedisInstance(RedisClusterSize clusterSize)
        {
            ClusterSize = clusterSize;
        }

        public Task<BoolResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            return BoolResult.SuccessTask;
        }

        public Task<BoolResult> ScaleAsync(OperationContext context, IReadOnlyList<RedisClusterSize> scalePath)
        {
            throw new System.NotImplementedException();
        }
    }
}
