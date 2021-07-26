// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Reference-counting and TTL-observing pool.
    /// </summary>
    /// <typeparam name="TKey">Identifier for a given resource.</typeparam>
    /// <typeparam name="TObject">Type of the pooled object.</typeparam>
    public class ResourcePoolV2<TKey, TObject> : IDisposable
        where TKey : notnull
        where TObject : IStartupShutdownSlim
    {
        private readonly Context _context;
        private readonly ResourcePoolConfiguration _configuration;

        private readonly Dictionary<TKey, ResourceWrapperV2<TObject>> _resources;
        private readonly object _resourcesLock = new object();

        private readonly ConcurrentQueue<ResourceWrapperV2<TObject>> _shutdownQueue = new ConcurrentQueue<ResourceWrapperV2<TObject>>();

        private readonly Func<TKey, TObject> _factory;
        private readonly IClock _clock;

        private readonly Tracer _tracer = new Tracer(nameof(ResourcePoolV2<TKey, TObject>));

        private readonly SemaphoreSlim _gcLock = TaskUtilities.CreateMutex();
        private readonly Task _gcTask;

        private readonly CancellationTokenSource _disposeCancellationTokenSource = new CancellationTokenSource();

        internal CounterCollection<ResourcePoolV2Counters> Counter { get; } = new CounterCollection<ResourcePoolV2Counters>();

        /// <nodoc />
        public ResourcePoolV2(Context context, ResourcePoolConfiguration configuration, Func<TKey, TObject> resourceFactory, IClock? clock = null)
        {
            _context = context;
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;

            _resources = new Dictionary<TKey, ResourceWrapperV2<TObject>>();
            _factory = resourceFactory;

            if (_configuration.GarbageCollectionPeriod != Timeout.InfiniteTimeSpan)
            {
                _gcTask = Task.Run(() => BackgroundGarbageCollectAsync(_disposeCancellationTokenSource.Token));
            }
            else
            {
                _gcTask = Task.CompletedTask;
            }
        }

        /// <summary>
        /// Use a resource obtained or created for a given key.
        /// </summary>
        /// <remarks>
        /// The method may throw <see cref="ObjectDisposedException"/> if the instance is disposed.
        /// Or it may throw <see cref="ResultPropagationException"/> if the resource's StartupAsync fails.
        /// </remarks>
        public async Task<T> UseAsync<T>(Context context, TKey key, Func<ResourceWrapperV2<TObject>, Task<T>> operation)
        {
            if (_disposeCancellationTokenSource.IsCancellationRequested)
            {
                throw new ObjectDisposedException(objectName: _tracer.Name, message: "Attempt to use resource after dispose");
            }

            var wrapper = AcquireWrapper(context, key);
            try
            {
                // NOTE: This can potentially throw. If it happens, then the throw will propagate to the client, which
                // can decide what it wants to do with it. We DON'T release the resource here, because that causes a
                // race condition (whereby the resource has actually already been released by a new thread that's
                // called UseAsync).
                await wrapper.LazyValue;

                // NOTE: resource may be invalid at this point. We can't really prevent this without looping. The
                // reason is that two different threads may have acquired the resource, one of them actually called the
                // startup and then, the other saw the result as it came back, and invalidated the instance as it
                // performed an operation. At that point, the thread that called startup will come back and execute
                // with an invalid instance.
                return await operation(wrapper);
            }
            finally
            {
                wrapper.Release(_clock.UtcNow);
            }
        }

        private ResourceWrapperV2<TObject> AcquireWrapper(Context context, TKey key)
        {
            lock (_resourcesLock)
            {
                ReleaseExpiredWrappers(context, disposing: false);
                var wrapper = AddOrGetWrapper(context, key);

                // We need to increment this here to avoid having another thread release the resource while its being
                // acquired and hence causing a race condition.
                wrapper.Acquire(_clock.UtcNow);

                return wrapper;
            }
        }

        private ResourceWrapperV2<TObject> AddOrGetWrapper(Context context, TKey key)
        {
            var now = _clock.UtcNow;
            if (_resources.TryGetValue(key, out var wrapper))
            {
                if (wrapper.IsAlive(now, _configuration.MaximumAge))
                {
                    return wrapper;
                }

                ReleaseWrapper(context, key, wrapper);
            }

            wrapper = CreateWrapper(context, key);
            _resources.Add(key, wrapper);
            return wrapper;
        }

        private void ReleaseWrapper(Context context, TKey key, ResourceWrapperV2<TObject> wrapper)
        {
            lock (_resourcesLock)
            {
                _resources.Remove(key);
            }

            wrapper.CancelOngoingOperations(context);
            _shutdownQueue.Enqueue(wrapper);

            Counter[ResourcePoolV2Counters.ReleasedResources].Increment();
        }

        private async Task BackgroundGarbageCollectAsync(CancellationToken cancellationToken)
        {
            Contract.Requires(_configuration.GarbageCollectionPeriod > TimeSpan.Zero);

            var context = new OperationContext(_context, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_configuration.GarbageCollectionPeriod, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                // Errors should be already traced
                await GarbageCollectAsync(context).IgnoreFailure();
            }
        }

        /// <summary>
        /// Externally trigger and wait for a garbage collection run to complete
        /// </summary>
        public Task<BoolResult> GarbageCollectAsync(OperationContext context)
        {
            return GarbageCollectAsync(context, disposing: false);
        }

        private Task<BoolResult> GarbageCollectAsync(OperationContext context, bool disposing)
        {
            return context.PerformOperationAsync(_tracer, async () =>
            {
                Counter[ResourcePoolV2Counters.GarbageCollectionAttempts].Increment();

                if (!disposing)
                {
                    await GarbageCollectCoreAsync(context);
                }
                else
                {
                    // The GC on dispose doesn't obey cancellation tokens because it needs to release everything for
                    // correctness.
                    await GarbageCollectOnDisposeCoreAsync(context);
                }

                Counter[ResourcePoolV2Counters.GarbageCollectionSuccesses].Increment();
                return BoolResult.Success;
            },
            extraStartMessage: $"Disposing=[{disposing}]",
            extraEndMessage: _ => $"Disposing=[{disposing}]");
        }

        private async Task GarbageCollectCoreAsync(OperationContext context)
        {
            ReleaseExpiredWrappers(context, disposing: false);

            // Meant to avoid concurrent GCs from taking place. This can happen if Dispose() is called concurrently
            // along an already-running GC.
            using var token = await _gcLock.AcquireAsync(context.Token);

            // WARNING: order is important in this guard
            var size = _shutdownQueue.Count;
            while (!context.Token.IsCancellationRequested && size > 0 && _shutdownQueue.TryDequeue(out var wrapper))
            {
                // Size is constantly decreased because if we don't do it, GC can hang forever waiting until a single
                // usage decreases its reference count on the resource. The fact that we do this means that such usages
                // will take more than one GC pass to release, but that's fine.
                size--;

                if (wrapper.ReferenceCount > 0)
                {
                    _shutdownQueue.Enqueue(wrapper);
                }
                else
                {
                    await ReleaseWrapperAsync(context, wrapper);
                }
            }
        }

        private Task GarbageCollectOnDisposeCoreAsync(OperationContext context)
        {
            ReleaseExpiredWrappers(context, disposing: true);

            var tasks = _shutdownQueue.Select(wrapper => ReleaseWrapperAsync(context, wrapper));
            return Task.WhenAll(tasks);
        }

        private async Task ReleaseWrapperAsync(Context context, ResourceWrapperV2<TObject> wrapper)
        {
            Contract.Requires(wrapper.ShutdownToken.IsCancellationRequested);
            bool initializationCompleted = false;
            try
            {
                // When running GC on Dispose, it is possible for this method to be called with an uninitialized or
                // faulted.
                if (!wrapper.IsValueCreated)
                {
                    // We will still dispose in the finally block
                    return;
                }

                var lazyValueTask = wrapper.LazyValue;

                if (lazyValueTask.IsFaulted)
                {
                    // We will still dispose in the finally block
                    return;
                }

                Counter[ResourcePoolV2Counters.ShutdownAttempts].Increment();
                var instance = await lazyValueTask;
                initializationCompleted = true;

                var result = await instance.ShutdownAsync(context);
                if (result)
                {
                    Counter[ResourcePoolV2Counters.ShutdownSuccesses].Increment();
                }
                else
                {
                    Counter[ResourcePoolV2Counters.ShutdownFailures].Increment();
                }
            }
            catch (Exception exception)
            {
                Counter[ResourcePoolV2Counters.ShutdownExceptions].Increment();

                if (initializationCompleted)
                {
                    _tracer.Error(context, $"Unexpected exception during `{nameof(ResourcePoolV2<TKey, TObject>)}` shutdown: {exception}");
                }
                else
                {
                    // Its possible for the value to fail during the startup and in this case
                    // obtaining the instance itself may fail. This is not an unexpected case.
                    _tracer.Info(context, $"Error obtaining an instance for `{nameof(ResourcePoolV2<TKey, TObject>)}`: {exception}");
                }
            }
            finally
            {
                wrapper.Dispose(context);
            }
        }

        private void ReleaseExpiredWrappers(Context context, bool disposing)
        {
            lock (_resourcesLock)
            {
                foreach (var kvp in _resources.ToList())
                {
                    var key = kvp.Key;
                    var wrapper = kvp.Value;
                    if (!wrapper.IsAlive(_clock.UtcNow, _configuration.MaximumAge) || disposing)
                    {
                        ReleaseWrapper(context, key, wrapper);
                    }
                }
            }
        }

        private ResourceWrapperV2<TObject> CreateWrapper(Context context, TKey key)
        {
            var lazy = new AsyncLazy<TObject>(async () =>
            {
                Counter[ResourcePoolV2Counters.ResourceInitializationAttempts].Increment();

                try
                {
                    var result = await CreateInstanceAsync(context, key);
                    Counter[ResourcePoolV2Counters.ResourceInitializationSuccesses].Increment();
                    return result;
                }
                catch
                {
                    Counter[ResourcePoolV2Counters.ResourceInitializationFailures].Increment();
                    throw;
                }
            });

            var wrapperId = Guid.NewGuid();
            var wrapper = new ResourceWrapperV2<TObject>(wrapperId, lazy, _clock.UtcNow, _disposeCancellationTokenSource.Token);
            Counter[ResourcePoolV2Counters.CreatedResources].Increment();
            _tracer.Info(context, $"Created wrapper with id {wrapperId}");
            return wrapper;
        }

        /// <summary>
        /// Called when a new instance needs to be created. May be overriden by inheritors when startup may be more complex.
        /// </summary>
        /// <remarks>
        /// The method throws if resource initialization fails.
        /// </remarks>
        protected virtual async Task<TObject> CreateInstanceAsync(Context context, TKey key)
        {
            var instance = _factory(key);
            await instance.StartupAsync(context).ThrowIfFailureAsync();
            return instance;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposeCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            // Cancel any ongoing GC and wait for it to complete. There can be no exceptions because the background
            // task swallows them all.
            _disposeCancellationTokenSource.Cancel();
            _gcTask.GetAwaiter().GetResult();

            // Forcefully shutdown all resources
            var context = new OperationContext(_context);
            GarbageCollectAsync(context, disposing: true)
                .GetAwaiter().GetResult()
                // Errors should already be logged
                .ThrowIfFailure();

            _disposeCancellationTokenSource.Dispose();
        }
    }
}
