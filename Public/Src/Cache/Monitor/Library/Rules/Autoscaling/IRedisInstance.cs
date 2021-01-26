// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;

namespace BuildXL.Cache.Monitor.Library.Rules.Autoscaling
{
    public interface IRedisInstance
    {
        string Id { get; }

        string Name { get; }

        string State { get; }

        bool IsFailed { get; }

        bool IsReadyToScale { get; }

        public RedisClusterSize ClusterSize { get; }

        Task<BoolResult> RefreshAsync(CancellationToken cancellationToken = default);

        Task<BoolResult> ScaleAsync(OperationContext context, IReadOnlyList<RedisClusterSize> scalePath);
    }
}
