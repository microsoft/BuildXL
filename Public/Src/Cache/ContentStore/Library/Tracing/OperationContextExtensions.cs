// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <summary>
    /// Configurable defaults for tracing configuration.
    /// </summary>
    public static class DefaultTracingConfiguration
    {
        /// <summary>
        /// If an operation takes longer than this threshold it will be traced regardless of other flags or options.
        /// </summary>
        public static TimeSpan DefaultSilentOperationDurationThreshold { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// A set of extension methods that create helper builder classes for configuring tracing options for an operation.
    /// </summary>
    public static class OperationContextExtensions
    {
        /// <nodoc />
        public static PerformAsyncOperationBuilder<TResult> CreateOperation<TResult>(this OperationContext context, Tracer tracer, Func<Task<TResult>> operation) where TResult : ResultBase
        {
            return new PerformAsyncOperationBuilder<TResult>(context, tracer, operation);
        }

        /// <nodoc />
        public static PerformAsyncOperationNonResultBuilder<TResult> CreateNonResultOperation<TResult>(this OperationContext context, Tracer tracer, Func<Task<TResult>> operation, Func<TResult, ResultBase>? resultBaseFactory = null)
        {
            return new PerformAsyncOperationNonResultBuilder<TResult>(context, tracer, operation, resultBaseFactory);
        }

        /// <nodoc />
        public static PerformInitializationOperationBuilder<TResult> CreateInitializationOperation<TResult>(this OperationContext context, Tracer tracer, Func<Task<TResult>> operation) where TResult : ResultBase
        {
            return new PerformInitializationOperationBuilder<TResult>(context, tracer, operation);
        }

        /// <nodoc />
        public static PerformOperationBuilder<TResult> CreateOperation<TResult>(this OperationContext context, Tracer tracer, Func<TResult> operation) where TResult : ResultBase
        {
            return new PerformOperationBuilder<TResult>(context, tracer, operation);
        }
    }

    /// <nodoc />
    public static class PerformOperationExtensions
    {
        /// <summary>
        /// Create a builder that will trace only operation completion failures and ONLY when the <paramref name="enableTracing"/> is true.
        /// </summary>
        public static TBuilder TraceErrorsOnlyIfEnabled<TResult, TBuilder>(
            this PerformOperationBuilderBase<TResult, TBuilder> builder,
            bool enableTracing,
            Func<TResult, string>? endMessageFactory = null)
            where TBuilder : PerformOperationBuilderBase<TResult, TBuilder>
        {
            return builder.WithOptions(traceErrorsOnly: true, traceOperationStarted: false, traceOperationFinished: enableTracing, endMessageFactory: endMessageFactory);
        }
    }
    /// <summary>
    /// A builder pattern used for perform operations with configurable tracings.
    /// </summary>
    public abstract class PerformOperationBuilderBase<TResult, TBuilder>
        where TBuilder : PerformOperationBuilderBase<TResult, TBuilder>
    {
        /// <nodoc />
        protected readonly OperationContext _context;
        /// <nodoc />
        protected readonly Tracer _tracer;

        /// <nodoc />
        protected Counter? _counter;
        /// <nodoc />
        protected bool _traceErrorsOnly = false;
        /// <nodoc />
        protected bool _traceOperationStarted = true;
        /// <nodoc />
        protected bool _traceOperationFinished = true;
        /// <nodoc />
        protected string? _extraStartMessage;
        /// <nodoc />
        protected Func<TResult, string>? _endMessageFactory;
        /// <nodoc />
        protected TimeSpan _silentOperationDurationThreshold = DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold;
        /// <nodoc />
        protected bool _isCritical;

        private readonly Func<TResult, ResultBase>? _resultBaseFactory;

        /// <summary>
        /// An optional timeout for asynchronous operations.
        /// </summary>
        protected TimeSpan? _timeout;

        /// <nodoc />
        protected PerformOperationBuilderBase(OperationContext context, Tracer tracer, Func<TResult, ResultBase>? resultBaseFactory)
        {
            _context = context;
            _tracer = tracer;
            _resultBaseFactory = resultBaseFactory;
        }

        /// <summary>
        /// Appends the start message to the current start message
        /// </summary>
        public TBuilder AppendStartMessage(string extraStartMessage)
        {
            _extraStartMessage = _extraStartMessage != null
                ? string.Join(" ", _extraStartMessage, extraStartMessage)
                : extraStartMessage;
            return (TBuilder)this;
        }

        /// <summary>
        /// Set tracing options for the operation the builder is responsible for construction.
        /// </summary>
        public TBuilder WithOptions(
            Counter? counter = default,
            bool traceErrorsOnly = false,
            bool traceOperationStarted = true,
            bool traceOperationFinished = true,
            string? extraStartMessage = null,
            Func<TResult, string>? endMessageFactory = null,
            TimeSpan? silentOperationDurationThreshold = null,
            bool isCritical = false,
            TimeSpan? timeout = null)
        {
            _counter = counter;
            _traceErrorsOnly = traceErrorsOnly;
            _traceOperationStarted = traceOperationStarted;
            _traceOperationFinished = traceOperationFinished;
            _extraStartMessage = extraStartMessage;
            _endMessageFactory = endMessageFactory;
            _silentOperationDurationThreshold = silentOperationDurationThreshold ?? DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold;
            _isCritical = isCritical;
            _timeout = timeout;
            return (TBuilder)this;
        }

        /// <nodoc />
        protected void TraceOperationStarted(string caller)
        {
            if (_traceOperationStarted && !_traceErrorsOnly)
            {
                _tracer.OperationStarted(_context, caller, enabled: true, additionalInfo: _extraStartMessage);
            }
        }

        /// <nodoc />
        protected void TraceOperationFinished(TResult result, TimeSpan duration, string caller)
        {
            if (_traceOperationFinished || duration > _silentOperationDurationThreshold)
            {
                string message = _endMessageFactory?.Invoke(result) ?? string.Empty;
                var traceableResult = _resultBaseFactory?.Invoke(result) ?? BoolResult.Success;

                if (_isCritical)
                {
                    traceableResult.MakeCritical();
                }

                // Ignoring _traceErrorsOnly flag if the operation is too long.
                bool traceErrorsOnly = duration > _silentOperationDurationThreshold ? false : _traceErrorsOnly;
                _tracer.OperationFinished(_context, traceableResult, duration, message, caller, traceErrorsOnly: traceErrorsOnly);
            }
        }

        /// <nodoc />
        protected T RunOperationAndConvertExceptionToError<T>(Func<T> operation)
            where T : ResultBase
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                var result = new ErrorResult(ex).AsResult<T>();
                if (_context.Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(ex))
                {
                    result.IsCancelled = true;
                }

                return result;
            }
        }

        /// <nodoc />
        protected static Task<T> WithOptionalTimeoutAsync<T>(Task<T> task, TimeSpan? timeout)
        {
            if (timeout == null)
            {
                return task;
            }

            return task.WithTimeoutAsync(timeout.Value);
        }

        /// <nodoc />
        protected async Task<T> RunOperationAndConvertExceptionToErrorAsync<T>(Func<Task<T>> operation)
            where T : ResultBase
        {
            try
            {
                return await WithOptionalTimeoutAsync(operation(), _timeout);
            }
            catch (Exception ex)
            {
                var result = new ErrorResult(ex).AsResult<T>();
                if (_context.Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(ex))
                {
                    result.IsCancelled = true;
                }

                return result;
            }
        }
    }

    /// <summary>
    /// A builder pattern used for perform operations with configurable tracings.
    /// </summary>
    public class PerformAsyncOperationNonResultBuilder<TResult> : PerformOperationBuilderBase<TResult, PerformAsyncOperationNonResultBuilder<TResult>>
    {
        /// <nodoc />
        protected readonly Func<Task<TResult>> _asyncOperation;

        /// <nodoc />
        public PerformAsyncOperationNonResultBuilder(OperationContext context, Tracer tracer, Func<Task<TResult>> operation, Func<TResult, ResultBase>? resultBaseFactory)
        : base(context, tracer, resultBaseFactory)
        {
            _asyncOperation = operation;
        }

        /// <nodoc />
        public async Task<TResult> RunAsync([CallerMemberName] string? caller = null)
        {
            using (_counter?.Start())
            {
                TraceOperationStarted(caller!);
                var stopwatch = StopwatchSlim.Start();

                try
                {
                    var result = await WithOptionalTimeoutAsync(_asyncOperation(), _timeout);
                    TraceOperationFinished(result, stopwatch.Elapsed, caller!);

                    return result;
                }
                catch (Exception e)
                {
                    var resultBase = new BoolResult(e);

                    if (_isCritical)
                    {
                        resultBase.MakeCritical();
                    }

                    TraceResultOperationFinished(resultBase, stopwatch.Elapsed, caller!);

                    throw;
                }
            }
        }

        private void TraceResultOperationFinished<TOther>(TOther result, TimeSpan duration, string caller) where TOther : ResultBase
        {
            if (_traceOperationFinished || duration > _silentOperationDurationThreshold)
            {
                // Ignoring _traceErrorsOnly flag if the operation is too long.
                bool traceErrorsOnly = duration > _silentOperationDurationThreshold ? false : _traceErrorsOnly;
                _tracer.OperationFinished(_context, result, duration, message: string.Empty, caller, traceErrorsOnly: traceErrorsOnly);
            }
        }
    }

    /// <summary>
    /// A builder pattern used for perform operations with configurable tracings.
    /// </summary>
    public class PerformAsyncOperationBuilder<TResult> : PerformOperationBuilderBase<TResult, PerformAsyncOperationBuilder<TResult>>
        where TResult : ResultBase
    {
        /// <nodoc />
        protected readonly Func<Task<TResult>> _asyncOperation;

        /// <nodoc />
        public PerformAsyncOperationBuilder(OperationContext context, Tracer tracer, Func<Task<TResult>> operation)
        : base(context, tracer, r => r)
        {
            _asyncOperation = operation;
        }

        /// <nodoc />
        public virtual async Task<TResult> RunAsync([CallerMemberName] string? caller = null)
        {
            using (_counter?.Start())
            {
                TraceOperationStarted(caller!);
                var stopwatch = StopwatchSlim.Start();

                var result = await RunOperationAndConvertExceptionToErrorAsync(_asyncOperation);

                TraceOperationFinished(result, stopwatch.Elapsed, caller!);

                return result;
            }
        }
    }

    /// <summary>
    /// A builder pattern used for perform operations with configurable tracings.
    /// </summary>
    public class PerformOperationBuilder<TResult> : PerformOperationBuilderBase<TResult, PerformOperationBuilder<TResult>>
        where TResult : ResultBase
    {
        private readonly Func<TResult> _operation;

        /// <nodoc />
        public PerformOperationBuilder(OperationContext context, Tracer tracer, Func<TResult> operation)
            : base(context, tracer, r => r)
        {
            _operation = operation;
        }

        /// <nodoc />
        public TResult Run([CallerMemberName] string? caller = null)
        {
            using (_counter?.Start())
            {
                TraceOperationStarted(caller!);

                var stopwatch = StopwatchSlim.Start();

                var result = RunOperationAndConvertExceptionToError(_operation);

                TraceOperationFinished(result, stopwatch.Elapsed, caller!);
                return result;
            }
        }
    }

    /// <summary>
    /// A builder pattern used for perform initialization operations with configurable tracings.
    /// </summary>
    public class PerformInitializationOperationBuilder<TResult> : PerformAsyncOperationBuilder<TResult>
        where TResult : ResultBase
    {
        /// <nodoc />
        public PerformInitializationOperationBuilder(OperationContext context, Tracer tracer, Func<Task<TResult>> operation)
        : base(context, tracer, operation)
        {
        }

        /// <inheritdoc />
        public override async Task<TResult> RunAsync([CallerMemberName] string? caller = null)
        {
            using (_counter?.Start())
            {
                TraceOperationStarted(caller!);

                var stopwatch = StopwatchSlim.Start();

                var result = await RunOperationAndConvertExceptionToErrorAsync(_asyncOperation);

                TraceInitializationFinished(result, stopwatch.Elapsed, caller!);

                return result;
            }
        }

        private void TraceInitializationFinished(TResult result, TimeSpan duration, string caller)
        {
            if (_traceOperationFinished)
            {
                string extraMessage = _endMessageFactory?.Invoke(result) ?? string.Empty;
                string message = _tracer.CreateMessageText(result, duration, extraMessage, caller);
                _tracer.InitializationFinished(_context, result, duration, message, caller);
            }
        }
    }
}
