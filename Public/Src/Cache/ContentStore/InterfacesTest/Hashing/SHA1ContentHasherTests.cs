// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
