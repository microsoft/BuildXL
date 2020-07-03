// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class VsoHashInfoTests
    {
        [Fact]
        public void VsoContentHasherAlgorithmUsesVsoHashInfo()
        {
            using (var hasher = HashInfoLookup.Find(HashType.Vso0).CreateContentHasher())
            {
                Assert.Equal(VsoHashInfo.Instance.ByteLength, hasher.Info.ByteLength);
                Assert.Equal(VsoHashInfo.Instance.HashType.ToString(), hasher.Info.Name);
                Assert.Equal(VsoHashInfo.Instance.HashType, hasher.Info.HashType);
            }
        }
    }
}
