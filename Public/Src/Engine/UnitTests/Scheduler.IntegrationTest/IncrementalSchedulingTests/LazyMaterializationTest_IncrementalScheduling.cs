// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    /// <summary>
    /// We test incremental scheduing in a seperate class so that we can control the
    /// requirment of journal scanning (see: [TheoryIfSupported(requiresJournalScan: true)] in LazyMaterializationTest below).
    /// </summary>
    [Trait("Category", "LazyMaterializationTests")]
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class LazyMaterializationTest_IncrementalScheduling : LazyMaterializationTests
    {
        public LazyMaterializationTest_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }
    }
}
