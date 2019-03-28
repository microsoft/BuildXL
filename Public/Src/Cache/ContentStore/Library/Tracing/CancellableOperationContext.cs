// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Tracing.Internal
{
    /// <summary>
    /// Operation context with additional managed resources associated with it.
    /// </summary>
    public readonly struct CancellableOperationContext : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        /// <nodoc />
        public OperationContext Context { get; }

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
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        /// <nodoc />
        public static implicit operator OperationContext(CancellableOperationContext context) => context.Context;
    }
}
