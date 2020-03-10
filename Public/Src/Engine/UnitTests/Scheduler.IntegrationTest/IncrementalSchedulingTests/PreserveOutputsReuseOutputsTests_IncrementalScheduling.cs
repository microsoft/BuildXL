// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Trait("Category", "PreserveOutputsReuseIncSchedTests")]
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class PreserveOutputsReuseOutputsTests_IncrementalScheduling : PreserveOutputsReuseOutputsTests
    {
        public PreserveOutputsReuseOutputsTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }
    }
}
