// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <nodoc />
    public static class OperationContextExtensions
    {
        /// <nodoc />
        public static PerformOperationBuilder WithTracer(this OperationContext context, Tracer tracer) => new PerformOperationBuilder(context, tracer);
    }

    /// <summary>
    /// A builder pattern used for perform operations with configurable tracings.
    /// </summary>
    public struct PerformOperationBuilder
    {
        private readonly OperationContext _context;
        private readonly Tracer _tracer;

        private readonly Counter? _counter;
        private readonly bool _traceErrorsOnly;
        private readonly bool _traceOperationStarted;
        private readonly bool _traceOperationFinished;
        private readonly string _extraStartMessage;

        private PerformOperationBuilder(
            OperationContext context,
            Tracer tracer,
            Counter? counter,
            bool traceErrorsOnly,
            bool traceOperationStarted,
            bool traceOperationFinished,
            string extraStartMessage)
        {
            _context = context;
            _tracer = tracer;
            _counter = counter;
            _traceErrorsOnly = traceErrorsOnly;
            _traceOperationStarted = traceOperationStarted;
            _traceOperationFinished = traceOperationFinished;
            _extraStartMessage = extraStartMessage;
        }

        /// <nodoc />
        public PerformOperationBuilder(OperationContext context, Tracer tracer) : this()
        {
            Contract.Requires(tracer != null);

            (_context, _tracer) = (context, tracer!);
            _traceOperationStarted = false;
            _traceOperationFinished = true;
        }

        /// <nodoc />
        public PerformOperationBuilder WithCounter(Counter counter)
        {
            return new PerformOperationBuilder(_context, _tracer, counter, _traceErrorsOnly, _traceOperationStarted, _traceOperationFinished, _extraStartMessage);
        }

        /// <nodoc />
        public PerformOperationBuilder WithStartAndStop(bool traceOperationStarted, bool traceOperationFinished)
        {
            return new PerformOperationBuilder(_context, _tracer, _counter, _traceErrorsOnly, traceOperationStarted, traceOperationFinished, _extraStartMessage);
        }

        /// <nodoc />
        public PerformOperationBuilder TraceErrorsOnly()
        {
            return new PerformOperationBuilder(_context, _tracer, _counter, traceErrorsOnly: true, _traceOperationStarted, _traceOperationFinished, _extraStartMessage);
        }

        /// <nodoc />
        public PerformOperationBuilder WithExtraStartMessage(string extraStartMessage)
        {
            return new PerformOperationBuilder(_context, _tracer, _counter, _traceErrorsOnly, _traceOperationStarted, _traceOperationFinished, extraStartMessage);
        }

        /// <nodoc />
        public Task<T> RunAsync<T>(Func<Task<T>> operation, Func<T, bool, string>? extraEndMessageFactory = null, [CallerMemberName] string? caller = null)
            where T : ResultBase
        {
            Contract.Assert(_tracer != null, "Default constructor should not be used with this data type.");
            return _context.PerformOperationWithStatusAsync<T>(
                _tracer!,
                operation,
                _counter,
                _traceErrorsOnly,
                _traceOperationStarted,
                _traceOperationFinished,
                _extraStartMessage,
                extraEndMessageFactory,
                caller);
        }
    }
}
