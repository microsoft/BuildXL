// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class SHA1ContentHasherTests : ContentHasherTests<SHA1Managed>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<SHA1Managed>(SHA1HashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }
    }
}
