// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of an TouchBulk operation for tracing purposes.
    /// </summary>
    public sealed class TouchBulkCall : TracedCallWithInput<RedisContentLocationStoreTracer, ValueTuple<IReadOnlyList<ContentHashWithSize>, MachineId?>, BoolResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashWithSize> contentHashesWithSize,
            MachineId? machineId,
            Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new TouchBulkCall(tracer, context, contentHashesWithSize, machineId))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TouchBulkCall"/> class.
        /// </summary>
        private TouchBulkCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashWithSize> contentHashesWithSize,
            MachineId? machineId)
            : base(tracer, context, ValueTuple.Create(contentHashesWithSize, machineId))
        {
            Tracer.TouchBulkStart();
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.TouchBulkStop(Context, Input, Result);
        }
    }
}
