// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
