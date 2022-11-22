// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <summary>
    /// An operation context that triggers cancellation when one of the cancellation tokens provided to the constructor are cancelled.
    /// </summary>
    public readonly struct CancellableOperationContext : IDisposable
    {
        private readonly CancellationTokenSource? _cts;
        private readonly CancellationTokenRegistration? _registration;

        /// <nodoc />
        public OperationContext Context { get; }

        /// <summary>
        /// Creates an instance with parent context and the token with delayed cancellation.
        /// </summary>
        /// <remarks>
        /// When <paramref name="delayToken"/> or `parentContext.Token` are triggered then the _cts is not canceled automatically.
        /// Instead, it'll be canceled after the delay.
        /// </remarks>
        public CancellableOperationContext(OperationContext parentContext, CancellationToken delayToken, TimeSpan delay)
        {
            // In some case it can be problematic if we want to eagerly cancel on one token but lazily on the other.
            // In practice, both tokens might be triggered because of the shutdown of different pieces of the system.
            _cts = new CancellationTokenSource();
            _registration = delayToken.Register(
                (state) =>
                {
                    ((CancellationTokenSource?)state)!.CancelAfter(delay);
                }, _cts);

            Context = new OperationContext(parentContext, _cts.Token);
        }

        /// <nodoc />
        public CancellableOperationContext(OperationContext parentContext, CancellationToken secondaryToken)
        {
            // To prevent a memory leak, linked cancellation token should be disposed.
            // Creating a linked token only when parentContext.Token was provided.
            // Otherwise using a shutdown cancellation token directly.
            _cts = parentContext.Token.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parentContext.Token, secondaryToken)
                : null;

            var token = _cts?.Token ?? secondaryToken;

            // Note, that this call does not create a nested tracing context.
            Context = new OperationContext(parentContext, token);
            _registration = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // No need to cancel the source.
            // We don't want to have a scope based cancellation here.
            _cts?.Dispose();
            _registration?.Dispose();
        }

        /// <nodoc />
        public static implicit operator OperationContext(CancellableOperationContext context) => context.Context;

        /// <nodoc />
        public static implicit operator Context(CancellableOperationContext context) => context.Context.TracingContext;
    }
}
