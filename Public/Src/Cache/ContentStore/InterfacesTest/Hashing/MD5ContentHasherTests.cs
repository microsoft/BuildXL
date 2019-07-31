// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;
#if NET_FRAMEWORK
using MD5Cng = System.Security.Cryptography.MD5Cng;
#else
using MD5Cng = System.Security.Cryptography.MD5CryptoServiceProvider;
#endif

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class MD5ContentHasherTests : ContentHasherTests<MD5Cng>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<MD5Cng>(MD5HashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }
    }
}
