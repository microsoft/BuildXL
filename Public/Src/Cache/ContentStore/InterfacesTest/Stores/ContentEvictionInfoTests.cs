// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Utils
{
    public class ContentEvictionInfoTests
    {
        [Fact]
        public void TestContentEvictionInfoComparer()
        {
            var inputs = new []
                         {
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(2), replicaCount: 1, size: 1, isImportantReplica: false),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(2), replicaCount: 1, size: 2, isImportantReplica: true),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(2), replicaCount: 1, size: 1, isImportantReplica: true),


                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(2), TimeSpan.FromHours(3), replicaCount: 1, size: 1, isImportantReplica: true),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(2), TimeSpan.FromHours(3), replicaCount: 1, size: 1, isImportantReplica: false),
                         };

            var list = inputs.ToList();
            list.Sort(ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance);

            var expected = new[]
                           {
                               inputs[4], // EffAge=3, Importance=false
                               inputs[3], // EffAge=3, Importance=true

                               inputs[0], // EffAge=2, Importance=false
                               inputs[1], // EffAge=2, Importance=true, Cost=2
                               inputs[2], // EffAge=2, Importance=true, Cost=1
                           };

            Assert.Equal(expected, list.ToArray());


        }
    }
}
