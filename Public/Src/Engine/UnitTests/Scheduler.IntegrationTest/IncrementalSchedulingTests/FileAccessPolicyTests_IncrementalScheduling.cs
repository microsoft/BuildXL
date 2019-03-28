// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Trait("Category", "FileAccessPolicyTests")]
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class FileAccessPolicyTests_IncrementalScheduling : FileAccessPolicyTests
    {
        public FileAccessPolicyTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }

        [Fact(Skip = "Bug 1087986")]
        public override void ValidateCachingUndefinedMount_Bug1087986()
        {
            base.ValidateCachingUndefinedMount_Bug1087986();
        }
    }
}
