// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
