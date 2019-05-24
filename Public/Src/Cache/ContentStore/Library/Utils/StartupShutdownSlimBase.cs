// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Base implementation of <see cref="IStartupShutdownSlim"/> interface.
    /// </summary>
    public abstract class StartupShutdownSlimBase : IStartupShutdownSlim
    {
        private readonly CancellationTokenSource _shutdownStartedCancellationTokenSource = new CancellationTokenSource();

        /// <nodoc />
        protected abstract Tracer Tracer { get; }

        /// <inheritdoc />
        public virtual bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public virtual bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted => _shutdownStartedCancellationTokenSource.Token.IsCancellationRequested;

        /// <summary>
        /// Returns a cancellation token that is triggered when <see cref="ShutdownAsync"/> method is called.
        /// </summary>
        public CancellationToken ShutdownStartedCancellationToken => _shutdownStartedCancellationTokenSource.Token;

        /// <summary>
        /// Creates a cancellable operation context with a cancellation token from the context is canceled or shutdown is requested.
        /// </summary>
        protected CancellableOperationContext TrackShutdown(OperationContext context)
            => new CancellableOperationContext(new OperationContext(context, context.Token), ShutdownStartedCancellationToken);

        /// <summary>
        /// Creates a cancellable operation context with a cancellation token that is triggered when a given token is canceled or shutdown is requested.
        /// </summary>
        protected CancellableOperationContext TrackShutdown(Context context, CancellationToken token)
            => new CancellableOperationContext(new OperationContext(context, token), ShutdownStartedCancellationToken);

        /// <inheritdoc />
        public virtual async Task<BoolResult> StartupAsync(Context context)
        {
            if (StartupStarted)
            {
                Contract.Assert(false, $"Cannot start '{Tracer.Name}' because StartupAsync method was already called on this instance.");
            }

            StartupStarted = true;
            var operationContext = OperationContext(context);
            var result = await operationContext.PerformInitializationAsync(
                Tracer,
                () => StartupCoreAsync(operationContext));
            StartupCompleted = true;

            return result;
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            if (ShutdownStarted)
            {
                Contract.Assert(false, $"Cannot shut down '{Tracer.Name}' because ShutdownAsync method was already called on this instance.");
            }

            TriggerShutdownStarted();

            if (ShutdownCompleted)
            {
                return BoolResult.Success;
            }

            var operationContext = new OperationContext(context);
            var result = await operationContext.PerformOperationAsync(
                Tracer,
                () => ShutdownCoreAsync(operationContext));
            ShutdownCompleted = true;

            return result;
        }

        /// <nodoc />
        protected void TriggerShutdownStarted()
        {
            _shutdownStartedCancellationTokenSource.Cancel();
        }

        /// <nodoc />
        protected virtual Task<BoolResult> StartupCoreAsync(OperationContext context) => BoolResult.SuccessTask;

        /// <nodoc />
        protected OperationContext OperationContext(Context context)
        {
            return new OperationContext(context, ShutdownStartedCancellationToken);
        }

        /// <summary>
        /// Runs a given function within a newly created operation context.
        /// </summary>
        protected async Task<T> WithOperationContext<T>(Context context, CancellationToken token, Func<OperationContext, Task<T>> func)
        {
            using (var operationContext = TrackShutdown(context, token))
            {
                return await func(operationContext);
            }
        }

        /// <nodoc />
        protected virtual Task<BoolResult> ShutdownCoreAsync(OperationContext context) => BoolResult.SuccessTask;

        /// <nodoc />
        protected virtual void ThrowIfInvalid([CallerMemberName]string operation = null)
        {
            if (!StartupCompleted)
            {
                throw new InvalidOperationException($"The component {Tracer.Name} is not initialized for '{operation}'. Did you forget to call 'Initialize' method?");
            }

            if (ShutdownStarted)
            {
                throw new InvalidOperationException($"The component {Tracer.Name} is shut down for '{operation}'.");
            }
        }
    }
}
