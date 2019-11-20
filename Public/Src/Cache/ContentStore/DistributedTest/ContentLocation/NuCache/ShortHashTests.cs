// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ShortHashTests
    {
        [Fact]
        public void TestToString()
        {
            // Hash.ToString is a very important method, because the result of it is used as keys in Redis.
            var hash = ContentHash.Random(HashType.Vso0);

            var shortHash = new ShortHash(hash);

            hash.ToString().Should().Contain(shortHash.ToString());

            var sb = new StringBuilder();
            shortHash.ToString(sb);
            shortHash.ToString().Should().BeEquivalentTo(sb.ToString());
        }
    }
}
