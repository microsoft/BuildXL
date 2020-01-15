// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;

using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class SharedOpaqueDirectoryTests_IncrementalScheduling : SharedOpaqueDirectoryTests
    {
        public SharedOpaqueDirectoryTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }
    }
}
