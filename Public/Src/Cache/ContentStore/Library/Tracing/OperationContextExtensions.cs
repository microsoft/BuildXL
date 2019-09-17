// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
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
        private readonly Func<TResult, ResultBase>? _resultBaseFactory;

        /// <nodoc />
        protected PerformOperationBuilderBase(OperationContext context, Tracer tracer, Func<TResult, ResultBase>? resultBaseFactory = null)
        {
            _context = context;
            _tracer = tracer;
            _resultBaseFactory = resultBaseFactory;
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
            Func<TResult, string>? endMessageFactory = null)
        {
            _counter = counter;
            _traceErrorsOnly = traceErrorsOnly;
            _traceOperationStarted = traceOperationStarted;
            _traceOperationFinished = traceOperationFinished;
            _extraStartMessage = extraStartMessage;
            _endMessageFactory = endMessageFactory;

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
            if (_traceOperationFinished)
            {
                string message = _endMessageFactory?.Invoke(result) ?? string.Empty;
                var traceableResult = _resultBaseFactory?.Invoke(result) ?? BoolResult.Success;
                _tracer.OperationFinished(_context, traceableResult, duration, message, caller, traceErrorsOnly: _traceErrorsOnly);
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
        protected async Task<T> RunOperationAndConvertExceptionToErrorAsync<T>(Func<Task<T>> operation)
            where T : ResultBase
        {
            try
            {
                return await operation();
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

                var result = await _asyncOperation();

                TraceOperationFinished(result, stopwatch.Elapsed, caller!);

                return result;
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
        : base(context, tracer)
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
            : base(context, tracer)
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
