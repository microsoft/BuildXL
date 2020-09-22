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

        /// <nodoc />
        public async Task<T> UseAsync<T>(TKey key, Func<ResourceWrapperV2<TObject>, Task<T>> operation)
        {
            if (_disposeCancellationTokenSource.IsCancellationRequested)
            {
                throw new ObjectDisposedException(objectName: _tracer.Name, message: "Attempt to use resource after dispose");
            }

            var wrapper = FetchResource(key);
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
                lock (wrapper)
                {
                    wrapper.LastAccessTime = _clock.UtcNow;
                    wrapper.ReferenceCount--;
                }
            }
        }

        private ResourceWrapperV2<TObject> FetchResource(TKey key)
        {
            lock (_resourcesLock)
            {
                ReleaseExpiredResources();
                var wrapper = GetOrCreateWrapper(key);

                // We need to increment this here to avoid having another thread release the resource while its being
                // acquired and hence causing a race condition.
                lock (wrapper)
                {
                    wrapper.LastAccessTime = _clock.UtcNow;
                    wrapper.ReferenceCount++;
                }

                return wrapper;
            }
        }

        private ResourceWrapperV2<TObject> GetOrCreateWrapper(TKey key)
        {
            var now = _clock.UtcNow;
            if (_resources.TryGetValue(key, out var wrapper))
            {
                if (IsWrapperAlive(now, wrapper))
                {
                    return wrapper;
                }

                ReleaseResource(key, wrapper);
            }

            wrapper = CreateWrapper(key);
            _resources.Add(key, wrapper);
            return wrapper;
        }

        private void ReleaseResource(TKey key, ResourceWrapperV2<TObject> wrapper)
        {
            lock (_resourcesLock)
            {
                _resources.Remove(key);
            }

            wrapper.ShutdownCancellationTokenSource.Cancel();
            _shutdownQueue.Enqueue(wrapper);

            Counter[ResourcePoolV2Counters.ReleasedResources].Increment();
        }

        private bool IsWrapperAlive(DateTime now, ResourceWrapperV2<TObject> wrapper)
        {
            lock (wrapper)
            {
                return !wrapper.Invalid && now - wrapper.LastAccessTime < _configuration.MaximumAge;
            }
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
                    await GarbageCollectCoreAsync(context.Token);
                }
                else
                {
                    // The GC on dispose doesn't obey cancellation tokens because it needs to release everything for
                    // correctness.
                    await GarbageCollectOnDisposeCoreAsync();
                }

                Counter[ResourcePoolV2Counters.GarbageCollectionSuccesses].Increment();
                return BoolResult.Success;
            },
            extraStartMessage: $"Disposing=[{disposing}]",
            extraEndMessage: _ => $"Disposing=[{disposing}]");
        }

        private async Task GarbageCollectCoreAsync(CancellationToken cancellationToken = default)
        {
            ReleaseExpiredResources(disposing: false);

            // Meant to avoid concurrent GCs from taking place. This can happen if Dispose() is called concurrently
            // along an already-running GC.
            using var token = await _gcLock.AcquireAsync(cancellationToken);

            // WARNING: order is important in this guard
            var size = _shutdownQueue.Count;
            while (!cancellationToken.IsCancellationRequested && size > 0 && _shutdownQueue.TryDequeue(out var wrapper))
            {
                size--;

                lock (wrapper)
                {
                    if (wrapper.ReferenceCount > 0)
                    {
                        _shutdownQueue.Enqueue(wrapper);
                        continue;
                    }
                }

                await ReleaseWrapperAsync(wrapper);
            }
        }

        private Task GarbageCollectOnDisposeCoreAsync()
        {
            ReleaseExpiredResources(disposing: true);

            var tasks = _shutdownQueue.Select(wrapper => ReleaseWrapperAsync(wrapper));
            return Task.WhenAll(tasks);
        }

        private async Task ReleaseWrapperAsync(ResourceWrapperV2<TObject> wrapper)
        {
            try
            {
                var lazyValueTask = wrapper.LazyValue;
                if (lazyValueTask.IsFaulted)
                {
                    // We will still dispose in the finally block
                    return;
                }

                Counter[ResourcePoolV2Counters.ShutdownAttempts].Increment();
                var instance = await lazyValueTask;
                var result = await instance.ShutdownAsync(_context);
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
                _context.Error($"Unexpected exception during `{nameof(ResourcePoolV2<TKey, TObject>)}` shutdown: {exception}");
            }
            finally
            {
                wrapper.Dispose();
            }
        }

        private void ReleaseExpiredResources(bool disposing = false)
        {
            lock (_resourcesLock)
            {
                foreach (var kvp in _resources.ToList())
                {
                    var key = kvp.Key;
                    var wrapper = kvp.Value;
                    if (!IsWrapperAlive(_clock.UtcNow, wrapper) || disposing)
                    {
                        ReleaseResource(key, wrapper);
                    }
                }
            }
        }

        private ResourceWrapperV2<TObject> CreateWrapper(TKey key)
        {
            var lazy = new AsyncLazy<TObject>(async () =>
            {
                Counter[ResourcePoolV2Counters.ResourceInitializationAttempts].Increment();

                try
                {
                    var result = await CreateInstanceAsync(key);
                    Counter[ResourcePoolV2Counters.ResourceInitializationSuccesses].Increment();
                    return result;
                }
                catch
                {
                    Counter[ResourcePoolV2Counters.ResourceInitializationFailures].Increment();
                    throw;
                }
            });
            var wrapper = new ResourceWrapperV2<TObject>(lazy, _clock.UtcNow, _disposeCancellationTokenSource.Token);
            Counter[ResourcePoolV2Counters.CreatedResources].Increment();
            return wrapper;
        }

        /// <summary>
        /// Called when a new instance needs to be created. May be overriden by inheritors when startup may be more complex.
        /// </summary>
        protected virtual async Task<TObject> CreateInstanceAsync(TKey key)
        {
            var instance = _factory(key);
            await instance.StartupAsync(_context).ThrowIfFailureAsync();
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
