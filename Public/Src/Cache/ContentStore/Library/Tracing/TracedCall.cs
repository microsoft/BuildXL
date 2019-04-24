// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Machinery for tracing the execution of some call/code.
    /// </summary>
    public abstract class TracedCall<TTracer, TResult>
        where TResult : ResultBase
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private readonly CancellationToken _token; // Optional cancellation token for a current operation.

        /// <summary>
        ///     The tracer instance.
        /// </summary>
        protected TTracer Tracer { get; }

        /// <summary>
        ///     The call tracing context.
        /// </summary>
        protected Context Context { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="TracedCall{TTracer, TResult}"/> class without a counter.
        /// </summary>
        protected TracedCall(TTracer tracer, Context context)
        {
            Tracer = tracer;
            Context = context;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="TracedCall{TTracer, TResult}"/> class without a counter.
        /// </summary>
        protected TracedCall(TTracer tracer, OperationContext context)
        {
            Tracer = tracer;
            Context = context;
            _token = context.Token;
        }

        /// <summary>
        ///     Gets result of the called code.
        /// </summary>
        protected TResult Result { get; private set; }

        /// <summary>
        ///     Create a result given an exception that has occurred.
        /// </summary>
        protected abstract TResult CreateErrorResult(Exception exception);

        /// <summary>
        ///     Run the call/code.
        /// </summary>
        protected internal async Task<TResult> RunAsync(Func<Task<TResult>> asyncFunc)
        {
            try
            {
                _stopwatch.Start();
                Result = await asyncFunc();
            }
            catch (Exception exception)
            {
                Result = CreateErrorResult(exception);

                if (_token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(exception))
                {
                    Result.IsCancelled = true;
                }
            }
            finally
            {
                _stopwatch.Stop();
                Result.Duration = _stopwatch.Elapsed;
            }

            return Result;
        }

        /// <summary>
        ///     Run the call/code.
        /// </summary>
        protected TResult Run(Func<TResult> func)
        {
            try
            {
                _stopwatch.Start();
                Result = func();
            }
            catch (Exception exception)
            {
                Result = CreateErrorResult(exception);

                if (_token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(exception))
                {
                    Result.IsCancelled = true;
                }
            }
            finally
            {
                _stopwatch.Stop();
                Result.Duration = _stopwatch.Elapsed;
            }

            return Result;
        }
    }
}
