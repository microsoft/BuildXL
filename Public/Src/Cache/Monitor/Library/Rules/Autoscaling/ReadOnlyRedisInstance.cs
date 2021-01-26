// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.Library.Rules.Autoscaling
{
    internal class ReadOnlyRedisInstance : RedisInstanceBase
    {
        internal ReadOnlyRedisInstance(IAzure azure, string resourceId, IRedisCache redisCache, RedisClusterSize clusterSize)
            : base(azure, resourceId, redisCache, clusterSize)
        {
        }

        public override Task<BoolResult> ScaleAsync(OperationContext context, IReadOnlyList<RedisClusterSize> scalePath)
        {
            // This is very weird. The expectation here is that we will perform an autoscale. The issue is that when
            // working in read-only mode, we can't perform autoscales. I have thought about emulating as if they were
            // actually happening, but it would result in really weird semantics. Instead, we just simulate an API
            // failure.
            return Task.FromResult(new BoolResult(errorMessage: $"Redis instance {Id} (name: {Name}) is read-only"));
        }
    }
}
