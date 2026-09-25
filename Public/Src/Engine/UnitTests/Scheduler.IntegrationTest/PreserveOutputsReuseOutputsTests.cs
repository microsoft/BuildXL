// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "PreserveOutputsReuseOutputsTests")]
    [TestClassIfSupported(requiresSandbox: true)]
    public class PreserveOutputsReuseOutputsTests : PreserveOutputsTests
    {
        public PreserveOutputsReuseOutputsTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.ReuseOutputsOnDisk = true;
        }
    }
}
