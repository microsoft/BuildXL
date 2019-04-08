// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using ContentStoreTest.Test;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
   [Trait("Category", "Simulation")]
   [SuppressMessage("ReSharper", "UnusedMember.Global")]
   public class WriteThroughBuildCacheSimulationTests : BuildCacheSimulationTests
    {
        public WriteThroughBuildCacheSimulationTests()
            : this(StorageOption.Item)
        {
        }

        protected WriteThroughBuildCacheSimulationTests(StorageOption storageOption)
            : base(TestGlobal.Logger, BackingOption.WriteThrough, storageOption)
        {
        }
    }
}
