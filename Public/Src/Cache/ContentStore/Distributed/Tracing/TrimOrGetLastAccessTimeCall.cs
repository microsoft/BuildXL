// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of a getting last access time operation for tracing purposes.
    /// </summary>
    public sealed class TrimOrGetLastAccessTimeCall : TracedCallWithInput<RedisContentLocationStoreTracer, IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>>, ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo,
            MachineId? machineId,
            Func<Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>>> funcAsync)
        {
            using (var call = new TrimOrGetLastAccessTimeCall(tracer, context, contentHashesWithInfo, machineId))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrimOrGetLastAccessTimeCall"/> class.
        /// </summary>
        private TrimOrGetLastAccessTimeCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo,
            MachineId? machineId)
            : base(tracer, context, contentHashesWithInfo)
        {
            Tracer.TrimOrGetLastAccessTimeStart(context, contentHashesWithInfo, machineId);
        }

        /// <inheritdoc />
        protected override ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>> CreateErrorResult(Exception exception)
        {
            return new ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.TrimOrGetLastAccessTimeStop(Context, Input, Result);
        }
    }
}
