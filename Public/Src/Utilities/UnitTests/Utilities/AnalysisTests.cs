// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class AnalysisTests : XunitBuildXLTest
    {
        public AnalysisTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void IgnoreResults()
        {
            // this function is a NOP on purpose, so there's nothing to actual test except the
            // fact it executes and doesn't crash
            Analysis.IgnoreResult(1234);
        }
    }
}
