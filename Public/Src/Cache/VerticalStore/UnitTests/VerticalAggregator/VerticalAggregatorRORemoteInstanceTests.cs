// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Tests the vertical aggregator against a remote that is read only.
    /// </summary>
    public sealed class VerticalAggregatorRORemoteInstanceTests : VerticalAggregatorInstanceTests
    {
        protected override string ReadOnly => "true";
    }
}
