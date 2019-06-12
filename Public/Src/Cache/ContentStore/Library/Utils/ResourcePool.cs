// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    public class ResourcePool<TKey, TObject> : IDisposable where TObject : IShutdown<BoolResult>
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
        /// Call <see cref="EnsureCapacityAsync"/> in a background delayed loop.
        /// </summary>
        private async Task BackgroundCleanupAsync()
        {
            var ct = _backgroundCleaningTaskTokenSource.Token;

            while (!ct.IsCancellationRequested)
            {
                await EnsureCapacityAsync();

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_waitBetweenCleanupInMinutes), ct);
                }
                catch (TaskCanceledException) { }
            }
        }

        private async Task EnsureCapacityAsync()
        {
            if (_resourceDict.Count >= _maxResourceCount)
            {
                await RunOnceAsync(ref _pendingCleanupTask, _pendingCleanupTaskLock, () => CleanupAsync(force: true));
            }
        }

        /// <summary>
        /// Assuming <paramref name="pendingTask"/> is a non-null Task, will either return the incomplete <paramref name="pendingTask"/> or start a new task constructed by <paramref name="runAsync"/>.
        /// </summary>
        private static Task RunOnceAsync(ref Task pendingTask, object lockHandle, Func<Task> runAsync)
        {
            if (pendingTask.IsCompleted)
            {
                lock (lockHandle)
                {
                    if (pendingTask.IsCompleted)
                    {
                        pendingTask = runAsync();
                    }
                }
            }

            return pendingTask;
        }

        /// <summary>
        /// Free resources which are no longer in use and older than <see cref="_maximumAgeInMinutes"/> minutes.
        /// </summary>
        /// <param name="force">Whether last use time should be ignored.</param>
        internal async Task CleanupAsync(bool force = false)
        {
            var earliestLastUseTime = DateTime.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);
            var shutdownTasks = new List<Task<BoolResult>>();

            using (var sw = Counter[ResourcePoolCounters.Cleanup].Start())
            {
                foreach (var kvp in _resourceDict)
                {
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
                        Contract.Assert(removed, $"Unable to remove resource with key {kvp.Key} which was marked for shutdown.");

                        // Cannot await within a lock
                        shutdownTasks.Add(resourceValue.ShutdownAsync(_context));
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
            using (var sw = Counter[ResourcePoolCounters.CreationTime].Start())
            {
                // Attempt to reuse an existing resource if it has been instantiated.
                if (_resourceDict.TryGetValue(key, out ResourceWrapper<TObject> existingWrappedResource) && existingWrappedResource.IsValueCreated && existingWrappedResource.TryAcquire(out var reused))
                {
                    returnWrapper = existingWrappedResource;
                }
                else
                {
                    // Start resource "GC" if the cache is full and it isn't already running
                    await EnsureCapacityAsync();

                    returnWrapper = _resourceDict.GetOrAdd(key, k =>
                    {
                        return new ResourceWrapper<TObject>(() => _resourceFactory(k));
                    });

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

                ResourcePoolCounters counter = reused ? ResourcePoolCounters.Reused : ResourcePoolCounters.Created;
                Counter[counter].Increment();

                _context.Debug($"{nameof(ResourcePool<TKey, TObject>)}.{nameof(CreateAsync)} {(reused ? "reused" : "created")} a resource with {returnWrapper.Uses} from a pool of {_resourceDict.Count}");
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
