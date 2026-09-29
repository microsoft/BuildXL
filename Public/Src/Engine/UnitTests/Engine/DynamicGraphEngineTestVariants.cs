// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Test.BuildXL.EngineTests.DynamicGraph
{
    public class DynamicDoubleWriteFailureTests : global::Test.BuildXL.EngineTests.DoubleWriteFailureTests
    {
        public DynamicDoubleWriteFailureTests(ITestOutputHelper output)
            : base(output)
        {
            Configuration.Engine.UnsafeEnableDynamicGraph = true;
            Configuration.Schedule.IncrementalScheduling = false;
            Configuration.Cache.CacheGraph = false;
        }
    }
}

namespace Test.BuildXL.Engine.DynamicGraph
{
    public class DynamicRewriteTests : global::Test.BuildXL.Engine.RewriteTests
    {
        public DynamicRewriteTests(ITestOutputHelper output)
            : base(output)
        {
            Configuration.Engine.UnsafeEnableDynamicGraph = true;
            Configuration.Schedule.IncrementalScheduling = false;
            Configuration.Cache.CacheGraph = false;
        }
    }
}
