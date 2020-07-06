// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        // Tracking instance id to simplify debugging of double shutdown issues.
        private static int CurrentInstanceId;
        private static int GetCurrentInstanceId() => Interlocked.Increment(ref CurrentInstanceId);
        private readonly int _instanceId = GetCurrentInstanceId();

        private readonly CancellationTokenSource _shutdownStartedCancellationTokenSource = new CancellationTokenSource();

        /// <nodoc />
        protected abstract Tracer Tracer { get; }

        /// <summary>
        /// Indicates whether the component supports multiple startup and shutdown calls. If true,
        /// component is ref counted (incremented on startup and decremented on shutdown). When the ref count reaches 0,
        /// the component will actually shutdown. NOTE: Multiple or concurrent startup calls are elided into a single execution
        /// of startup where no startup calls will return until the operation is complete.
        /// </summary>
        public virtual bool AllowMultipleStartupAndShutdowns => false;

        private int _refCount = 0;
        private Lazy<Task<BoolResult>>? _lazyStartupTask;

        /// <inheritdoc />
        public virtual bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public virtual bool ShutdownCompleted { get; private set; }

        /// <nodoc />
        protected virtual Func<BoolResult, string>? ExtraStartupMessageFactory => null;

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
            if (AllowMultipleStartupAndShutdowns)
            {
                Interlocked.Increment(ref _refCount);
            }
            else 
            {
                Contract.Check(!StartupStarted)?.Assert($"Cannot start '{Tracer.Name}' because StartupAsync method was already called on this instance.");
            }
            StartupStarted = true;

            LazyInitializer.EnsureInitialized(ref _lazyStartupTask, () =>
                new Lazy<Task<BoolResult>>(async () =>
                {
                    var operationContext = OperationContext(context);
                    var result = await operationContext.PerformInitializationAsync(
                        Tracer,
                        () => StartupCoreAsync(operationContext),
                        endMessageFactory: r => $"Id={_instanceId}." + ExtraStartupMessageFactory?.Invoke(r));
                    StartupCompleted = true;

                    return result;
                }));

            return await _lazyStartupTask!.Value;
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            if (AllowMultipleStartupAndShutdowns)
            {
                var refCount = Interlocked.Decrement(ref _refCount);
                if (refCount > 0)
                {
                    return BoolResult.Success;
                }
            }

            Contract.Check(!ShutdownStarted)?.Assert($"Cannot shut down '{Tracer.Name}' because ShutdownAsync method was already called on the instance with Id={_instanceId}.");
            TriggerShutdownStarted();

            if (ShutdownCompleted)
            {
                return BoolResult.Success;
            }

            var operationContext = new OperationContext(context);
            var result = await operationContext.PerformOperationAsync(
                Tracer,
                () => ShutdownCoreAsync(operationContext),
                extraEndMessage: r => $"Id={_instanceId}.");
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
        protected virtual void ThrowIfInvalid([CallerMemberName]string? operation = null)
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
