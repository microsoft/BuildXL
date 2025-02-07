// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.ChangeFeed;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class BlobLifetimeManager
    {
        private static readonly Tracer Tracer = new(nameof(BlobLifetimeManager));

        public Task RunAsync(
            OperationContext context,
            BlobQuotaKeeperConfig config,
            IAbsFileSystem fileSystem,
            IBlobCacheAccountSecretsProvider secretsProvider,
            IReadOnlyList<BlobCacheStorageAccountName> accountNames,
            IClock clock,
            string runId,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism,
            string cacheInstance,
            BuildCacheConfiguration? buildCacheConfiguration,
            bool dryRun)
        {
            var extraMessage = $"CacheInstance=[{cacheInstance}] RunId=[{runId}]";
            return context.PerformOperationAsync(
                Tracer,
                () => RunCoreAsync(context, config, fileSystem, secretsProvider, accountNames, clock, runId, contentDegreeOfParallelism, fingerprintDegreeOfParallelism, dryRun, cacheInstance, buildCacheConfiguration),
                extraStartMessage: extraMessage,
                extraEndMessage: _ => extraMessage,
                pendingOperationTracingInterval: TimeSpan.FromHours(1));
        }

        private async Task<BoolResult> RunCoreAsync(
            OperationContext context,
            BlobQuotaKeeperConfig config,
            IAbsFileSystem fileSystem,
            IBlobCacheAccountSecretsProvider secretsProvider,
            IReadOnlyList<BlobCacheStorageAccountName> accountNames,
            IClock clock,
            string runId,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism,
            bool dryRun,
            string cacheInstance,
            BuildCacheConfiguration? buildCacheConfiguration)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
            context = context.WithCancellationToken(cts.Token);

            var shardingScheme = new ShardingScheme(ShardingAlgorithm.JumpHash, accountNames);
            var (metadataMatrix, contentMatrix) = shardingScheme.GenerateMatrix();

            var maxSizes = config.Namespaces.ToDictionary(
                c => new BlobNamespaceId(c.Universe, c.Namespace),
                c => (long)(c.MaxSizeGb * 1024 * 1024 * 1024));

            var namespaces = maxSizes.Keys.ToList();

            var topologies = namespaces.ToDictionary(
                n => n,
                n => (IBlobCacheTopology)new ShardedBlobCacheTopology(
                    new ShardedBlobCacheTopology.Configuration(
                        buildCacheConfiguration,
                        shardingScheme,
                        secretsProvider,
                        n.Universe,
                        n.Namespace,
                        config.BlobRetryPolicy)));

            foreach (var topology in topologies.Values)
            {
                await topology.EnsureContainersExistAsync(context).ThrowIfFailureAsync();
            }

            // Using the 0th shard of the cache, so that checkpoint data is preserved between re-sharding.
            // However, the container needs to be differentiated between reshardings.
            string checkpointContainerName;
            BlobCacheStorageAccountName checkpointManagerStorageAccount;
            if (buildCacheConfiguration is not null)
            {
                var shard = buildCacheConfiguration.Shards.First();
                checkpointContainerName = shard.CheckpointContainer.Name;
                checkpointManagerStorageAccount = shard.GetAccountName();
            }
            else
            {
                checkpointContainerName = $"checkpoints-{metadataMatrix}";
                checkpointManagerStorageAccount = ShardingScheme.SortAccounts(accountNames).First();
                Contract.Assert(checkpointManagerStorageAccount is not null);
            }

            var checkpointManagerStorageCreds = await secretsProvider.RetrieveAccountCredentialsAsync(context, checkpointManagerStorageAccount);

            var machineLocation = MachineLocation.Parse(runId);

            await RunWithLeaseAsync(
                context,
                checkpointManagerStorageCreds,
                checkpointContainerName,
                machineLocation,
                cts,
                clock,
                async () =>
                {
                    using var temp = new DisposableDirectory(fileSystem);

                    var rootPath = temp.Path / "LifetimeDatabase";
                    var dbConfig = new RocksDbLifetimeDatabase.Configuration
                    {
                        DatabasePath = rootPath.Path,
                        LruEnumerationPercentileStep = config.LruEnumerationPercentileStep,
                        LruEnumerationBatchSize = config.LruEnumerationBatchSize,
                        BlobNamespaceIds = namespaces,
                    };

                    var checkpointable = new RocksDbLifetimeDatabase.CheckpointableLifetimeDatabase(
                        dbConfig,
                        clock);

                    var registry = new AzureBlobStorageCheckpointRegistry(
                        new AzureBlobStorageCheckpointRegistryConfiguration
                        {
                            Storage = new AzureBlobStorageCheckpointRegistryConfiguration.StorageSettings(
                                Credentials: checkpointManagerStorageCreds,
                                ContainerName: checkpointContainerName)
                        });

                    var centralStorage = new BlobCentralStorage(new BlobCentralStoreConfiguration(
                        checkpointManagerStorageCreds,
                        containerName: checkpointContainerName,
                        checkpointsKey: "lifetime-manager"
                        ));

                    var checkpointManager = new CheckpointManager(
                        checkpointable,
                        registry,
                        centralStorage,
                        new CheckpointManagerConfiguration(
                            WorkingDirectory: temp.Path / "CheckpointManager",
                            PrimaryMachineLocation: machineLocation)
                        {
                            // We don't want to restore checkpoints on a loop. There's only once GC instance that runs
                            // at a time, and it's the only one creating checkpoints.
                            RestoreCheckpoints = false,

                            // Contrary to what you might expect, this component doesn't actually self-call
                            // CreateCheckpoint. That happens inside of the processor that takes this class as
                            // parameter. There's a good reason for that: we need to ensure we pause all processing
                            // while we're creating the checkpoint.
                            CreateCheckpointInterval = config.CheckpointCreationInterval,

                            // Checkpoint files are uploaded in parallel. We don't want to overload the storage account
                            // with too many parallel uploads, but we also don't want to do this serially. This is the
                            // same value we use in CaSaaS.
                            IncrementalCheckpointDegreeOfParallelism = 10,
                        },
                        new CounterCollection<ContentLocationStoreCounters>(),
                        clock);

                    await checkpointManager.StartupAsync(context).ThrowIfFailure();
                    var checkpointStateResult = await registry.GetCheckpointStateAsync(context).ThrowIfFailure();

                    var startTime = clock.UtcNow;

                    RocksDbLifetimeDatabase db;
                    var firstRun = string.IsNullOrEmpty(checkpointStateResult.Value?.CheckpointId);
                    if (!firstRun)
                    {
                        await checkpointManager.RestoreCheckpointAsync(context, checkpointStateResult.Value!).ThrowIfFailure();
                        db = checkpointable.GetDatabase();

                        // The checkpoint might not have been compacted when uploaded, so we should do it here.
                        // This could happen if a checkpoint was created mid-run and GC didn't run to completion.
                        db.Compact(context).ThrowIfFailure();
                    }
                    else
                    {
                        db = await LifetimeDatabaseCreator.CreateAsync(
                            context,
                            dbConfig,
                            clock,
                            contentDegreeOfParallelism,
                            fingerprintDegreeOfParallelism,
                            namespaceId => topologies[namespaceId]).ThrowIfFailureAsync();

                        checkpointable.Database = db;
                    }


                    using (db)
                    {
                        // When GC is too slow to keep up with the change feed, we can end up in a situation where the
                        // cache is too old to be garbage collected. This can happen if the GC falls behind the
                        // retention period for event processing.
                        //
                        // In this case, we will fail GC and require manual intervention to recover. This is a safety
                        // measure to prevent us from deleting data that we should not be deleting.
                        bool changeFeedRetention = false;
                        foreach (var accountName in accountNames)
                        {
                            var opaqueContinuationToken = db.GetCursor(accountName.AccountName);
                            if (string.IsNullOrEmpty(opaqueContinuationToken))
                            {
                                Tracer.Warning(context, $"Failed to find continuation token. Account=[{accountName}]");
                                continue;
                            }

                            var continuationToken = JsonSerializer.Deserialize<ChangeFeedContinuationToken>(opaqueContinuationToken!);
                            if (continuationToken is null)
                            {
                                Tracer.Warning(context, $"Failed to deserialize continuation token. Account=[{accountName}] ContinuationToken=[{opaqueContinuationToken}]");
                                continue;
                            }

                            if (continuationToken.CurrentSegmentCursor.SegmentDate is null)
                            {
                                Tracer.Warning(context, $"Failed to extract segment date from continuation token. Account=[{accountName}] ContinuationToken=[{opaqueContinuationToken}]");
                                continue;
                            }

                            var segmentDate = continuationToken.CurrentSegmentCursor.SegmentDate.Value;
                            var retentionCutoffDate = startTime - config.ChangeFeedRetentionPeriod;
                            if (segmentDate < retentionCutoffDate)
                            {
                                changeFeedRetention = true;
                            }
                        }

                        if (changeFeedRetention)
                        {
                            throw new InvalidOperationException("Garbage collection is too old to run. Please follow the recovery steps.");
                        }

                        var accessors = namespaces.ToDictionary(
                            n => n,
                            db.GetAccessor);

                        if (!firstRun)
                        {
                            // Only get updates from Azure if the database already existed
                            var updater = new LifetimeDatabaseUpdater(topologies, accessors, clock, fingerprintDegreeOfParallelism);
                            AzureStorageChangeFeedEventDispatcher dispatcher =
                                CreateDispatcher(secretsProvider, accountNames, metadataMatrix, contentMatrix, db, updater, clock, checkpointManager, config.ChangeFeedPageSize, buildCacheConfiguration);

                            await dispatcher.ConsumeNewChangesAsync(context, config.CheckpointCreationInterval).ThrowIfFailure();

                            await BlobLifetimeManagerHelpers.HandleConfigAndAccountDifferencesAsync(
                                context, db, secretsProvider, accountNames, config, metadataMatrix, contentMatrix, clock, buildCacheConfiguration);
                        }

                        // TODO: consider doing this in parallel, although it could be argued that if each of these calls
                        // is making full use of resources, it can be better to just do this sequentially.
                        foreach (var namespaceId in namespaces)
                        {
                            context.Token.ThrowIfCancellationRequested();

                            var accessor = accessors[namespaceId];
                            var quotaKeeper = new BlobQuotaKeeper(accessor, topologies[namespaceId], config.LastAccessTimeDeletionThreshold, clock);
                            _ = await quotaKeeper.EnsureUnderQuotaAsync(
                                context,
                                maxSizes[namespaceId],
                                dryRun,
                                contentDegreeOfParallelism,
                                fingerprintDegreeOfParallelism,
                                checkpointManager,
                                config.CheckpointCreationInterval,
                                cacheInstance,
                                runId,
                                startTime);
                        }

                        if (!dryRun)
                        {
                            context.Token.ThrowIfCancellationRequested();

                            // Make sure to compact the database before creating a checkpoint to ensure we are being optimal
                            // with the space we are using.
                            db.Compact(context).ThrowIfFailure();

                            await checkpointManager.CreateCheckpointAsync(
                                context,
                                new EventSequencePoint(clock.UtcNow),
                                maxEventProcessingDelay: null)
                                .ThrowIfFailure();
                        }
                    }
                });

            return BoolResult.Success;
        }

        protected virtual AzureStorageChangeFeedEventDispatcher CreateDispatcher(
            IBlobCacheAccountSecretsProvider secretsProvider,
            IReadOnlyList<BlobCacheStorageAccountName> accountNames,
            string metadataMatrix,
            string contentMatrix,
            RocksDbLifetimeDatabase db,
            LifetimeDatabaseUpdater updater,
            IClock clock,
            CheckpointManager checkpointManager,
            int? changeFeedPageSize,
            BuildCacheConfiguration? buildCacheConfiguration)
        {
            return new AzureStorageChangeFeedEventDispatcher(secretsProvider, accountNames, updater, checkpointManager, db, clock, metadataMatrix, contentMatrix, changeFeedPageSize, buildCacheConfiguration);
        }

        private static async Task RunWithLeaseAsync(
            OperationContext context,
            IAzureStorageCredentials checkpointManagerStorageAccount,
            string checkpointContainerName,
            MachineLocation machineLocation,
            CancellationTokenSource cts,
            IClock clock,
            Func<Task> run)
        {
            var masterSelection = new AzureBlobStorageMasterElectionMechanism(
                new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Storage = new AzureBlobStorageMasterElectionMechanismConfiguration.StorageSettings(
                            checkpointManagerStorageAccount,
                            ContainerName: checkpointContainerName),
                    ReleaseLeaseOnShutdown = true,
                },
                machineLocation,
                clock);

            await masterSelection.StartupAsync(context).ThrowIfFailure();
            var state = await masterSelection.GetRoleAsync(context).ThrowIfFailureAsync();

            if (state.Role != Role.Master)
            {
                throw new InvalidOperationException(
                    $"Failed to get lease on the blob cache. This indicates another GC run is already happening. The cache is currently leased to: {state.Master}");
            }

            try
            {
                using var timer = new IntervalTimer(async () =>
                    {
                        var stateResult = await masterSelection.GetRoleAsync(context);
                        if (!stateResult.Succeeded || stateResult.Value.Role != Role.Master)
                        {
                            Tracer.Error(
                                context,
                                $"Failed to renew lease on the blob cache. Aborting since this could lead to multiple GC runs running at the same time.");

                            await cts.CancelTokenAsyncIfSupported();
                        }
                    },
                    period: TimeSpan.FromMinutes(5),
                    dueTime: TimeSpan.FromMinutes(5));

                await run();
            }
            finally
            {
                await masterSelection.ShutdownAsync(context).ThrowIfFailure();
            }
        }
    }
}
