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
    public class ResourcePool<TKey, TObject> : IDisposable where TObject : IStartupShutdownSlim
    {
        private readonly int _maxResourceCount;
        private readonly int _maximumAgeInMinutes;
        private readonly Context _context;
        private readonly Dictionary<TKey, ResourceWrapper<TObject>> _resourceDict;
        private readonly Func<TKey, TObject> _resourceFactory;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1);
        private readonly IClock _clock;

        private readonly Tracer _tracer = new Tracer(nameof(ResourcePool<TKey, TObject>));

        internal CounterCollection<ResourcePoolCounters> Counter { get; } = new CounterCollection<ResourcePoolCounters>();

        /// <summary>
        /// Cache of objects.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxResourceCount">Maximum number of clients to cache.</param>
        /// <param name="maxAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="resourceFactory">Constructor for a new resource.</param>
        /// <param name="clock">Clock to use for TTL</param>
        public ResourcePool(Context context, int maxResourceCount, int maxAgeMinutes, Func<TKey, TObject> resourceFactory, IClock clock = null)
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
        /// <param name="key">Key to lookup an exisiting resource, if one exists.</param>
        public async Task<ResourceWrapper<TObject>> CreateAsync(TKey key)
        {
            using (Counter[ResourcePoolCounters.CreationTime].Start())
            {
                using (await _semaphore.WaitToken())
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

                        // Cannot await within a lock
                        shutdownTasks.Add(resourceValue.ShutdownAsync(_context));
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
                    _tracer.Error(_context, $"Shutdown of unused resource failed after removal from resource cache. {allTasks.Exception}");
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

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
            _tracer.Debug(_context, string.Join(Environment.NewLine, Counter.AsStatistics(nameof(ResourcePool<TKey, TObject>)).Select(kvp => $"{kvp.Key} : {kvp.Value}")));

            if (_semaphore.CurrentCount == 0)
            {
                throw new InvalidOperationException("No one should be holding the lock on Dispose");
            }

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
                _tracer.Error(_context, $"Shutdown of unused resources failed after removal from resource cache. {allTasks.Exception}");
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }
    }
}
