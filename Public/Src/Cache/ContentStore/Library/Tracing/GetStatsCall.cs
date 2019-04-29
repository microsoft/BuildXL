// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a GetStats operation for tracing purposes.
    /// </summary>
    public sealed class GetStatsCall<TTracer> : TracedCall<TTracer, GetStatsResult>, IDisposable
        where TTracer : Tracer
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<GetStatsResult> RunAsync(TTracer tracer, OperationContext context, Func<Task<GetStatsResult>> funcAsync)
        {
            using (var call = new GetStatsCall<TTracer>(tracer, context))
            {
                var result = await call.RunAsync(funcAsync);
                if (result)
                {
                    var stats = new CounterSet();

                    stats.Add("CriticalErrors", tracer.NumberOfCriticalErrors);
                    stats.Add("RecoverableErrors", tracer.NumberOfRecoverableErrors);
                    result.CounterSet.Merge(stats, $"{tracer.GetType().Name}.{tracer.Name}.ErrorStats.");
                }
                
                return result;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetStatsCall{TTracer}"/> class.
        /// </summary>
        private GetStatsCall(TTracer tracer, OperationContext context)
            : base(tracer, context)
        {
            Tracer.GetStatsStart(Context);
        }

        /// <inheritdoc />
        protected override GetStatsResult CreateErrorResult(Exception exception)
        {
            return new GetStatsResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.GetStatsStop(Context, Result);
        }
    }
}
