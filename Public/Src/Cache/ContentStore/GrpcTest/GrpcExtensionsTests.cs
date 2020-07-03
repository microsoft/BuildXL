// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace ContentStoreTest.Grpc
{
    public class GrpcExtensionsTests
    {
        [Fact]
        public void RoundtripContentHash()
        {
            ContentHash hash = ContentHash.Random(HashType.Vso0);
            ByteString byteString = hash.ToByteString();
            ContentHash roundtripContentHash = byteString.ToContentHash(HashType.Vso0);

            roundtripContentHash.Should().Be(hash);
        }
    }
}
