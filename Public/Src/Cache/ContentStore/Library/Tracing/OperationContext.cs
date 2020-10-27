// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public OperationContext CreateNested(string componentName, [CallerMemberName]string? caller = null)
        {
            return new OperationContext(new Context(TracingContext, componentName, caller), Token);
        }

        /// <nodoc />
        public OperationContext CreateNested(Guid id, string componentName, [CallerMemberName]string? caller = null)
        {
            return new OperationContext(new Context(TracingContext, id, componentName, caller), Token);
        }

        /// <summary>
        /// Creates new instance with a given <see cref="CancellationToken"/> without creating nested tracing context.
        /// </summary>
        public CancellableOperationContext WithCancellationToken(CancellationToken linkedCancellationToken)
        {
            return new CancellableOperationContext(this, linkedCancellationToken);
        }

        /// <nodoc />
        public OperationContext CreateNested(CancellationToken linkedCancellationToken, [CallerMemberName]string? caller = null)
        {
            var token = CancellationTokenSource.CreateLinkedTokenSource(Token, linkedCancellationToken).Token;
            return new OperationContext(new Context(TracingContext, caller), token);
        }

        /// <nodoc />
        public async Task<T> WithTimeoutAsync<T>(Func<OperationContext, Task<T>> func, TimeSpan timeout, Func<T>? getTimeoutResult = null)
            where T : ResultBase
        {
            try
            {
                return await PerformAsyncOperationWithTimeoutBuilder<T>.WithOptionalTimeoutAsync(func, timeout, this);
            }
            catch (TimeoutException) when (getTimeoutResult != null)
            {
                return getTimeoutResult();
            }
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
        public void TraceWarning(string message)
        {
            TracingContext.TraceMessage(Severity.Warning, message);
        }

        /// <nodoc />
        public void TraceError(string message)
        {
            TracingContext.TraceMessage(Severity.Error, message);
        }

        /// <nodoc />
        public Task<T> PerformInitializationAsync<T>(Tracer operationTracer, Func<Task<T>> operation, Counter? counter = default, Func<T, string>? endMessageFactory = null, [CallerMemberName]string? caller = null)
            where T : ResultBase
        {
            return this.CreateInitializationOperation(operationTracer, operation)
                .WithOptions(counter, traceErrorsOnly: false, traceOperationStarted: false, traceOperationFinished: true, extraStartMessage: null, endMessageFactory: endMessageFactory)
                .RunAsync(caller);
        }

        /// <summary>
        /// Track metric with a given name and a value in MDM.
        /// </summary>
        public void TrackMetric(string name, long value, string tracerName) => TracingContext.TrackMetric(name, value, tracerName);

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
            bool isCritical = false,
            [CallerMemberName]string? caller = null) where T : ResultBase
        {
            return this.CreateOperation(operationTracer, operation)
                .WithOptions(counter, traceErrorsOnly, traceOperationStarted, traceOperationFinished, extraStartMessage, messageFactory, isCritical: isCritical)
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
            bool isCritical = false,
            TimeSpan? pendingOperationTracingInterval = null,
            [CallerMemberName]string? caller = null) where T : ResultBase
        {
            return this.CreateOperation(operationTracer, operation)
                .WithOptions(
                    counter,
                    traceErrorsOnly,
                    traceOperationStarted,
                    traceOperationFinished,
                    extraStartMessage,
                    extraEndMessage,
                    isCritical: isCritical,
                    pendingOperationTracingInterval: pendingOperationTracingInterval,
                    caller: caller)
                .RunAsync(caller);
        }

        /// <nodoc />
        public Task<T> PerformOperationWithTimeoutAsync<T>(
            Tracer operationTracer,
            Func<OperationContext, Task<T>> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string? extraStartMessage = null,
            Func<T, string>? extraEndMessage = null,
            TimeSpan? timeout = null,
            bool isCritical = false,
            TimeSpan? pendingOperationTracingInterval = null,
            [CallerMemberName] string? caller = null) where T : ResultBase
        {
            return this.CreateOperationWithTimeout(operationTracer, operation, timeout)
                .WithOptions(
                    counter,
                    traceErrorsOnly,
                    traceOperationStarted,
                    traceOperationFinished,
                    extraStartMessage,
                    extraEndMessage,
                    isCritical: isCritical,
                    pendingOperationTracingInterval: pendingOperationTracingInterval,
                    caller: caller)
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
            bool isCritical = false,
            [CallerMemberName]string? caller = null)
        {
            return this.CreateNonResultOperation(operationTracer, operation, resultBaseFactory)
                .WithOptions(counter, traceErrorsOnly, traceOperationStarted, traceOperationFinished, extraStartMessage, extraEndMessage, isCritical: isCritical)
                .RunAsync(caller);
        }
    }
}
