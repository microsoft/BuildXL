// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
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

        private async Task<BoolResult> ReadPagesFromStorageAsync(
            OperationContext context,
            BlobCacheStorageAccountName accountName,
            IAzureStorageCredentials credentials,
            string? continuationToken,
            Channel<Result<Page<IBlobChangeFeedEvent>?>> channel)
        {
            var changeFeedClient = CreateChangeFeedClient(credentials);
            IAsyncEnumerable<Page<IBlobChangeFeedEvent>> enumerable;
            if (continuationToken is null)
            {
                var creationDate = _db.GetCreationTime();

                Tracer.Debug(context, $"Starting enumeration of change feed for account=[{accountName.AccountName}] " +
                    $"with startTimeUtc=[{creationDate.ToString() ?? "null"}]");

                enumerable = changeFeedClient.GetChangesAsync(creationDate, _changeFeedPageSize);
            }
            else
            {
                Tracer.Debug(context, $"Starting enumeration of change feed for account=[{accountName.AccountName}] with cursor=[{continuationToken ?? "null"}]");
                enumerable = changeFeedClient.GetChangesAsync(continuationToken, _changeFeedPageSize);
            }

            await using var enumerator = enumerable.GetAsyncEnumerator(context.Token);
            try
            {
                while (!context.Token.IsCancellationRequested)
                {
                    var hasMoreResult = await context.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            var hasMore = await enumerator.MoveNextAsync();
                            Page<IBlobChangeFeedEvent>? page = null;
                            if (hasMore)
                            {
                                page = enumerator.Current;
                                continuationToken = page.ContinuationToken;
                            }

                            return new Result<Page<IBlobChangeFeedEvent>?>(page, isNullAllowed: true);
                        },
                        caller: "GetChangeFeedPage",
                        traceOperationStarted: false,
                        extraEndMessage: r => $"Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}], HasMore=[{(r.Succeeded ? r.Value : null)}], NextContinuationToken=[{((r.Succeeded && r.Value is not null) ? enumerator.Current.ContinuationToken : null)}]");

                    hasMoreResult.ThrowIfFailure(unwrap: true);

                    try
                    {
                        await channel.Writer.WriteAsync(hasMoreResult, context.Token);
                    }
                    catch
                    {
                        // We failed to write to the channel. This is unrecoverable, and we'll fail. We should still
                        // dispose of the response.
                        try
                        {
                            hasMoreResult?.Value?.GetRawResponse()?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            // We don't want to fail this operation because of a failure to dispose the response.
                            Tracer.Error(context, ex, $"Failed to dispose page after failing to write. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}] PageSize=[{hasMoreResult?.Value?.Values.Count}]");
                        }

                        throw;
                    }

                    if (hasMoreResult.Value is null)
                    {
                        // We've finished consuming the change feed. This is uncommon.
                        break;
                    }
                }

                channel.Writer.Complete();
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                var exception = new InvalidOperationException($"Failure reading pages from Storage's event feed. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}]", ex);
                channel.Writer.Complete(exception);
                return new BoolResult(exception, $"Failure reading pages from Storage's event feed. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}]");
            }
            finally
            {
                channel.Writer.TryComplete(error: new InvalidOperationException($"This should never happen. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}]"));
            }
        }

        private async Task<BoolResult> ConsumeAccountChanges(
            OperationContext context,
            DateTime now,
            CancellationTokenSource gcCancellationSource,
            BlobCacheStorageAccountName accountName,
            IAzureStorageCredentials credentials,
            Lock<BoolResult> consumePageLock)
        {
            var continuationToken = _db.GetCursor(accountName.AccountName);

            using var cancellableNestedContext = context
                .CreateNested(nameof(ConsumeAccountChanges))
                .WithCancellationToken(gcCancellationSource.Token);
            context = cancellableNestedContext.Context;

            var channel = Channel.CreateBounded<Result<Page<IBlobChangeFeedEvent>?>>(
                new BoundedChannelOptions(capacity: 10)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

            using var readerCancellationSource = new CancellationTokenSource();
            using var readerCancellableContext = cancellableNestedContext
                .Context
                .CreateNested(nameof(ReadPagesFromStorageAsync))
                .WithCancellationToken(readerCancellationSource.Token);
            var readerTask = ReadPagesFromStorageAsync(readerCancellableContext, accountName, credentials, continuationToken, channel);

            try
            {
                while (!context.Token.IsCancellationRequested)
                {
                    // Wait until we have downloaded a page we can use 
                    var hasReadPage = await channel.Reader.WaitToReadAsync(context.Token);
                    if (!hasReadPage)
                    {
                        // The channel was completed. If it had completed in a failure mode, we'd have thrown above.
                        // Therefore, we can only land here if we actually have a page ready to process.
                        return BoolResult.Success;
                    }

                    bool doneProcessingItems = true;
                    Result<BoolResult?> lockResult = await consumePageLock.UseAsync(async _ =>
                    {
                        var readPageResult = await channel.Reader.ReadAsync(context.Token);
                        if (!readPageResult.Succeeded)
                        {
                            // We have failed to read a page. This is unrecoverable. Cancel further page processing.
                            return readPageResult;
                        }

                        if (readPageResult.Value is null)
                        {
                            // We've finished consuming the change feed.
                            return BoolResult.Success;
                        }

                        var page = readPageResult.Value!;
                        try
                        {
                            var maxDateProcessed = await ProcessPageAsync(context, page, accountName, continuationToken);
                            if (context.Token.IsCancellationRequested)
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

                            // This ensures we only process pages until a fixed point in time across all accounts, so we have a
                            // "consistent" view of the change feed (note: it's actually impossible to have consistency).
                            if (maxDateProcessed.Value > now)
                            {
                                return BoolResult.Success;
                            }

                            doneProcessingItems = false;
                            return BoolResult.Success;
                        }
                        finally
                        {
                            try
                            {
                                page?.GetRawResponse()?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                // We don't want to fail this operation because of a failure to dispose the response.
                                Tracer.Error(context, ex, $"Failed to dispose page. Account=[{accountName}] ContinuationToken=[{continuationToken ?? "null"}] PageSize=[{page.Values.Count}]");
                            }
                        }
                    });

                    if (!lockResult.Succeeded || !lockResult.Value.Succeeded)
                    {
                        // We've failed to process a page. This is unrecoverable. Cancel further page processing.
                        await gcCancellationSource.CancelTokenAsyncIfSupported();
                        return lockResult.Succeeded ? lockResult.Value : lockResult;
                    }

                    if (doneProcessingItems)
                    {
                        return lockResult;
                    }
                }
            }
            finally
            {
                await readerCancellationSource.CancelTokenAsyncIfSupported();

                try
                {
                    await readerTask.ThrowIfFailureAsync();
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    // Ignoring on purpose. This is expected to happen as part of normal operation as per above
                    // cancellation.
                }
            }

            return context.Token.IsCancellationRequested
                ? new BoolResult("Cancellation was requested")
                : BoolResult.Success;
        }

        internal virtual IChangeFeedClient CreateChangeFeedClient(IAzureStorageCredentials creds)
        {
            return new AzureChangeFeedClientAdapter(creds.CreateBlobChangeFeedClient());
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

            var eventTimestampUtc = change.EventTime.UtcDateTime;
            switch (blobPath.Container.Purpose)
            {
                case BlobCacheContainerPurpose.Content:
                {
                    // If resharding happened, we don't want to process events for the other shard configuration.
                    if (!blobPath.HasMatrixMatch(_contentMatrix))
                    {
                        return;
                    }

                    _updater.ContentCreated(context, blobPath, change.ContentLength);

                    _db.SetNamespaceLastAccessTime(blobPath.NamespaceId, blobPath.Container.Matrix, eventTimestampUtc);
                    break;
                }
                case BlobCacheContainerPurpose.Metadata:
                {
                    // If resharding happened, we don't want to process events for the other shard configuration.
                    if (!blobPath.HasMatrixMatch(_metadataMatrix))
                    {
                        return;
                    }

                    // Failing to process a content hash list is a fatal error, so we must abort further processing.
                    await _updater
                        .ContentHashListCreatedAsync(new LifetimeDatabaseUpdater.FingerprintCreationEvent(
                            context,
                            blobPath,
                            change.ContentLength,
                            EventTimestampUtc: eventTimestampUtc))
                        .ThrowIfFailureAsync();


                    _db.SetNamespaceLastAccessTime(blobPath.NamespaceId, blobPath.Container.Matrix, eventTimestampUtc);
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

        /// <inheritdoc />
        private class AzureChangeFeedClientAdapter : IChangeFeedClient
        {
            private readonly BlobChangeFeedClient _instance;

            public AzureChangeFeedClientAdapter(BlobChangeFeedClient instance)
            {
                _instance = instance;
            }

            /// <inheritdoc />
            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(string? continuationToken, int? pageSizeHint)
            {
                await using var enumerator = _instance
                    .GetChangesAsync(continuationToken)
                    .AsPages(pageSizeHint: pageSizeHint)
                    .GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync())
                {
                    yield return CreateAdapter(enumerator);
                }
            }

            /// <inheritdoc />
            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(DateTime? startTimeUtc, int? pageSizeHint)
            {
                await using var enumerator = _instance
                    .GetChangesAsync(start: startTimeUtc)
                    .AsPages(pageSizeHint: pageSizeHint)
                    .GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync())
                {
                    yield return CreateAdapter(enumerator);
                }
            }

            private static Page<IBlobChangeFeedEvent> CreateAdapter(IAsyncEnumerator<Page<BlobChangeFeedEvent>> enumerator)
            {
                var page = enumerator.Current;
                var changes = page.Values.Select(c => new BlobChangeFeedEventAdapter(c)).ToArray();
                var newPage = Page<IBlobChangeFeedEvent>.FromValues(changes, page.ContinuationToken, page.GetRawResponse());
                return newPage;
            }
        }

        /// <inheritdoc />
        internal class BlobChangeFeedEventAdapter : IBlobChangeFeedEvent
        {
            private readonly BlobChangeFeedEvent _instance;

            public BlobChangeFeedEventAdapter(BlobChangeFeedEvent instance)
            {
                _instance = instance;
            }

            /// <inheritdoc />
            public DateTimeOffset EventTime => _instance.EventTime;

            /// <inheritdoc />
            public BlobChangeFeedEventType EventType => _instance.EventType;

            /// <inheritdoc />
            public string Subject => _instance.Subject;

            /// <inheritdoc />
            public long ContentLength => _instance.EventData.ContentLength;
        }
    }
}
