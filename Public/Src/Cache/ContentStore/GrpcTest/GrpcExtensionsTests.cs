// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
