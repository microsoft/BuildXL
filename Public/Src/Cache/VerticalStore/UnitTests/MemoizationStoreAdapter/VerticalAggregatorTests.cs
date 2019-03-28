// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.Tests;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

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
