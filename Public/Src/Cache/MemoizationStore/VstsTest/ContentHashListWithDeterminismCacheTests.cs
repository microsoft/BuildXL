// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public class ContentHashListWithDeterminismCacheTests
    {
        [Fact]
        public void NonDeterministicItemShouldNotBeCached()
        {
            var cache = ContentHashListWithDeterminismCache.Instance;

            var cacheNamespace = "ns";
            var strongFingerprint = new StrongFingerprint(Fingerprint.Random(), Selector.Random());
            var contentHashList = new ContentHashListWithDeterminism(
                new ContentHashList(new ContentHash[] {ContentHash.Random()}),
                CacheDeterminism.None);

            // Lets check first that the content hash list is not deterministic.
            contentHashList.Determinism.IsDeterministic.Should().BeFalse();

            cache.AddValue(cacheNamespace, strongFingerprint, contentHashList);
            bool foundInCache = cache.TryGetValue(cacheNamespace, strongFingerprint, out _);

            foundInCache.Should().BeFalse("The cache should not save non-deterministic content hash lists.");
        }
    }
}
