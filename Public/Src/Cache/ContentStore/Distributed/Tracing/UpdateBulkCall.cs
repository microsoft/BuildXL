// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of an UpdateBulk operation for tracing purposes.
    /// </summary>
    public sealed class UpdateBulkCall : TracedCallWithInput<RedisContentLocationStoreTracer, ValueTuple<IReadOnlyList<ContentHashWithSizeAndLocations>, MachineId?>, BoolResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesWithSizeAndLocations,
            MachineId? machineId,
            Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new UpdateBulkCall(tracer, context, contentHashesWithSizeAndLocations, machineId))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateBulkCall"/> class.
        /// </summary>
        private UpdateBulkCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesWithSizeAndLocations,
            MachineId? machineId)
            : base(tracer, context, ValueTuple.Create(contentHashesWithSizeAndLocations, machineId))
        {
            Tracer.UpdateBulkStart();
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.UpdateBulkStop(Context, Input, Result);
        }
    }
}
