// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache
{
    /// <summary>
    /// Tests for <see cref="ElidingArtifactContentCacheWrapper"/>.
    /// </summary>
    public sealed class ElidingArtifactContentCacheWrapperTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ElidingArtifactContentCacheWrapperTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task BasicTests()
        {
            var harness = new HarnessArtifactContentCache();
            var elidingCache = new ElidingArtifactContentCacheWrapper(harness);

            var h1 = await harness.StoreGuid(Guid.NewGuid());
            var h2 = await harness.StoreGuid(Guid.NewGuid());
            var h3 = await harness.StoreGuid(Guid.NewGuid());

            ContentHash[] storedHashes = new[] { h1, h2, h3 };

            ContentHash[] loadedHashes = new ContentHash[1000];

            for (int i = 0; i < 1000; i++)
            {
                loadedHashes[i] = storedHashes[i % storedHashes.Length];
            }

            // Perform multiple iterations to ensure subsequent calls pick up cached results rather than
            // going to backing cache
            for (int i = 0; i < 5; i++)
            {
                var result = await elidingCache.TryLoadAvailableContentAsync(loadedHashes);
                Assert.True(result.Succeeded);
                Assert.True(result.Result.AllContentAvailable);
                Assert.Equal(loadedHashes.Length, result.Result.Results.Length);
                Assert.True(result.Result.Results.All(r => r.IsAvailable));

                foreach (var storedHash in storedHashes)
                {
                    // Content hash should only be requested once
                    Assert.Equal(1, harness.GetLoadCount(storedHash));
                }
            }

            int[] missingHashIndices = new int[] { 4, 27 };

            var missingHash = ContentHashingUtilities.CreateRandom();
            foreach (var missingHashIndex in missingHashIndices)
            {
                loadedHashes[missingHashIndex] = missingHash;
            }

            var notAllAvailableResults = await elidingCache.TryLoadAvailableContentAsync(loadedHashes);
            Assert.True(notAllAvailableResults.Succeeded);
            Assert.False(notAllAvailableResults.Result.AllContentAvailable);
            Assert.Equal(loadedHashes.Length, notAllAvailableResults.Result.Results.Length);

            for (int i = 0; i < loadedHashes.Length; i++)
            {
                bool shouldBeAvailable = !missingHashIndices.Contains(i);

                Assert.Equal(shouldBeAvailable, notAllAvailableResults.Result.Results[i].IsAvailable);
            }

            foreach (var storedHash in storedHashes)
            {
                // Content hash should only be requested once even if requested with missing hashes
                Assert.Equal(1, harness.GetLoadCount(storedHash));
            }
        }

        private class HarnessArtifactContentCache : IArtifactContentCache
        {
            private readonly ConcurrentDictionary<ContentHash, int> m_hashLoadCounts = new ConcurrentDictionary<ContentHash, int>();
            private readonly InMemoryArtifactContentCache m_cache;

            public HarnessArtifactContentCache()
            {
                m_cache = new InMemoryArtifactContentCache();
            }

            public int GetLoadCount(ContentHash hash)
            {
                return m_hashLoadCounts.GetOrAdd(hash, 0);
            }

            public async Task<ContentHash> StoreGuid(Guid guid)
            {
                var bytes = guid.ToByteArray();
                var hash = ContentHashingUtilities.HashBytes(bytes);
                using (var stream = new MemoryStream(bytes))
                {
                    Analysis.IgnoreResult(await TryStoreAsync(stream, hash));
                }

                return hash;
            }

            public Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes)
            {
                foreach (var hash in hashes)
                {
                    m_hashLoadCounts.AddOrUpdate(hash, 1, (k, v) => v + 1);
                }

                return this.m_cache.TryLoadAvailableContentAsync(hashes);
            }

            public Task<Possible<Unit, Failure>> TryMaterializeAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash)
            {
                return this.m_cache.TryMaterializeAsync(fileRealizationModes, path, contentHash);
            }

            public Task<Possible<Stream, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
            {
                return this.m_cache.TryOpenContentStreamAsync(contentHash);
            }

            public Task<Possible<Unit, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash)
            {
                return this.m_cache.TryStoreAsync(fileRealizationModes, path, contentHash);
            }

            public Task<Possible<ContentHash, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path)
            {
                return this.m_cache.TryStoreAsync(fileRealizationModes, path);
            }

            public Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash)
            {
                return this.m_cache.TryStoreAsync(content, contentHash);
            }
        }
    }
}
