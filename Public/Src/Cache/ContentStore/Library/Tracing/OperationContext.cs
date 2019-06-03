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

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <summary>
    /// Context for an individual operation.
    /// </summary>
    public readonly struct OperationContext
    {
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
        public OperationTracer Trace(Action operationStarted)
        {
            operationStarted?.Invoke();
            return new OperationTracer(StopwatchSlim.Start());
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
        public async Task<T> PerformInitializationAsync<T>(Tracer operationTracer, Func<Task<T>> operation, Counter? counter = default, [CallerMemberName]string caller = null)
            where T : ResultBase
        {
            var self = this;
            using (counter?.Start())
            {
                var tracer = Trace(() => operationTracer?.OperationStarted(self, caller, enabled: false)); // Don't need to explicitly trace start of initialization
                var result = await RunOperationAndConvertExceptionToErrorAsync(operation);

                tracer.OperationFinished(
                    duration => operationTracer?.InitializationFinished(
                        self,
                        result,
                        tracer.Elapsed,
                        message: operationTracer.CreateMessageText(result, tracer.Elapsed, string.Empty, caller),
                        operationName: caller));
                return result;
            }
        }

        /// <nodoc />
        public T PerformOperation<T>(
            Tracer operationTracer,
            Func<T> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            Func<T, string> messageFactory = null,
            string extraStartMessage = null,
            [CallerMemberName]string caller = null) where T : ResultBase
        {
            var self = this;
            var operationStartedAction = traceOperationStarted
                ? () => operationTracer?.OperationStarted(self, caller, enabled: !traceErrorsOnly, additionalInfo: extraStartMessage)
                : (Action)null;

            using (counter?.Start())
            {
                var tracer = Trace(operationStartedAction);
            
                var result = RunOperationAndConvertExceptionToError(operation);

                var message = messageFactory?.Invoke(result) ?? string.Empty;

                var operationFinishedAction = traceOperationFinished
                    ? duration => operationTracer?.OperationFinished(self, result, duration, message, caller, traceErrorsOnly: traceErrorsOnly)
                    : (Action<TimeSpan>)null;

                tracer.OperationFinished(operationFinishedAction);
                return result;
            }
        }

        /// <nodoc />
        public Task<T> PerformOperationAsync<T>(
            Tracer operationTracer,
            Func<Task<T>> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string extraStartMessage = null,
            Func<T, string> extraEndMessage = null,
            [CallerMemberName]string caller = null) where T : ResultBase
        {
            var self = this;
            return PerformNonResultOperationAsync(
                operationTracer,
                operation: () => self.RunOperationAndConvertExceptionToErrorAsync(operation),
                counter,
                traceErrorsOnly,
                traceOperationStarted,
                traceOperationFinished,
                extraStartMessage,
                extraEndMessage,
                resultBaseFactory: r => r,
                caller);
        }

        /// <nodoc />
        public async Task<T> PerformNonResultOperationAsync<T>(
            Tracer operationTracer,
            Func<Task<T>> operation,
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string extraStartMessage = null,
            Func<T, string> extraEndMessage = null,
            Func<T, ResultBase> resultBaseFactory = null,
            [CallerMemberName]string caller = null)
        {
            var self = this;

            var operationStartedAction = traceOperationStarted
                ? () => operationTracer?.OperationStarted(self, caller, enabled: !traceErrorsOnly, additionalInfo: extraStartMessage)
                : (Action)null;

            using (counter?.Start())
            {
                var tracer = Trace(operationStartedAction);

                var result = await operation();

                var message = extraEndMessage?.Invoke(result) ?? string.Empty;

                var operationFinishedAction = traceOperationFinished
                    ? duration => operationTracer?.OperationFinished(self, resultBaseFactory?.Invoke(result) ?? BoolResult.Success, duration, message, caller, traceErrorsOnly: traceErrorsOnly)
                    : (Action<TimeSpan>)null;

                tracer.OperationFinished(operationFinishedAction);
                return result;
            }
        }

        private T RunOperationAndConvertExceptionToError<T>(Func<T> operation)
            where T : ResultBase
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                var result = new ErrorResult(ex).AsResult<T>();
                if (Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(ex))
                {
                    result.IsCancelled = true;
                }

                return result;
            }
        }

        private async Task<T> RunOperationAndConvertExceptionToErrorAsync<T>(Func<Task<T>> operation)
            where T : ResultBase
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                var result = new ErrorResult(ex).AsResult<T>();
                if (Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(ex))
                {
                    result.IsCancelled = true;
                }

                return result;
            }
        }
    }

    /// <summary>
    /// Disposable struct for tracking an operation duration.
    /// </summary>
    public readonly struct OperationTracer
    {
        private readonly StopwatchSlim _stopwatch;

        /// <nodoc />
        internal OperationTracer(StopwatchSlim stopwatch)
        {
            _stopwatch = stopwatch;
        }

        /// <nodoc />
        public void OperationFinished(Action<TimeSpan> tracer)
        {
            tracer?.Invoke(_stopwatch.Elapsed);
        }

        /// <nodoc />
        public TimeSpan Elapsed => _stopwatch.Elapsed;
    }
}
