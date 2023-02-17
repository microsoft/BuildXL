// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Core;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class HashTypeExtensionsTests
    {
        [Theory]
        [InlineData("MD5", HashType.MD5)]
        [InlineData("SHA1", HashType.SHA1)]
        [InlineData("SHA256", HashType.SHA256)]
        [InlineData("VSO0", HashType.Vso0)]
        [InlineData("VSo0", HashType.Vso0)]
        [InlineData("DEDUPNODEORCHUNK", HashType.Dedup64K)]
        [InlineData("DEDUP1024K", HashType.Dedup1024K)]
        public void Serialize(string value, HashType hashType)
        {
            Assert.True(hashType.Serialize().Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("MD5", HashType.MD5)]
        [InlineData("SHA1", HashType.SHA1)]
        [InlineData("SHA256", HashType.SHA256)]
        [InlineData("VSO0", HashType.Vso0)]
        [InlineData("VSo0", HashType.Vso0)]
        [InlineData("DEDUPNODEORCHUNK", HashType.Dedup64K)]
        [InlineData("DEDUP1024K", HashType.Dedup1024K)]
        public void DeserializeSucceeds(string value, HashType expectedHashType)
        {
            HashType hashType;
            Assert.True(value.Deserialize(out hashType));
            Assert.Equal(expectedHashType, hashType);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("00000000000000000000000000000000")]
        public void DeserializeFails(string value)
        {
            HashType hashType;
            var succeeded = value.Deserialize(out hashType);
            Assert.False(succeeded);
        }

        [Fact]
        public void AssertValidDedupHashTypes()
        {
            var hashTypes = Enum.GetValues(typeof(HashType)).Cast<HashType>();
            foreach(var hashType in hashTypes)
            {
                Analysis.IgnoreResult(hashType.IsValidDedup());
            }
        }

        [Fact]
        public void AssertChunkConfigForHashType()
        {
            var hashTypes = Enum.GetValues(typeof(HashType)).Cast<HashType>();
            foreach(var hashType in hashTypes)
            {
                if (hashType.IsValidDedup())
                {
                   Analysis.IgnoreResult(hashType.GetChunkerConfiguration());
                }
                else
                {
                   Assert.Throws<NotImplementedException>(() => hashType.GetChunkerConfiguration());
                }
            }
        }

        [Fact]
        public void AssertAvgChunkSizeForHashType()
        {
            var hashTypes = Enum.GetValues(typeof(HashType)).Cast<HashType>();
            foreach(var hashType in hashTypes)
            {
                if (hashType.IsValidDedup())
                {
                    Analysis.IgnoreResult(hashType.GetAvgChunkSize());
                }
                else
                {
                    Assert.Throws<NotImplementedException>(() => hashType.GetAvgChunkSize());
                }
            }
        }
    }
}
