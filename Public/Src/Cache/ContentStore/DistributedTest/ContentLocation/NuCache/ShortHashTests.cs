// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ShortHashTests
    {
        private readonly ITestOutputHelper _helper;

        public ShortHashTests(ITestOutputHelper helper) => _helper = helper;

        public static IEnumerable<object[]> HashTypes => HashInfoLookup.All().Distinct().Select(i => new object[] { i.HashType });

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void ParseAllHashTypes(HashType hashType)
        {
            var hash = ContentHash.Random(hashType);
            var stringHash = hash.ToShortString();

            // Test using TryParse
            ShortHash.TryParse(stringHash, out var shortHash).Should().BeTrue();
            var expectedShortHash = hash.AsShortHash();
            shortHash.Should().Be(expectedShortHash);

            // Test using constructor
            shortHash = new ShortHash(stringHash);
            shortHash.Should().Be(expectedShortHash);
        }

        [Fact]
        public void ShortHashBinaryRoundtrip()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(debug: false, ms, leaveOpen: false, logStats: false))
                {
                    var hash = ContentHash.Random(HashType.Vso0);
                    var v1 = new ShortHash(hash);
                    v1.Serialize(writer);
                    ms.Position = 0;

                    using (var reader = new BuildXLReader(debug: false, ms, leaveOpen: false))
                    {
                        var v2 = reader.ReadShortHash();
                        Assert.Equal(v1, v2);
                    }
                }
            }
        }

        [Fact]
        public void TestMemoryMarshalRead()
        {
            var hash = ContentHash.Random(HashType.Vso0);
            var shortHash = new ShortHash(hash);
            var data = shortHash.ToByteArray();

            var shortHash2 = MemoryMarshal.Read<ShortHash>(data);
            shortHash2.Should().Be(shortHash);
        }

        [Fact]
        public void TestToByteArray()
        {
            for (int i = 0; i < 1000; i++)
            {
                var hash = ContentHash.Random(HashType.Vso0);
                var shortHash = new ShortHash(hash);

                var byteArray = shortHash.ToByteArray();
                using var handle = shortHash.ToPooledByteArray();
                byteArray.Should().BeEquivalentTo(handle.Value);
            }
        }

        [Fact]
        public void TestEquality()
        {
            for (int i = 0; i < 1000; i++)
            {
                var hash = ContentHash.Random(HashType.Vso0);
                var shortHash = new ShortHash(hash);
                var shortHash2 = new ShortHash(hash);
                shortHash.GetHashCode().Should().Be(shortHash2.GetHashCode());
                shortHash.Should().Be(shortHash2);
            }
        }

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
