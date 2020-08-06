// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class DedupChunkContentHasherTests : ContentHasherTests<DedupChunkHashAlgorithm>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<DedupChunkHashAlgorithm>(DedupSingleChunkHashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }
    }
}
