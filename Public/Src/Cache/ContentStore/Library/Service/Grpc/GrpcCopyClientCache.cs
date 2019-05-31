// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Cache for <see cref="GrpcCopyClient"/>.
    /// </summary>
    public sealed class GrpcCopyClientCache
    {
        // Link to DistributedContentStoreSettings.MaxConcurrentCopyOperations
        internal readonly ConcurrentDictionary<GrpcCopyClientKey, GrpcCopyClient> _clientDict;

        private readonly object _backgroundCleanupLock = new object();
        private readonly int _maxClientCount;
        private readonly int _maximumAgeInMinutes;
        private readonly int _waitBetweenCleanupInMinutes;
        private Context _context;
        private Task _backgroundCleaningTask;
        private CancellationTokenSource _backgroundCleaningTaskTokenSource;

        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxClientCount">Maximum number of clients to cache.</param>
        /// <param name="maxClientAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="waitBetweenCleanupMinutes">Minutes to wait between cache purges.</param>
        public GrpcCopyClientCache(Context context, int maxClientCount = 512, int maxClientAgeMinutes = 55, int waitBetweenCleanupMinutes = 17)
        {
            _context = context;
            _maximumAgeInMinutes = maxClientAgeMinutes;
            _waitBetweenCleanupInMinutes = waitBetweenCleanupMinutes;
            _maxClientCount = maxClientCount;

            _clientDict = new ConcurrentDictionary<GrpcCopyClientKey, GrpcCopyClient>(Environment.ProcessorCount, _maxClientCount);
            StartBackgroundCleanup();
        }

        internal void StartBackgroundCleanup()
        {
            lock (_backgroundCleanupLock)
            {
                if (_backgroundCleaningTask != null)
                {
                    _backgroundCleaningTaskTokenSource.Cancel();
                    try
                    {
                        _backgroundCleaningTask.Wait();
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch (AggregateException) { }
#pragma warning restore ERP022  // Unobserved exception in generic exception handler
                }

                _backgroundCleaningTaskTokenSource = new CancellationTokenSource();
                _backgroundCleaningTask = Task.Run(() => BackgroundCleanupAsync());
            }
        }

        private async Task BackgroundCleanupAsync()
        {
            var ct = _backgroundCleaningTaskTokenSource.Token;

            while (!ct.IsCancellationRequested)
            {
                var earliestLastUseTime = DateTime.UtcNow - TimeSpan.FromMinutes(_maximumAgeInMinutes);

                var shutdownTasks = new List<Task<BoolResult>>();
                foreach (var kvp in _clientDict)
                {
                    var client = kvp.Value;
                    if (client._uses == 0 && client._lastUseTime < earliestLastUseTime)
                    {
                        lock (_backgroundCleanupLock)
                        {
                            if (client._uses == 0
                                && client._lastUseTime < earliestLastUseTime
                                && _clientDict.TryRemove(client.Key, out GrpcCopyClient removedClient))
                            {
                                // Cannot await within a lock
                                shutdownTasks.Add(client.ShutdownAsync(_context));
                            }
                        }
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

                await Task.Delay(TimeSpan.FromMinutes(_waitBetweenCleanupInMinutes), ct);
            }
        }

        /// <summary>
        /// Use an existing GRPC client if possible, else create a new one.
        /// </summary>
        /// <param name="host">Name of the host for the server (e.g. 'localhost').</param>
        /// <param name="grpcPort">GRPC port on the server.</param>
        /// <param name="useCompression">Whether or not GZip is enabled for copies.</param>
        public GrpcCopyClient Create(string host, int grpcPort, bool useCompression = false)
        {
            var key = new GrpcCopyClientKey(host, grpcPort, useCompression);
            if (_clientDict.TryGetValue(key, out GrpcCopyClient existingClient))
            {
                Interlocked.Increment(ref existingClient._uses);
                existingClient._lastUseTime = DateTime.UtcNow;
                return existingClient;
            }
            else if (_clientDict.Count > _maxClientCount)
            {
                throw new CacheException($"Attempting to create {nameof(GrpcCopyClient)} to increase cached count above maximum allowed ({_maxClientCount})");
            }
            else
            {
                var foundClient = _clientDict.GetOrAdd(key, k => {
                    var newClient = new GrpcCopyClient(k);
                    newClient._lastUseTime = DateTime.UtcNow;
                    return newClient;
                });
                Interlocked.Increment(ref foundClient._uses);
                return foundClient;
            }
        }
    }
}
