// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true, requiresSymlinkPermission: true)]
    public class SymlinkTests_IncrementalScheduling : SymlinkTests
    {
        public SymlinkTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }
    }
}
