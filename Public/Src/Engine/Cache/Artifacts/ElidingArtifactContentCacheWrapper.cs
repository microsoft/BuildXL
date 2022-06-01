// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;
using OperationHints = BuildXL.Cache.ContentStore.Interfaces.Sessions.OperationHints;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Artifact content cache wrapper which elides calls to <see cref="TryLoadAvailableContentAsync(System.Collections.Generic.IReadOnlyList{BuildXL.Cache.ContentStore.Hashing.ContentHash}, CancellationToken, BuildXL.Cache.ContentStore.Interfaces.Sessions.OperationHints)"/> to prevent duplicate calls
    /// for the same hash to the inner cache.
    /// NOTE: Concurrent requests for the same hash may not be elided.
    /// </summary>
    public sealed class ElidingArtifactContentCacheWrapper : IArtifactContentCache
    {
        private readonly IArtifactContentCache m_innerCache;
        private readonly ConcurrentBigMap<ContentHash, string> m_availableContent = new ConcurrentBigMap<ContentHash, string>();

        private readonly ObjectPool<Dictionary<ContentHash, ContentAvailabilityResult>> m_hashAvailabilityMapPool = new ObjectPool<Dictionary<ContentHash, ContentAvailabilityResult>>(
            () => new Dictionary<ContentHash, ContentAvailabilityResult>(),
            d => d.Clear());

        private readonly ObjectPool<List<ContentHash>> m_hashListPool = new ObjectPool<List<ContentHash>>(
            () => new List<ContentHash>(),
            d => d.Clear());

        /// <nodoc />
        public ElidingArtifactContentCacheWrapper(IArtifactContentCache innerCache)
        {
            m_innerCache = innerCache;
        }

        /// <inheritdoc />
        public async Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes, CancellationToken cancellationToken, OperationHints hints = default)
        {
            using (var hashAvailabilityMapWrapper = m_hashAvailabilityMapPool.GetInstance())
            using (var hashListWrapper = m_hashListPool.GetInstance())
            {
                var hashAvailabilityMap = hashAvailabilityMapWrapper.Instance;
                var hashList = hashListWrapper.Instance;

                var uniqueUnknownAvailabilityHashes = DeduplicateAndGetUnknownAvailabilityHashes(hashes, hashAvailabilityMap, hashList);

                bool allContentAvailable = true;
                if (uniqueUnknownAvailabilityHashes.Count != 0)
                {
                    // Only query inner cache if there are hashes whose availabilty is unknown
                    var possibleBatchResult = await m_innerCache.TryLoadAvailableContentAsync(uniqueUnknownAvailabilityHashes, cancellationToken, hints);
                    if (!possibleBatchResult.Succeeded)
                    {
                        // If not successful just return the result
                        return possibleBatchResult;
                    }

                    // Populate hash availability map with results from inner cache
                    foreach (var result in possibleBatchResult.Result.Results)
                    {
                        hashAvailabilityMap[result.Hash] = result;
                        if (!result.IsAvailable)
                        {
                            allContentAvailable = false;
                        }
                        else
                        {
                            // Mark the hash as available for subsequent operations
                            m_availableContent.TryAdd(result.Hash, result.SourceCache);
                        }
                    }

                    if (uniqueUnknownAvailabilityHashes == hashes)
                    {
                        // If the hashes are the same as original hashes just return the result
                        // now that availability map has been updated.
                        return possibleBatchResult;
                    }
                }

                ContentAvailabilityResult[] results = new ContentAvailabilityResult[hashes.Count];
                for (int i = 0; i < hashes.Count; i++)
                {
                    var hash = hashes[i];
                    if (hashAvailabilityMap.TryGetValue(hash, out var result))
                    {
                        results[i] = result;
                    }
                    else
                    {
                        throw Contract.AssertFailure(I($"Hash {hash} should be present in availability map."));
                    }
                }

                return new ContentAvailabilityBatchResult(ReadOnlyArray<ContentAvailabilityResult>.FromWithoutCopy(results), allContentAvailable);
            }
        }

        private IReadOnlyList<ContentHash> DeduplicateAndGetUnknownAvailabilityHashes(
            IReadOnlyList<ContentHash> hashes, 
            Dictionary<ContentHash, ContentAvailabilityResult> hashAvailabilityMap, 
            List<ContentHash> hashList)
        {
            bool hasDuplicateOrKnownAvailableHash = false;

            for (int i = 0; i < hashes.Count; i++)
            {
                var hash = hashes[i];
                if (!hashAvailabilityMap.TryGetValue(hash, out var result))
                {
                    if (m_availableContent.TryGetValue(hash, out var sourceCache))
                    {
                        // Known available hash
                        hasDuplicateOrKnownAvailableHash = true;
                        result = new ContentAvailabilityResult(hash, true, 0, sourceCache);
                    }

                    hashAvailabilityMap[hash] = result;
                }
                else
                {
                    // Duplicate
                    hasDuplicateOrKnownAvailableHash = true;
                }
            }

            IReadOnlyList<ContentHash> uniqueUnknownAvailabilityHashes;
            if (!hasDuplicateOrKnownAvailableHash)
            {
                // Just use original set of hashes since no duplicates or known available hashes
                uniqueUnknownAvailabilityHashes = hashes;
            }
            else
            {
                // Only submit unique hashes which are not known to be available to the inner cache
                foreach (var entry in hashAvailabilityMap)
                {
                    if (!entry.Value.IsAvailable)
                    {
                        hashList.Add(entry.Key);
                    }
                }

                uniqueUnknownAvailabilityHashes = hashList;
            }

            return uniqueUnknownAvailabilityHashes;
        }

        /// <inheritdoc />
        public Task<Possible<Unit, Failure>> TryMaterializeAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash, CancellationToken cancellationToken)
        {
            return m_innerCache.TryMaterializeAsync(fileRealizationModes, path, contentHash, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Possible<StreamWithLength, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
        {
            return m_innerCache.TryOpenContentStreamAsync(contentHash);
        }

        /// <inheritdoc />
        public Task<Possible<Unit, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash, StoreArtifactOptions options)
        {
            return m_innerCache.TryStoreAsync(fileRealizationModes, path, contentHash, options);
        }

        /// <inheritdoc />
        public Task<Possible<ContentHash, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, StoreArtifactOptions options)
        {
            return m_innerCache.TryStoreAsync(fileRealizationModes, path, options);
        }

        /// <inheritdoc />
        public Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash, StoreArtifactOptions options)
        {
            return m_innerCache.TryStoreAsync(content, contentHash, options);
        }
    }
}
