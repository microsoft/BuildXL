// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class VsoHashAlgorithmTests
    {
        [Fact]
        public void HashAlgorithmForCacheIsTheSame()
        {
            using (var hashAlgorithm = new VsoHashAlgorithm())
            {
                var blobSizes = new[]
                {
                    0, 1,
                    VsoHash.BlockSize - 1, VsoHash.BlockSize, VsoHash.BlockSize + 1,
                    (2 * VsoHash.BlockSize) - 1, 2 * VsoHash.BlockSize, (2 * VsoHash.BlockSize) + 1,
                };

                foreach (int blobSize in blobSizes)
                {
                    var content = ThreadSafeRandom.GetBytes(blobSize);
                    hashAlgorithm.Initialize();

                    byte[] hashAlgoBytes = hashAlgorithm.ComputeHash(content);
                    BlobIdentifier blobId = VsoHash.CalculateBlobIdentifier(content);
                    Assert.True(hashAlgoBytes.SequenceEqual(blobId.Bytes));
                }
            }
        }

        [Fact]
        public void HashAlgorithmIsStable()
        {
            // We want to test with multiple blocks.
            var bytes = new byte[VsoHash.BlockSize * 5];

            using (var hashAlgorithm = new VsoHashAlgorithm())
            {
                hashAlgorithm.Initialize();
                byte[] hashAlgoBytes = hashAlgorithm.ComputeHash(bytes);
                var hash = new ContentHash(HashType.Vso0, hashAlgoBytes);
                Assert.Equal("VSO0:36668B653DB0B48D3AA1F2FDDCEA481B34A310C166B9B041A5B23B59BE02E5DB00", hash.ToString());
            }
        }
    }
}
