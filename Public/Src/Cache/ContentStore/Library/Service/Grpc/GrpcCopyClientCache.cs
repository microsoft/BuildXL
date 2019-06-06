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
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Cache for <see cref="GrpcCopyClient"/>.
    /// </summary>
    public sealed class GrpcCopyClientCache : IDisposable
    {
        private readonly int _maxClientCount;
        private readonly int _maximumAgeInMinutes;
        private readonly int _waitBetweenCleanupInMinutes;
        private readonly Task _backgroundCleaningTask;
        private readonly CancellationTokenSource _backgroundCleaningTaskTokenSource;
        private readonly Context _context;

        private readonly object _pendingCleanupTaskLock = new object();
        private Task _pendingCleanupTask = BoolResult.SuccessTask;

        private readonly ConcurrentDictionary<GrpcCopyClientKey, Lazy<GrpcCopyClient>> _clientDict;

        private CounterCollection<GrpcCopyClientCacheCounters> Counter { get; } = new CounterCollection<GrpcCopyClientCacheCounters>();

        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxClientCount">Maximum number of clients to cache.</param>
        /// <param name="maxClientAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="waitBetweenCleanupMinutes">Minutes to wait between cache purges.</param>
        public GrpcCopyClientCache(Context context, int maxClientCount = 512, int maxClientAgeMinutes = 55, int waitBetweenCleanupMinutes = 17)
        {
            // Creating nested context to trace all the messages from this class in a separate "tracing thread".
            _context = new Context(context);
            _maximumAgeInMinutes = maxClientAgeMinutes;
            _waitBetweenCleanupInMinutes = waitBetweenCleanupMinutes;
            _maxClientCount = maxClientCount;

            _clientDict = new ConcurrentDictionary<GrpcCopyClientKey, Lazy<GrpcCopyClient>>(Environment.ProcessorCount, _maxClientCount);

            _backgroundCleaningTaskTokenSource = new CancellationTokenSource();
            _backgroundCleaningTask = Task.Run(() => BackgroundCleanupAsync());
        }

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

        internal async Task CleanupAsync(bool force = false)
        {
            var earliestLastUseTime = DateTime.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);
            var shutdownTasks = new List<Task<BoolResult>>();

            using (var sw = Counter[GrpcCopyClientCacheCounters.Cleanup].Start())
            {
                foreach (var kvp in _clientDict)
                {
                    if (!kvp.Value.IsValueCreated)
                    {
                        continue;
                    }

                    var client = kvp.Value.Value;
                    if (client.TryMarkForShutdown(force, earliestLastUseTime))
                    {
                        bool removed = _clientDict.TryRemove(kvp.Key, out _);
                        Contract.Assert(removed, $"Unable to remove client {kvp.Key} which was marked for shutdown.");
                        // Cannot await within a lock
                        shutdownTasks.Add(client.ShutdownAsync(_context));
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
                    _context.Error($"Shutdown of unused GRPC clients failed after removal from client cache. {allTasks.Exception}");
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                Counter[GrpcCopyClientCacheCounters.ClientsCleaned].Add(shutdownTasks.Count);

                _context.Debug($"Cleaned {shutdownTasks.Count} of {_clientDict.Count} in {sw.Elapsed.TotalMilliseconds}ms");
            }
        }

        private async Task EnsureCapacityAsync()
        {
            if (_clientDict.Count >= _maxClientCount)
            {
                await RunOnceAsync(ref _pendingCleanupTask, _pendingCleanupTaskLock, () => CleanupAsync(force: true));
            }
        }

        /// <summary>
        /// Assuming <paramref name="pendingTask"/> is non-null, will either return an in-progress <paramref name="pendingTask"/> or start a new task from <paramref name="runAsync"/> 
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
        /// Use an existing GRPC client if possible, else create a new one.
        /// </summary>
        /// <param name="host">Name of the host for the server (e.g. 'localhost').</param>
        /// <param name="grpcPort">GRPC port on the server.</param>
        /// <param name="useCompression">Whether or not GZip is enabled for copies.</param>
        public async Task<GrpcCopyClient> CreateAsync(string host, int grpcPort, bool useCompression = false)
        {
            GrpcCopyClient returnClient;
            using (var sw = Counter[GrpcCopyClientCacheCounters.ClientCreationTime].Start())
            {

                var key = new GrpcCopyClientKey(host, grpcPort, useCompression);
                if (_clientDict.TryGetValue(key, out Lazy<GrpcCopyClient> existingClientLazy) && existingClientLazy.IsValueCreated && existingClientLazy.Value.TryAcquire(out var reused))
                {
                    returnClient = existingClientLazy.Value;
                }
                else
                {
                    await EnsureCapacityAsync();

                    var clientLazy = _clientDict.GetOrAdd(key, k =>
                    {
                        return new Lazy<GrpcCopyClient>(() => new GrpcCopyClient(k));
                    });

                    if (_clientDict.Count >= _maxClientCount)
                    {
                        _clientDict.TryRemove(key, out _);
                        throw new CacheException($"Attempting to create {nameof(GrpcCopyClient)} to increase cached count above maximum allowed ({_maxClientCount})");
                    }

                    returnClient = clientLazy.Value;

                    if (!returnClient.TryAcquire(out reused))
                    {
                        throw Contract.AssertFailure($"GRPC client was marked for shutdown before acquired.");
                    }
                }

                GrpcCopyClientCacheCounters counter = reused ? GrpcCopyClientCacheCounters.ClientsReused : GrpcCopyClientCacheCounters.ClientsCreated;
                Counter[counter].Increment();

                _context.Debug($"{nameof(GrpcCopyClientCache)}.{nameof(CreateAsync)} {(reused ? "reused" : "created")} a client with {returnClient.Uses} from a pool of {_clientDict.Count}");
            }

            return returnClient;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _context.Debug(string.Join(Environment.NewLine, Counter.AsStatistics(nameof(GrpcCopyClientCache)).Select(kvp => $"{kvp.Key} : {kvp.Value}")));

            // Trace the disposal: both start of it and the result.
            _backgroundCleaningTaskTokenSource.Cancel();

            var taskList = new List<Task>();
            foreach (var clientKvp in _clientDict)
            {
                // Check boolresult.
                taskList.Add(clientKvp.Value.Value.ShutdownAsync(_context));
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
                _context.Error($"Shutdown of unused GRPC clients failed after removal from client cache. {allTasks.Exception}");
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }
    }
}
