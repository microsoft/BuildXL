// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
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
