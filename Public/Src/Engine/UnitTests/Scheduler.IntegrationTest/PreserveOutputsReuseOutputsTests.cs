// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "PreserveOutputsReuseOutputsTests")]
    public class PreserveOutputsReuseOutputsTests : PreserveOutputsTests
    {
        public PreserveOutputsReuseOutputsTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.ReuseOutputsOnDisk = true;
        }
    }
}
