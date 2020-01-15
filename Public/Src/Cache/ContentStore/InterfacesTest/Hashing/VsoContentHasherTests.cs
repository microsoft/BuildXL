// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class VsoContentHasherTests : ContentHasherTests<VsoHashAlgorithm>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<VsoHashAlgorithm>(VsoHashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }
    }
}
