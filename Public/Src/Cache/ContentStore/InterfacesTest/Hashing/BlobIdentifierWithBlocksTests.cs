// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class BlobIdentifierWithBlocksTests
    {
        private readonly BlobIdentifierWithBlocks _blobIdentifierWithBlocks = Guid.NewGuid().ToByteArray().CalculateBlobIdentifierWithBlocks();
        private readonly BlobIdentifierWithBlocks _blobIdentifierWithBlocks2 = Guid.NewGuid().ToByteArray().CalculateBlobIdentifierWithBlocks();

        [Fact]
        public void ConstructionWithEmptyBlockHashesThrows()
        {
            BlobIdentifierWithBlocks identifier = null;
            Assert.Throws<InvalidDataException>(() => identifier = new BlobIdentifierWithBlocks(_blobIdentifierWithBlocks.BlobId, new List<BlobBlockHash>()));
            Assert.Null(identifier);
        }

        [Fact]
        public void ConstructionWithNullBlockHashesThrows()
        {
            BlobIdentifierWithBlocks identifier = null;
            Assert.Throws<ArgumentNullException>(() => identifier = new BlobIdentifierWithBlocks(_blobIdentifierWithBlocks.BlobId, null));
            Assert.Null(identifier);
        }

        [Fact]
        public void DeserializeWithIncorrectBlockHashesThrows()
        {
            string serialized = _blobIdentifierWithBlocks.Serialize();
            string serialized2 = _blobIdentifierWithBlocks2.Serialize();

            Assert.Equal(serialized.Length, serialized2.Length);
            Assert.NotEqual(serialized, serialized2);

            int splitIndex = serialized.Length / 2;
            string corrupted = serialized.Substring(0, splitIndex) + serialized2.Substring(splitIndex);

            Assert.Throws<InvalidDataException>(() => BlobIdentifierWithBlocks.Deserialize(corrupted));
        }

        [Fact]
        public void GetIdentifierReturnsCorrectIdentifier()
        {
            BlobIdentifier identifierOnly = _blobIdentifierWithBlocks.BlobId;
            Assert.Equal(_blobIdentifierWithBlocks.BlobId.ValueString, identifierOnly.ValueString);
        }

        [Fact]
        public void SerializeDeserializeAreSameWithNotEmptyBlocks()
        {
            string serialized = _blobIdentifierWithBlocks.Serialize();
            var identifier2 = BlobIdentifierWithBlocks.Deserialize(serialized);

            Assert.Equal(_blobIdentifierWithBlocks.BlobId.ValueString, identifier2.BlobId.ValueString);
            Assert.Equal(identifier2.BlobId.ValueString, _blobIdentifierWithBlocks.BlobId.ValueString);
        }
    }
}
