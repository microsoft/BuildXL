// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;
#if NET_FRAMEWORK
using MD5Cng = System.Security.Cryptography.MD5Cng;
#else
using MD5Cng = System.Security.Cryptography.MD5CryptoServiceProvider;
#endif

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
#pragma warning disable SYSLIB0021 // Type or member is obsolete. Temporarily suppressing the warning for .net 6. Work item: 1885580
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
#pragma warning restore SYSLIB0021 // Type or member is obsolete
}
