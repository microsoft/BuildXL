// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
