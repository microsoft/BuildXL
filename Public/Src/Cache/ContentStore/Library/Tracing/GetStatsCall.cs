// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                    // There might be some nesting happening where the same tracer is used multiple times in the underlying topology
                    // So for aggregating critical and recoverable errors, just sum those if that's the case
                    result.CounterSet.AddOrSum($"{tracer.GetType().Name}.{tracer.Name}.ErrorStats.CriticalErrors", tracer.NumberOfCriticalErrors);
                    result.CounterSet.AddOrSum($"{tracer.GetType().Name}.{tracer.Name}.ErrorStats.RecoverableErrors", tracer.NumberOfRecoverableErrors);
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
