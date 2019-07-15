// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Tests;

namespace Test.Cache.MemoizationStoreAdapter
{
    public class VerticalAggregatorTests : VerticalAggregatorSharedTests
    {
        protected override bool ImplementsTrackedSessions => false;

        protected override Type ReferenceType => typeof(TestInMemory);

        protected override Type TestType => typeof(MemoizationStoreAdapterLocalCacheTests);

        protected override bool CacheStoreCannotBeRemote => true;

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];
    }
}
