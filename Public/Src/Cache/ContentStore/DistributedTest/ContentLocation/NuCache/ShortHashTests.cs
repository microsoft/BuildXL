// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ShortHashTests
    {
        private readonly ITestOutputHelper _helper;

        public ShortHashTests(ITestOutputHelper helper) => _helper = helper;

        [Fact]
        public void TestToString()
        {
            var hash = ContentHash.Random(HashType.Vso0);

            var shortHash = new ShortHash(hash);

            hash.ToString().Should().Contain(shortHash.ToString());

            var sb = new StringBuilder();
            shortHash.ToString(sb);
            shortHash.ToString().Should().BeEquivalentTo(sb.ToString());
        }

        [Fact]
        public void TestToStringRoundtrip()
        {
            // We should be able to create a short hash from a long hash.
            // Then get a string representation of the short hash and re-create another instance
            // that should be the same as the original short hash.
            var longHashAsString = "VSO0:135752CA343D7AAD9CA65B919957A17FDBB9678F71BC092BD3554CEF8EF144FD00";
            var longHash = ParseContentHash(longHashAsString);

            var shortHash = longHash.AsShortHash();
            var shortHashFromShortString = ParseShortHash(shortHash.ToString());
            shortHash.Should().Be(shortHashFromShortString);
        }

        [Fact]
        public void GetRedisKeyShouldReturn20Characters()
        {
            // Hash.ToString is a very important method, because the result of it is used as keys in Redis.
            // So the output oof GetRedisKey should not change even when ShortHash.ToString() implementation has changed.
            var hash = ContentHash.Random();
            var redisKey = RedisGlobalStore.GetRedisKey(hash);

            redisKey.Should().NotBe(hash.AsShortHash().ToString());
            const int expectedLength = 25; // 'VSO0' + 20 characters for the hash.
            redisKey.Length.Should().Be(expectedLength);
        }

        private static ContentHash ParseContentHash(string str)
        {
            bool parsed = ContentHash.TryParse(str, out var result);
            Contract.Assert(parsed);
            return result;

        }

        private static ShortHash ParseShortHash(string str)
        {
            bool parsed = ShortHash.TryParse(str, out var result);
            Contract.Assert(parsed);
            return result;
        }
    }
}
