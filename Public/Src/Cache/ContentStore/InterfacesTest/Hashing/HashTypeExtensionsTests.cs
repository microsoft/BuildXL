// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
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
        [InlineData("DEDUPNODEORCHUNK", HashType.DedupNodeOrChunk)]
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
        [InlineData("DEDUPNODEORCHUNK", HashType.DedupNodeOrChunk)]
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
    }
}
