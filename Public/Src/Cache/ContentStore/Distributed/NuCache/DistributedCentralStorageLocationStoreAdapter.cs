// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Adapts <see cref="LocalLocationStore"/> to interface needed for content locations (<see cref="DistributedCentralStorage.ILocationStore"/>) by
    /// <see cref="NuCache.DistributedCentralStorage"/>
    /// </summary>
    internal class DistributedCentralStorageLocationStoreAdapter : DistributedCentralStorage.ILocationStore
    {
        public ClusterState ClusterState => _store.ClusterState;
        public DistributedCentralStoreConfiguration Configuration => _store.Configuration.DistributedCentralStore!;
        public Tracer Tracer { get; } = new Tracer(nameof(DistributedCentralStorageLocationStoreAdapter));

        // Randomly generated seed for use when computing derived hash represent fake content for tracking
        // which machines have started copying a particular piece of content
        private const uint _startedCopyHashSeed = 1006063109;

        private readonly LocalLocationStore _store;

        public DistributedCentralStorageLocationStoreAdapter(LocalLocationStore store)
        {
            _store = store;
        }

        public Result<MachineLocation> GetRandomMachineLocation()
        {
            return ClusterState.GetRandomMachineLocation(Array.Empty<MachineLocation>());
        }

        public ValueTask<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo)
        {
            return _store.GlobalCacheStore.RegisterLocationAsync(context, ClusterState.PrimaryMachineId, contentInfo.SelectList(c => (ShortHashWithSize)c), touch: false);
        }

        private Task<GetBulkLocationsResult> GetBulkCoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes)
        {
            return _store.GetBulkFromGlobalAsync(context, ClusterState.PrimaryMachineId, contentHashes);
        }

        public async Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, ContentHash hash)
        {
            var startedCopyHash = ComputeStartedCopyHash(hash);
            await RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(startedCopyHash, -1) }).ThrowIfFailure();

            for (int i = 0; i < Configuration.PropagationIterations; i++)
            {
                // If initial place fails, try to copy the content from remote locations
                var (hashInfo, pendingCopyCount) = await GetFileLocationsAsync(context, hash, startedCopyHash);

                var machineId = ClusterState.PrimaryMachineId.Index;
                int machineNumber = GetMachineNumber();
                var requiredReplicas = ComputeRequiredReplicas(machineNumber);

                var actualReplicas = hashInfo.Locations?.Count ?? 0;

                // Copy from peers if:
                // The number of pending copies is known to be less that the max allowed copies
                // OR the number replicas exceeds the number of required replicas computed based on the machine index
                bool shouldCopy = pendingCopyCount < Configuration.MaxSimultaneousCopies || actualReplicas >= requiredReplicas;

                Tracer.Debug(context, $"{i} (ShouldCopy={shouldCopy}): Hash={hash.ToShortString()}, Id={machineId}" +
                    $", Replicas={actualReplicas}, RequiredReplicas={requiredReplicas}, Pending={pendingCopyCount}, Max={Configuration.MaxSimultaneousCopies}");

                if (shouldCopy)
                {
                    return new GetBulkLocationsResult(new[] { hashInfo });
                }

                // Wait for content to propagate to more machines
                await Task.Delay(Configuration.PropagationDelay, context.Token);
            }

            return new GetBulkLocationsResult("Insufficient replicas");
        }

        private ContentHash ComputeStartedCopyHash(ContentHash hash)
        {
            var murmurHash = MurmurHash3.Create(hash.ToByteArray(), _startedCopyHashSeed);

            var hashLength = HashInfoLookup.Find(CachingCentralStorage.HashType).ByteLength;
            var buffer = murmurHash.ToByteArray();
            Array.Resize(ref buffer, hashLength);

            return new ContentHash(CachingCentralStorage.HashType, buffer);
        }

        private async Task<(ContentHashWithSizeAndLocations info, int pendingCopies)> GetFileLocationsAsync(OperationContext context, ContentHash hash, ContentHash startedCopyHash)
        {
            // Locations are registered under the derived fake startedCopyHash to keep a count of which machines have started
            // copying content. This allows computing the amount of pending copies by subtracting the machines which have
            // finished copying (i.e. location is registered with real hash)
            var result = await GetBulkCoreAsync(context, new[] { hash, startedCopyHash }).ThrowIfFailure();
            var info = result.ContentHashesInfo[0];

            var startedCopyLocations = result.ContentHashesInfo[1].Locations!;
            var finishedCopyLocations = info.Locations!;
            var pendingCopies = startedCopyLocations.Except(finishedCopyLocations).Count();

            return (info, pendingCopies);
        }

        /// <summary>
        /// Computes an index for the machine among active machines
        /// </summary>
        private int GetMachineNumber()
        {
            var machineId = ClusterState.PrimaryMachineId.Index;
            var machineNumber = machineId - ClusterState.InactiveMachineList.Count(id => id.Index < machineId);
            return machineNumber;
        }

        private int ComputeRequiredReplicas(int index)
        {
            if (index <= 0)
            {
                return 1;
            }

            // Threshold is index / MaxSimultaneousCopies.
            // This ensures when locations are chosen at random there should be on average MaxSimultaneousCopies or less
            // from the set of locations assuming worst case where all machines are trying to copy concurrently
            var machineThreshold = index / Configuration.MaxSimultaneousCopies;
            return Math.Max(1, machineThreshold);
        }
    }
}
