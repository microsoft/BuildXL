// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
        where TKey: notnull
        where TObject : IStartupShutdownSlim
    {
        private readonly int _maxResourceCount;
        private readonly int _maximumAgeInMinutes;
        private readonly Context _context;
        private readonly Dictionary<TKey, ResourceWrapper<TObject>> _resourceDict;
        private readonly Func<TKey, TObject> _resourceFactory;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1);
        private readonly IClock _clock;

        private readonly Tracer _tracer = new Tracer(nameof(ResourcePool<TKey, TObject>));
        private bool _disposed;

        internal CounterCollection<ResourcePoolCounters> Counter { get; } = new CounterCollection<ResourcePoolCounters>();

        /// <summary>
        /// Cache of objects.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxResourceCount">Maximum number of clients to cache.</param>
        /// <param name="maxAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="resourceFactory">Constructor for a new resource.</param>
        /// <param name="clock">Clock to use for TTL</param>
        public ResourcePool(Context context, int maxResourceCount, int maxAgeMinutes, Func<TKey, TObject> resourceFactory, IClock? clock = null)
        {
            _context = context;
            _maxResourceCount = maxResourceCount;
            _maximumAgeInMinutes = maxAgeMinutes;
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
                    // Remove anything that has expired.
                    await CleanupAsync(force: false, numberToRelease: int.MaxValue);

                    ResourceWrapper<TObject> returnWrapper;

                    // Attempt to reuse an existing resource if it has been instantiated.
                    if (_resourceDict.TryGetValue(key, out var existingWrappedResource))
                    {
                        returnWrapper = existingWrappedResource;
                    }
                    else
                    {
                        // Start resource "GC" if the cache is full
                        if (_resourceDict.Count >= _maxResourceCount)
                        {
                            // Attempt to remove whatever resource was used last.
                            await CleanupAsync(force: true, numberToRelease: 1);

                            if (_resourceDict.Count >= _maxResourceCount)
                            {
                                throw Contract.AssertFailure($"Failed to make space for new resource. Count={_resourceDict.Count}, Max={_maxResourceCount}");
                            }
                        }

                        returnWrapper = new ResourceWrapper<TObject>(() => _resourceFactory(key), _context);
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

        /// <summary>
        /// Free resources which are no longer in use and older than <see cref="_maximumAgeInMinutes"/> minutes.
        /// </summary>
        /// <param name="force">Whether last use time should be ignored.</param>
        /// <param name="numberToRelease">Max amount of resources you want to release.</param>
        private async Task CleanupAsync(bool force, int numberToRelease)
        {
            var earliestLastUseTime = _clock.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);
            var shutdownTasks = new List<Task<BoolResult>>();

            using (var sw = Counter[ResourcePoolCounters.Cleanup].Start())
            {
                var amountRemoved = 0;
                var initialCount = _resourceDict.Count;

                foreach (var kvp in _resourceDict.OrderBy(kvp => kvp.Value.LastUseTime))
                {
                    if (amountRemoved >= numberToRelease)
                    {
                        break;
                    }

                    if (!force && kvp.Value.LastUseTime > earliestLastUseTime)
                    {
                        break;
                    }

                    var resourceValue = kvp.Value.Resource.Value;

                    // If the resource is approved for shutdown, queue it to shutdown.
                    if (kvp.Value.TryMarkForShutdown(force, earliestLastUseTime))
                    {
                        _resourceDict.Remove(kvp.Key);

                        // Shutting down all the resources in parallel
                        shutdownTasks.Add(resourceValue.ShutdownAsync(_context));
                        amountRemoved++;
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

                if (force && amountRemoved < numberToRelease)
                {
                    throw Contract.AssertFailure($"Failed to force-clean. Cleaned {amountRemoved} of the {numberToRelease} requested");
                }
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

                var shutdownTasks = _resourceDict.Select(resourceKvp => resourceKvp.Value.Value.ShutdownAsync(_context)).ToArray();
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
