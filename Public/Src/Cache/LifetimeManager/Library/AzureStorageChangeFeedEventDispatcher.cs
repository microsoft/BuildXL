﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs.ChangeFeed;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Core.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// <see cref="BlobChangeFeedEvent"/> can't be extended or instantiated, so we have to create interfaces around it for testing. 
    /// </summary>
    internal interface IBlobChangeFeedEvent
    {
        DateTimeOffset EventTime { get; }
        BlobChangeFeedEventType EventType { get; }
        string Subject { get; }
        long ContentLength { get; }
    }

    /// <summary>
    /// For the sake of testing, and because the Azure emulator does not support the change feed, this interface encapsulates the operations
    /// performed with <see cref="BlobChangeFeedClient"/>
    /// </summary>
    internal interface IChangeFeedClient
    {
        IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(string? continuationToken, int? pageSizeHint);

        IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(DateTime? startTimeUtc, int? pageSizeHint);
    }

    /// <summary>
    /// Reads events from the Azure Storage change feed for each account in the cache and dispatches them to the database updater. This ensures that
    /// our view of the remote is accurate.
    /// </summary>
    public class AzureStorageChangeFeedEventDispatcher
    {
        private static readonly Tracer Tracer = new(nameof(AzureStorageChangeFeedEventDispatcher));

        private readonly IBlobCacheAccountSecretsProvider _secretsProvider;
        private readonly IReadOnlyList<BlobCacheStorageAccountName> _accounts;
        private readonly LifetimeDatabaseUpdater _updater;
        private readonly RocksDbLifetimeDatabase _db;
        private readonly IClock _clock;
        private readonly CheckpointManager _checkpointManager;
        private readonly int? _changeFeedPageSize;
        private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, BuildCacheShard>? _buildCacheShardMapping;

        private readonly string _metadataMatrix;
        private readonly string _contentMatrix;

        public AzureStorageChangeFeedEventDispatcher(
            IBlobCacheAccountSecretsProvider secretsProvider,
            IReadOnlyList<BlobCacheStorageAccountName> accounts,
            LifetimeDatabaseUpdater updater,
            CheckpointManager checkpointManager,
            RocksDbLifetimeDatabase db,
            IClock clock,
            string metadataMatrix,
            string contentMatrix,
            int? changeFeedPageSize,
            BuildCacheConfiguration? buildCacheConfiguration)
        {
            _secretsProvider = secretsProvider;
            _checkpointManager = checkpointManager;
            _accounts = accounts;
            _updater = updater;
            _db = db;
            _clock = clock;

            _metadataMatrix = metadataMatrix;
            _contentMatrix = contentMatrix;
            _changeFeedPageSize = changeFeedPageSize;
            _buildCacheShardMapping = buildCacheConfiguration?.Shards.ToDictionary(shard => shard.GetAccountName(), shard => shard);
        }

        public Task<BoolResult> ConsumeNewChangesAsync(OperationContext context, TimeSpan checkpointCreationInterval)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var now = _clock.UtcNow;

                    using var cts = new CancellationTokenSource();

                    var acquirer = new LockSetAcquirer<BoolResult>(lockCount: _accounts.Count);

                    using var checkpointTimer = new IntervalTimer(
                        () => PauseConsumeChangesAndCreateCheckpointAsync(context, acquirer),
                        checkpointCreationInterval,
                        dueTime: checkpointCreationInterval);

                    // It should be OK to do this unbounded, since we never expect a number of accounts big enough to overwhelm the system with tasks.
                    var tasks = _accounts.Select((accountName, i) =>
                    {
                        return Task.Run(async () =>
                        {
                            var creds = await _secretsProvider.RetrieveAccountCredentialsAsync(context, accountName);
                            return await ConsumeAccountChanges(context, now, cts, accountName, creds, acquirer.Locks[i]);
                        });
                    }).ToArray();

                    var results = await TaskUtilities.SafeWhenAll(tasks);

                    var aggregatedResult = BoolResult.Success;
                    foreach (var result in results)
                    {
                        if (!result.Succeeded)
                        {
                            aggregatedResult &= result;
                        }
                    }

                    return aggregatedResult;
                });
        }

        private Task<BoolResult> PauseConsumeChangesAndCreateCheckpointAsync(OperationContext context, LockSetAcquirer<BoolResult> acquirer)
        {
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return acquirer.UseAsync(
                        latestPages =>
                        {
                            if (latestPages.Any(r => r?.Succeeded == false))
                            {
                                var result = BoolResult.Success;
                                foreach (var r in latestPages)
                                {
                                    result &= (r ?? BoolResult.Success);
                                }

                                return Task.FromResult(new BoolResult(result, "One or more page failed to be processed. Checkpoint creation skipped."));
                            }

                            return _checkpointManager.CreateCheckpointAsync(
                                context,
                                new EventSequencePoint(_clock.UtcNow),
                                maxEventProcessingDelay: null);
                        });
                });
        }

        private async Task<BoolResult> ConsumeAccountChanges(
            OperationContext context,
            DateTime now,
            CancellationTokenSource cts,
            BlobCacheStorageAccountName accountName,
            IAzureStorageCredentials creds,
            Lock<BoolResult> consumePageLock)
        {
            OperationContext nestedContext = context.CreateNested("StorageAccountChangeFeed").WithCancellationToken(cts.Token);

            var changeFeedClient = CreateChangeFeedClient(creds);

            IAsyncEnumerable<Page<IBlobChangeFeedEvent>> pagesEnumerable;
            var continuationToken = _db.GetCursor(accountName.AccountName);

            if (continuationToken is null)
            {
                var creationDate = _db.GetCreationTime();

                Tracer.Debug(nestedContext, $"Starting enumeration of change feed for account=[{accountName.AccountName}] " +
                    $"with startTimeUtc=[{creationDate.ToString() ?? "null"}]");

                pagesEnumerable = changeFeedClient.GetChangesAsync(creationDate, _changeFeedPageSize);
            }
            else
            {
                Tracer.Debug(nestedContext, $"Starting enumeration of change feed for account=[{accountName.AccountName}] with cursor=[{continuationToken ?? "null"}]");
                pagesEnumerable = changeFeedClient.GetChangesAsync(continuationToken, _changeFeedPageSize);
            }

            var enumerator = pagesEnumerable.GetAsyncEnumerator();

            while (!nestedContext.Token.IsCancellationRequested)
            {
                bool doneProcessingItems = false;
                Result<BoolResult?> lockResult = await consumePageLock.UseAsync(async _ =>
                {
                    doneProcessingItems = true;

                    var hasMoreResult = await nestedContext.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            var hasMore = await enumerator.MoveNextAsync();
                            return new Result<bool>(hasMore);
                        },
                        caller: "GetChangeFeedPage",
                        traceOperationStarted: false,
                        extraEndMessage: hasMore => $"Account=[{accountName}] ContinuationToken=[{continuationToken}], HasMore=[{(hasMore.Succeeded ? hasMore.Value : null)}], NextContinuationToken=[{((hasMore.Succeeded && hasMore.Value) ? enumerator.Current.ContinuationToken : null)}]");

                    if (!hasMoreResult.Succeeded)
                    {
                        // We failed to download the next page.
                        return hasMoreResult;
                    }

                    if (!hasMoreResult.Value)
                    {
                        // We've finished consuming the change feed.
                        return BoolResult.Success;
                    }

                    var page = enumerator.Current;
                    var maxDateProcessed = await ProcessPageAsync(nestedContext, page, accountName, continuationToken);

                    if (nestedContext.Token.IsCancellationRequested)
                    {
                        return new BoolResult("Cancellation was requested");
                    }

                    if (!maxDateProcessed.Succeeded)
                    {
                        return maxDateProcessed;
                    }

                    continuationToken = page.ContinuationToken;

                    if (continuationToken is not null)
                    {
                        _db.SetCursor(accountName.AccountName, continuationToken);
                    }

                    if (maxDateProcessed.Value > now)
                    {
                        return BoolResult.Success;
                    }

                    doneProcessingItems = false;
                    return BoolResult.Success;
                });

                if (!lockResult.Succeeded || !lockResult.Value.Succeeded)
                {
                    // We've failed to process a page. This is unrecoverable. Cancel further page processing.
                    await cts.CancelTokenAsyncIfSupported();
                    return lockResult.Succeeded ? lockResult.Value : lockResult;
                }

                if (doneProcessingItems)
                {
                    return lockResult;
                }
            }

            return nestedContext.Token.IsCancellationRequested
                ? new BoolResult("Cancellation was requested")
                : BoolResult.Success;
        }

        internal virtual IChangeFeedClient CreateChangeFeedClient(IAzureStorageCredentials creds)
        {
            return new AzureChangeFeedClientWrapper(creds.CreateBlobChangeFeedClient());
        }

        private Task<Result<DateTime?>> ProcessPageAsync(
            OperationContext context,
            Page<IBlobChangeFeedEvent> page,
            BlobCacheStorageAccountName accountName,
            string? continuationToken)
        {
            DateTime? minimumEventTimestamp = null;
            DateTime? maximumEventTimestamp = null;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // REMARK: this function doesn't deal with cancellation because pages shouldn't take significantly
                    // long to process, and ultimately most of what's happening in the parallel operations isn't really
                    // cancellable. What can be cancelled will get cancelled, but the rest will just finish.

                    var processingTasks = new List<Task>(capacity: page.Values.Count);
                    foreach (var change in page.Values)
                    {
                        if (change is null)
                        {
                            // Not sure why this would be null, but the SDK makes it an option.
                            Tracer.Error(context, $"Found null change. Skipping. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}]");
                            continue;
                        }

                        minimumEventTimestamp = (minimumEventTimestamp is not null && minimumEventTimestamp < change.EventTime.UtcDateTime) ? minimumEventTimestamp : change.EventTime.UtcDateTime;
                        maximumEventTimestamp = (maximumEventTimestamp is null || maximumEventTimestamp < change.EventTime.UtcDateTime) ? change.EventTime.UtcDateTime : maximumEventTimestamp;

                        // For new we'll ignore everything except blob creations. We'll assume that this GC service is the only thing deleting blobs.
                        if (change.EventType != BlobChangeFeedEventType.BlobCreated)
                        {
                            continue;
                        }

                        processingTasks.Add(ProcessBlobCreatedEventAsync(context, accountName, change));
                    }

                    // This will block depending on the parallelism of the processing tasks inside the
                    // LifetimeDatabaseUpdater. That's configurable by outside sources. This means that there's three
                    // sources that bound the parallelism of processing:
                    //  1. The number of shards (accounts) that we have in the given instance. There's going to be one
                    //     page being processed per shard (i.e., one call to this method).
                    //  2. The number of entries in a change feed page. This is bounded by the Azure Storage service,
                    //     and it's the parallelism we're achieving below.
                    //  3. The amount of fingerprints that we're willing to process in parallel in the
                    //     LifetimeDatabaseUpdater. This is configurable by the user.
                    await Task.WhenAll(processingTasks);

                    return new Result<DateTime?>(maximumEventTimestamp, isNullAllowed: true);
                },
                traceOperationStarted: false,
                traceOperationFinished: true,
                extraEndMessage: r =>
                {
                    var output =
                        $"Account=[{accountName}] " +
                        $"ContinuationToken=[{continuationToken ?? "null"}] " +
                        $"PageSize=[{page.Values.Count}] " +
                        $"MinimumEventTimestamp=[{minimumEventTimestamp?.ToString("O") ?? "null"}] " +
                        $"MaximumEventTimestamp=[{maximumEventTimestamp?.ToString("O") ?? "null"}]";

                    return output;
                });
        }

        private async Task ProcessBlobCreatedEventAsync(OperationContext context, BlobCacheStorageAccountName accountName, IBlobChangeFeedEvent change)
        {
            // We yield on purpose here because most codepaths in this method are sync. We want to avoid blocking the caller.
            await Task.Yield();

            AbsoluteBlobPath blobPath;
            try
            {
                blobPath = AbsoluteBlobPath.ParseFromChangeEventSubject(_buildCacheShardMapping, accountName, change.Subject);
            }
            catch (Exception e)
            {
                Tracer.Debug(context, e, $"Failed to parse blob path from subject {change.Subject}.");
                return;
            }

            var namespaceId = new BlobNamespaceId(blobPath.Container.Universe, blobPath.Container.Namespace);

            switch (blobPath.Container.Purpose)
            {
                case BlobCacheContainerPurpose.Content:
                {
                    // If resharding happened, we don't want to process events for the other shard configuration.
                    if (!blobPath.Container.Matrix.Equals(_contentMatrix, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _updater.ContentCreated(context, namespaceId, blobPath.Path.Path, change.ContentLength);

                    _db.SetNamespaceLastAccessTime(namespaceId, blobPath.Container.Matrix, change.EventTime.UtcDateTime);
                    break;
                }
                case BlobCacheContainerPurpose.Metadata:
                {
                    // If resharding happened, we don't want to process events for the other shard configuration.
                    if (!blobPath.Container.Matrix.Equals(_metadataMatrix, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    // Failing to process a content hash list is a fatal error, so we must abort further processing.
                    await _updater
                        .ContentHashListCreatedAsync(context, namespaceId, blobPath.Path.Path, change.ContentLength)
                        .ThrowIfFailureAsync();


                    _db.SetNamespaceLastAccessTime(namespaceId, blobPath.Container.Matrix, change.EventTime.UtcDateTime);
                    break;
                }
                case BlobCacheContainerPurpose.Checkpoint:
                {
                    // Avoiding on purpose. The checkpoint container are used only for internal purposes and aren't
                    // meant to be tracked.
                    break;
                }
                default:
                    throw new NotSupportedException($"{blobPath.Container.Purpose} is not a supported purpose");
            }
        }

        /// <summary>
        /// Wrapper around <see cref="BlobChangeFeedClient"/> to be able to use our own defined interfaces.
        /// </summary>
        private class AzureChangeFeedClientWrapper : IChangeFeedClient
        {
            private readonly BlobChangeFeedClient _client;

            public AzureChangeFeedClientWrapper(BlobChangeFeedClient client) => _client = client;

            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(string? continuationToken, int? pageSizeHint)
            {
                var enunmerator = _client.GetChangesAsync(continuationToken).AsPages(pageSizeHint: pageSizeHint).GetAsyncEnumerator();
                while (await enunmerator.MoveNextAsync())
                {
                    var page = enunmerator.Current;
                    var changes = page.Values.Select(c => new BlobChangeFeedEventWrapper(c)).ToArray();
                    var newPage = Page<IBlobChangeFeedEvent>.FromValues(changes, page.ContinuationToken, page.GetRawResponse());
                    yield return newPage;
                }
            }

            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(DateTime? startTimeUtc, int? pageSizeHint)
            {
                var enunmerator = _client.GetChangesAsync(start: startTimeUtc).AsPages(pageSizeHint: pageSizeHint).GetAsyncEnumerator();
                while (await enunmerator.MoveNextAsync())
                {
                    var page = enunmerator.Current;
                    var changes = page.Values.Select(c => new BlobChangeFeedEventWrapper(c)).ToArray();
                    var newPage = Page<IBlobChangeFeedEvent>.FromValues(changes, page.ContinuationToken, page.GetRawResponse());
                    yield return newPage;
                }
            }
        }

        /// <summary>
        /// Wrapper around <see cref="BlobChangeFeedEvent"/> to be able to use our own defined interfaces.
        /// </summary>
        internal class BlobChangeFeedEventWrapper : IBlobChangeFeedEvent
        {
            private readonly BlobChangeFeedEvent _inner;

            public BlobChangeFeedEventWrapper(BlobChangeFeedEvent inner) => _inner = inner;

            public DateTimeOffset EventTime => _inner.EventTime;
            public BlobChangeFeedEventType EventType => _inner.EventType;
            public string Subject => _inner.Subject;
            public long ContentLength => _inner.EventData.ContentLength;
        }
    }
}
