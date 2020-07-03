// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipGraphFragmentWithTopSortTests : PipGraphFragmentTests
    {
        public PipGraphFragmentWithTopSortTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <inheritdoc />
        protected override TestPipGraphFragment CreatePipGraphFragmentTest(string moduleName)
        {
            return CreatePipGraphFragment(moduleName, useTopSort: true);
        }
    }
}
