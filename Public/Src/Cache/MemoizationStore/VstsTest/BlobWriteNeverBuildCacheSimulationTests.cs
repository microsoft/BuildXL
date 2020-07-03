// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "Simulation")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class BlobWriteNeverBuildCacheSimulationTests : WriteNeverBuildCacheSimulationTests
    {
        public BlobWriteNeverBuildCacheSimulationTests()
            : base(StorageOption.Blob)
        {
        }
    }
}
