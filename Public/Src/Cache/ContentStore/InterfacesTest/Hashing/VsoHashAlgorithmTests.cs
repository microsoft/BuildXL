// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
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
                    {
                        hashAlgorithm.Initialize();
                        byte[] hashAlgoBytes = hashAlgorithm.ComputeHash(content);
                        BlobIdentifier blobId = VsoHash.CalculateBlobIdentifier(content);
                        Assert.True(hashAlgoBytes.SequenceEqual(blobId.Bytes));
                    }
                }
            }
        }
    }
}
