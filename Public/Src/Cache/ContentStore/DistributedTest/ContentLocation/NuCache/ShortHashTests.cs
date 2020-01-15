// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
