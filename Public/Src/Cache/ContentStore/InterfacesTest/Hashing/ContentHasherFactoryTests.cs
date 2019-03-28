// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ContentHasherFactoryTests
    {
        [Fact]
        public void EveryHashTypeProducesInstance()
        {
            foreach (HashType hashType in Enum.GetValues(typeof(HashType)))
            {
                if (hashType == HashType.Unknown)
                {
                    continue;
                }

                if (hashType == HashType.DeprecatedVso0)
                {
                    continue;
                }

                using (var hasher = HashInfoLookup.Find(hashType).CreateContentHasher())
                {
                    Assert.NotNull(hasher);
                    Assert.Equal(hashType, hasher.Info.HashType);
                }
            }
        }
    }
}
