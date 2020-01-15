// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Reference-counting and TTL-observing pool.
    /// </summary>
    /// <typeparam name="TKey">Identifier for a given resource.</typeparam>
    /// <typeparam name="TObject">Type of the pooled object.</typeparam>
    public class ResourcePool<TKey, TObject> : IDisposable where TObject : IStartupShutdownSlim
    {
        private readonly int _maxResourceCount;
        private readonly int _maximumAgeInMinutes;
        private readonly int _waitBetweenCleanupInMinutes;
        private readonly Task _backgroundCleaningTask;
        private readonly CancellationTokenSource _backgroundCleaningTaskTokenSource;
        private readonly Context _context;

        private readonly object _pendingCleanupTaskLock = new object();
        private Task _pendingCleanupTask = BoolResult.SuccessTask;

        private readonly ConcurrentDictionary<TKey, ResourceWrapper<TObject>> _resourceDict;
        private readonly Func<TKey, TObject> _resourceFactory;

        private int _resourceCount;

        internal CounterCollection<ResourcePoolCounters> Counter { get; } = new CounterCollection<ResourcePoolCounters>();

        /// <summary>
        /// Cache of objects.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxResourceCount">Maximum number of clients to cache.</param>
        /// <param name="maxAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="waitBetweenCleanupMinutes">Minutes to wait between cache purges.</param>
        /// <param name="resourceFactory">Constructor for a new resource.</param>
        public ResourcePool(Context context, int maxResourceCount, int maxAgeMinutes, int waitBetweenCleanupMinutes, Func<TKey, TObject> resourceFactory)
        {
            _context = context;
            _maxResourceCount = maxResourceCount;
            _maximumAgeInMinutes = maxAgeMinutes;
            _waitBetweenCleanupInMinutes = waitBetweenCleanupMinutes;

            _resourceDict = new ConcurrentDictionary<TKey, ResourceWrapper<TObject>>(Environment.ProcessorCount, _maxResourceCount);
            _resourceFactory = resourceFactory;

            _backgroundCleaningTaskTokenSource = new CancellationTokenSource();
            _backgroundCleaningTask = Task.Run(() => BackgroundCleanupAsync());
        }

        /// <summary>
        /// Call <see cref="EnqueueCleanupAsync"/> in a background delayed loop.
        /// </summary>
        private async Task BackgroundCleanupAsync()
        {
            var ct = _backgroundCleaningTaskTokenSource.Token;

            while (!ct.IsCancellationRequested)
            {
                await EnqueueCleanupAsync(force: false, numberToRelease: int.MaxValue);

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_waitBetweenCleanupInMinutes), ct);
                }
                catch (TaskCanceledException) { }
            }
        }

        private Task EnqueueCleanupAsync(bool force, int numberToRelease)
            => EnqueueTaskAsync(ref _pendingCleanupTask, _pendingCleanupTaskLock, () => CleanupAsync(force, numberToRelease));

        private static Task EnqueueTaskAsync(ref Task queueTail, object lockHandle, Func<Task> runAsync)
        {
            lock (lockHandle)
            {
                if (queueTail.IsCompleted)
                {
                    queueTail = runAsync();
                }
                else
                {
                    queueTail = queueTail.ContinueWith(_ => runAsync()).Unwrap();
                }

                return queueTail;
            }
        }

        /// <summary>
        /// Free resources which are no longer in use and older than <see cref="_maximumAgeInMinutes"/> minutes.
        /// </summary>
        /// <param name="force">Whether last use time should be ignored.</param>
        /// <param name="numberToRelease">Max amount of resources you want to release.</param>
        internal async Task CleanupAsync(bool force, int numberToRelease)
        {
            var earliestLastUseTime = DateTime.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);
            var shutdownTasks = new List<Task<BoolResult>>();

            using (var sw = Counter[ResourcePoolCounters.Cleanup].Start())
            {
                var amountRemoved = 0;

                // Important to call ToArray as there is a race condition in ConcurrentDictionary when calling OrderBy
                foreach (var kvp in _resourceDict.ToArray().OrderBy(kvp => kvp.Value._lastUseTime))
                {
                    if (amountRemoved >= numberToRelease)
                    {
                        break;
                    }

                    // Don't free resources which have not yet been instantiated. This avoids a race between
                    // construction of the lazy object and initialization.
                    if (!kvp.Value.IsValueCreated)
                    {
                        continue;
                    }

                    var resourceValue = kvp.Value._resource.Value;

                    // If the resource is approved for shutdown, queue it to shutdown.
                    if (kvp.Value.TryMarkForShutdown(force, earliestLastUseTime))
                    {
                        bool removed = _resourceDict.TryRemove(kvp.Key, out _);

                        // Cannot await within a lock
                        shutdownTasks.Add(resourceValue.ShutdownAsync(_context));

                        Interlocked.Decrement(ref _resourceCount);
                        amountRemoved++;
                    }
                }

                var allTasks = Task.WhenAll(shutdownTasks.ToArray());
                try
                {
                    await allTasks;
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch (Exception)
                {
                    // If Task.WhenAll throws in an await, it unwraps the AggregateException and only
                    // throws the first inner exception. We want to see all failed shutdowns.
                    _context.Error($"Shutdown of unused resource failed after removal from resource cache. {allTasks.Exception}");
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                Counter[ResourcePoolCounters.Cleaned].Add(shutdownTasks.Count);

                _context.Debug($"Cleaned {shutdownTasks.Count} of {_resourceDict.Count} in {sw.Elapsed.TotalMilliseconds}ms");
            }
        }

        /// <summary>
        /// Use an existing resource if possible, else create a new one.
        /// </summary>
        /// <param name="key">Key to lookup an exisiting resource, if one exists.</param>
        public async Task<ResourceWrapper<TObject>> CreateAsync(TKey key)
        {
            ResourceWrapper<TObject> returnWrapper;
            using (Counter[ResourcePoolCounters.CreationTime].Start())
            {
                // Attempt to reuse an existing resource if it has been instantiated.
                if (_resourceDict.TryGetValue(key, out ResourceWrapper<TObject> existingWrappedResource) && existingWrappedResource.IsValueCreated && existingWrappedResource.TryAcquire(out var reused))
                {
                    returnWrapper = existingWrappedResource;
                }
                else
                {
                    var count = Interlocked.Increment(ref _resourceCount);

                    // Start resource "GC" if the cache is full and it isn't already running
                    if (count >= _maxResourceCount)
                    {
                        await EnqueueCleanupAsync(force: true, numberToRelease: 1);
                    }

                    returnWrapper = _resourceDict.GetOrAdd(
                        key,
                        (k, resourceFactory) => new ResourceWrapper<TObject>(() => resourceFactory(k), _context),
                        _resourceFactory);

                    if (_resourceDict.Count > _maxResourceCount)
                    {
                        _resourceDict.TryRemove(key, out _);
                        throw new CacheException($"Attempting to create resource to increase cached count above maximum allowed ({_maxResourceCount})");
                    }

                    if (!returnWrapper.TryAcquire(out reused))
                    {
                        throw Contract.AssertFailure($"Resource was marked for shutdown before acquired.");
                    }
                }

                Counter[reused ? ResourcePoolCounters.Reused : ResourcePoolCounters.Created].Increment();
            }

            return returnWrapper;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _context.Debug(string.Join(Environment.NewLine, Counter.AsStatistics(nameof(ResourcePool<TKey, TObject>)).Select(kvp => $"{kvp.Key} : {kvp.Value}")));

            // Trace the disposal: both start of it and the result.
            _backgroundCleaningTaskTokenSource.Cancel();

            var taskList = new List<Task>();
            foreach (var resourceKvp in _resourceDict)
            {
                // Check boolresult.
                taskList.Add(resourceKvp.Value.Value.ShutdownAsync(_context));
            }

            var allTasks = Task.WhenAll(taskList.ToArray());
            try
            {
                allTasks.GetAwaiter().GetResult();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch (Exception)
            {
                // If Task.WhenAll throws in an await, it unwraps the AggregateException and only
                // throws the first inner exception. We want to see all failed shutdowns.
                _context.Error($"Shutdown of unused resources failed after removal from resource cache. {allTasks.Exception}");
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }
    }
}
