// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
