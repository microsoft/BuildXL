// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public class BlobContentHashListCacheTests
    {
        [Fact]
        public void NonDeterministicItemShouldNotBeCached()
        {
            var cache = BlobContentHashListCache.Instance;

            var cacheNamespace = "ns";
            var strongFingerprint = new StrongFingerprint(Fingerprint.Random(), Selector.Random());
            var contentHashList = new BlobContentHashListWithCacheMetadata(
                new BlobContentHashListWithDeterminism(Guid.NewGuid(), CreateBlobIdentifier()),
                DateTime.UtcNow);

            // Lets check first that the content hash list is not deterministic.
            contentHashList.Determinism.IsDeterministic.Should().BeFalse();

            cache.AddValue(cacheNamespace, strongFingerprint, contentHashList);
            bool foundInCache = cache.TryGetValue(cacheNamespace, strongFingerprint, out _);

            foundInCache.Should().BeFalse("The cache should not save non-deterministic content hash lists.");
        }

        private BlobIdentifier CreateBlobIdentifier()
        {
            const string HashIdentifier = "54CE418A2A89A74B42CC39630167795DED5F3B16A75FF32A01B2B01C59697784";
            return BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier.ToUpperInvariant());
        }
    }
}
