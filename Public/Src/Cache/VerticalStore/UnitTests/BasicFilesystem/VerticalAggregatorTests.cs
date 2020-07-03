// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Tests;

namespace Test.Cache.BasicFilesystem
{
    public class VerticalAggregatorTests : VerticalAggregatorSharedTests
    {
        protected override Type ReferenceType => typeof(TestInMemory);

        protected override Type TestType => typeof(TestBasicFilesystem);

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];
    }
}
