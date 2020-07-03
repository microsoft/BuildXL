// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ContentHashWithSizeTests
    {
        [Fact]
        public void ConstructionSetsMembers()
        {
            var contentHash = ContentHash.Random();
            const long size = long.MaxValue;
            var contentHashWithSize = new ContentHashWithSize(contentHash, size);
            Assert.Equal(contentHash, contentHashWithSize.Hash);
        }
    }
}
