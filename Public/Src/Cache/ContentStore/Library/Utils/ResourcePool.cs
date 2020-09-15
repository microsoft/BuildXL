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
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Reference-counting and TTL-observing pool.
    /// </summary>
    /// <typeparam name="TKey">Identifier for a given resource.</typeparam>
    /// <typeparam name="TObject">Type of the pooled object.</typeparam>
    public class ResourcePool<TKey, TObject> : IDisposable
        where TKey : notnull
        where TObject : IStartupShutdownSlim
    {
        private readonly Context _context;
        private readonly Func<TKey, TObject> _resourceFactory;

        private readonly int _maxResourceCount;
        private readonly int _maximumAgeInMinutes;
        private readonly bool _enableInstanceInvalidation;
        private readonly Dictionary<TKey, ResourceWrapper<TObject>> _resourceDict;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1);
        private readonly IClock _clock;

        private readonly Tracer _tracer = new Tracer(nameof(ResourcePool<TKey, TObject>));
        private bool _disposed;

        internal CounterCollection<ResourcePoolCounters> Counter { get; } = new CounterCollection<ResourcePoolCounters>();

        private readonly ConcurrentQueue<ResourceWrapper<TObject>> _shutdownQueue = new ConcurrentQueue<ResourceWrapper<TObject>>();

        /// <summary>
        /// Cache of objects.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxResourceCount">Maximum number of clients to cache.</param>
        /// <param name="maxAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="resourceFactory">Constructor for a new resource.</param>
        /// <param name="clock">Clock to use for TTL</param>
        /// <param name="enableInstanceInvalidation">Allow callers to invalidate instances</param>
        public ResourcePool(Context context, int maxResourceCount, int maxAgeMinutes, Func<TKey, TObject> resourceFactory, IClock? clock = null, bool enableInstanceInvalidation = false)
        {
            _context = context;
            _maxResourceCount = maxResourceCount;
            _maximumAgeInMinutes = maxAgeMinutes;
            _enableInstanceInvalidation = enableInstanceInvalidation;

            _clock = clock ?? SystemClock.Instance;

            _resourceDict = new Dictionary<TKey, ResourceWrapper<TObject>>(_maxResourceCount);
            _resourceFactory = resourceFactory;
        }

        /// <summary>
        /// Use an existing resource if possible, else create a new one.
        /// </summary>
        /// <param name="key">Key to lookup an existing resource, if one exists.</param>
        /// <exception cref="ObjectDisposedException">If the pool has already been disposed</exception>
        public async Task<ResourceWrapper<TObject>> CreateAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(objectName: _tracer.Name, message: "Attempt to obtain resource after dispose");
            }

            using (Counter[ResourcePoolCounters.CreationTime].Start())
            {
                // NOTE: if dispose has happened at this point, we will fail to take the semaphore
                using (await _semaphore.WaitTokenAsync())
                {
                    // Remove anything that has expired or been invalidated.
                    await CleanupAsync();

                    ResourceWrapper<TObject> returnWrapper;

                    // Attempt to reuse an existing resource if it has been instantiated.
                    if (_resourceDict.TryGetValue(key, out var existingWrappedResource))
                    {
                        returnWrapper = existingWrappedResource;
                    }
                    else
                    {
                        returnWrapper = CreateResourceWrapper(key);
                        _resourceDict.Add(key, returnWrapper);
                    }

                    if (!returnWrapper.TryAcquire(out var reused, _clock))
                    {
                        // Should be impossible
                        throw Contract.AssertFailure($"Failed to acquire resource. LastUseTime={returnWrapper.LastUseTime}, Uses={returnWrapper.Uses}");
                    }

                    Counter[reused ? ResourcePoolCounters.Reused : ResourcePoolCounters.Created].Increment();

                    return returnWrapper;
                }
            }
        }

        /// <nodoc />
        protected ResourceWrapper<TObject> CreateResourceWrapper(TKey key, bool shutdownOnDispose = false)
        {
            return new ResourceWrapper<TObject>(() => _resourceFactory(key), _context, shutdownOnDispose);
        }

        /// <summary>
        /// Free resources which are no longer in use and older than <see cref="_maximumAgeInMinutes"/> minutes.
        /// </summary>
        private async Task CleanupAsync()
        {
            var earliestLastUseTime = _clock.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);
            var shutdownTasks = new List<Task<BoolResult>>();

            using (var sw = Counter[ResourcePoolCounters.Cleanup].Start())
            {
                var initialCount = _resourceDict.Count;

                // First remove everything that's either expired or invalid
                foreach (var kvp in _resourceDict.ToList())
                {
                    if (kvp.Value.LastUseTime > earliestLastUseTime && (!_enableInstanceInvalidation || !kvp.Value.Invalid))
                    {
                        // If the resource is within its lifetime, and it's not invalid (when invalidation is enabled),
                        // we can skip it.
                        continue;
                    }

                    _resourceDict.Remove(kvp.Key);
                    _shutdownQueue.Enqueue(kvp.Value);
                }

                // Now prune until we are within quota
                var resourceRemovalTarget = _resourceDict.Count - _maxResourceCount;
                if (resourceRemovalTarget > 0)
                {
                    foreach (var kvp in _resourceDict.OrderBy(kvp => kvp.Value.LastUseTime))
                    {
                        if (resourceRemovalTarget <= 0)
                        {
                            break;
                        }

                        _resourceDict.Remove(kvp.Key);
                        _shutdownQueue.Enqueue(kvp.Value);

                        resourceRemovalTarget--;
                    }
                }

                var maxShutdownAttempts = _shutdownQueue.Count;
                while (maxShutdownAttempts-- > 0 && _shutdownQueue.TryDequeue(out var instance))
                {
                    if (instance.TryMarkForShutdown(force: true, earliestLastUseTime))
                    {
                        if (!instance.IsValueCreated)
                        {
                            // We can avoid shutting down instances that aren't even created
                            Counter[ResourcePoolCounters.Cleaned].Increment();
                            continue;
                        }

                        shutdownTasks.Add(instance.Value.ShutdownAsync(_context));
                    }
                    else
                    {
                        // We'll need to retry later
                        _shutdownQueue.Enqueue(instance);
                    }
                }

                var shutdownTasksArray = shutdownTasks.ToArray();
                if (shutdownTasksArray.Length == 0)
                {
                    // Early return to avoid extra async steps and unnecessary traces that we "cleaned up 0" resources.
                    return;
                }

                await ShutdownGrpcClientsAsync(shutdownTasksArray);

                Counter[ResourcePoolCounters.Cleaned].Add(shutdownTasks.Count);

                _tracer.Debug(_context, $"Cleaned {shutdownTasks.Count} of {initialCount} in {sw.Elapsed.TotalMilliseconds}ms");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            using (_semaphore.WaitToken())
            {
                _disposed = true;

                var queuedShutdownTasks = _shutdownQueue.Select(instance => instance.Value.ShutdownAsync(_context));
                var shutdownTasks = _resourceDict.Select(resourceKvp => resourceKvp.Value.Value.ShutdownAsync(_context)).Concat(queuedShutdownTasks).ToArray();
                ShutdownGrpcClientsAsync(shutdownTasks).GetAwaiter().GetResult();
            }

            _tracer.Debug(_context, string.Join(Environment.NewLine, Counter.AsStatistics(nameof(ResourcePool<TKey, TObject>)).Select(kvp => $"{kvp.Key} : {kvp.Value}")));

            _semaphore.Dispose();
        }

        private async Task ShutdownGrpcClientsAsync(Task<BoolResult>[] shutdownTasks)
        {
            var allTasks = Task.WhenAll(shutdownTasks);
            try
            {
                // If the shutdown failed with unsuccessful BoolResult, then the result is already traced. No need to any extra steps.
                await allTasks;
            }
            catch (Exception e)
            {
                new ErrorResult(e).IgnoreFailure();
                // If Task.WhenAll throws in an await, it unwraps the AggregateException and only
                // throws the first inner exception. We want to see all failed shutdowns.
                _tracer.Error(_context, $"Shutdown of unused resource failed after removal from resource cache. {allTasks.Exception}");
            }
        }
    }
}
