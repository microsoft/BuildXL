// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <summary>
    /// Context for an individual operation.
    /// </summary>
    public readonly struct OperationContext
    {
        static OperationContext()
        {
            // Ensure result exceptions are demystified.
            // This class is called almost ubiquitously by code utilizing the cache. So this ensures that most
            // cases are covered without needing to handle all executables.
            BuildXL.Cache.ContentStore.Utils.ResultsExtensions.InitializeResultExceptionPreprocessing();
        }

        /// <summary>
        /// Tracing context for an operation.
        /// </summary>
        public Context TracingContext { get; }

        /// <summary>
        /// Optional cancellation token for an operation.
        /// </summary>
        public CancellationToken Token { get; }

        /// <nodoc />
        public OperationContext(Context tracingContext, CancellationToken token = default)
        {
            TracingContext = tracingContext;
            Token = token;
        }

        /// <nodoc />
        public OperationContext CreateNested()
        {
            return new OperationContext(new Context(TracingContext), Token);
        }

        /// <nodoc />
        public OperationContext CreateNested(Guid id)
        {
            return new OperationContext(new Context(TracingContext, id), Token);
        }

        /// <nodoc />
        public OperationContext CreateNested(CancellationToken linkedCancellationToken)
        {
            var token = CancellationTokenSource.CreateLinkedTokenSource(Token, linkedCancellationToken).Token;
            return new OperationContext(new Context(TracingContext), token);
        }

        /// <summary>
        /// Implicit conversion from <see cref="OperationContext"/> to <see cref="Context"/>.
        /// </summary>
        /// <remarks>
        /// Implicit operators may be dangerous, but this conversion is safe and useful.
        /// </remarks>
        public static implicit operator Context(OperationContext context) => context.TracingContext;

        /// <nodoc />
        public void TraceDebug(string message)
        {
            TracingContext.TraceMessage(Severity.Debug, message);
        }

        /// <nodoc />
        public void TraceInfo(string message)
        {
            TracingContext.TraceMessage(Severity.Info, message);
        }

        /// <nodoc />
        public Task<T> PerformInitializationAsync<T>(Tracer operationTracer, Func<Task<T>> operation, Counter? counter = default, [CallerMemberName]string? caller = null)
            where T : ResultBase
        {
            return this.CreateInitializationOperation(operationTracer, operation)
                .WithOptions(counter, traceErrorsOnly: false, traceOperationStarted: false, traceOperationFinished: true, extraStartMessage: null, endMessageFactory: null)
                .RunAsync(caller);
        }

        /// <nodoc />
        public T PerformOperation<T>(
            Tracer operationTracer,
            Func<T> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            Func<T, string>? messageFactory = null,
            string? extraStartMessage = null,
            [CallerMemberName]string? caller = null) where T : ResultBase
        {
            return this.CreateOperation(operationTracer, operation)
                .WithOptions(counter, traceErrorsOnly, traceOperationStarted, traceOperationFinished, extraStartMessage, messageFactory)
                .Run(caller);
        }

        /// <nodoc />
        public Task<T> PerformOperationAsync<T>(
            Tracer operationTracer,
            Func<Task<T>> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string? extraStartMessage = null,
            Func<T, string>? extraEndMessage = null,
            [CallerMemberName]string? caller = null) where T : ResultBase
        {
            return this.CreateOperation(operationTracer, operation)
                .WithOptions(counter, traceErrorsOnly, traceOperationStarted, traceOperationFinished, extraStartMessage, extraEndMessage)
                .RunAsync(caller);
        }

        /// <nodoc />
        public Task<T> PerformNonResultOperationAsync<T>(
            Tracer operationTracer,
            Func<Task<T>> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string? extraStartMessage = null,
            Func<T, string>? extraEndMessage = null,
            Func<T, ResultBase>? resultBaseFactory = null,
            [CallerMemberName]string? caller = null)
        {

            return this.CreateNonResultOperation(operationTracer, operation, resultBaseFactory)
                .WithOptions(counter, traceErrorsOnly, traceOperationStarted, traceOperationFinished, extraStartMessage, extraEndMessage)
                .RunAsync(caller);
        }
    }
}
