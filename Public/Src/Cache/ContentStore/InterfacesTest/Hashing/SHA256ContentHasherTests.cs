// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class SHA256ContentHasherTests : ContentHasherTests<SHA256Managed>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<SHA256Managed>(SHA256HashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }
    }
}
