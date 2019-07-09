// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
